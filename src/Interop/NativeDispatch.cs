using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MMP.Herald.Interop;

/// <summary>
/// Runtime selection of the native hot-path backend.
///
/// <para>
/// The first read of <see cref="ActiveBackend"/> probes for Rust, then
/// falls back to the managed C# implementation in native/dotnet. The
/// chosen backend's binding (<see cref="RustTemplateParse"/>) is wired
/// up via <see cref="NativeLibrary.GetExport"/>-fetched function
/// pointers — not <c>[DllImport]</c> externs — so the loaded library
/// can be unloaded safely at runtime.
/// </para>
///
/// <para>
/// One stderr line is written when a backend is selected; suppress with
/// <c>HERALD_SILENT_NATIVE=1</c>. Game devs who wire
/// <c>HeraldEmbed.Console()</c> see one line at startup confirming whether
/// the Rust library loaded or the managed fallback is in use.
/// </para>
///
/// <para>
/// <b>Hot-load and hot-unload.</b> Both are supported.
/// </para>
/// <list type="bullet">
///   <item><description>
///     <see cref="Reload"/> re-runs the probe. If a library is already
///     bound, the OS keeps the same handle (re-load is a refcount bump);
///     if no library was bound and a dll has been dropped in alongside
///     the host, the new library is loaded and bound.
///   </description></item>
///   <item><description>
///     <see cref="Unload"/> unbinds the active backend's function
///     pointers, sleeps for the deferred-free window
///     (<see cref="DeferredUnloadWindow"/>) so any in-flight dispatch can
///     drain, then frees the OS handle and reverts to the managed
///     fallback. The default window is 100 ms; override before calling
///     <see cref="Unload"/> if your dispatch rate / latency profile
///     warrants something different.
///   </description></item>
///   <item><description>
///     <see cref="SwapTo"/> moves to a specific backend. Unload + Reload
///     for Rust; just unload for <see cref="NativeBackend.Managed"/>.
///   </description></item>
/// </list>
///
/// <para>
/// <b>Thread safety.</b> Concurrent <see cref="Reload"/> /
/// <see cref="Unload"/> / <see cref="SwapTo"/> calls serialise on a
/// private lock. Read-side properties (<see cref="ActiveBackend"/>,
/// <see cref="IsNativeLoaded"/>) are volatile reads and never block.
/// </para>
/// </summary>
// C# 12 note: kept static class form for .NET 8 compatibility. Revisit on C# 14
// because extension members on static classes may simplify the probe pattern.
public static class NativeDispatch
{
    private const string SuppressEnvVar = "HERALD_SILENT_NATIVE";

    private static volatile NativeBackend _backend = NativeBackend.Managed;
    private static IntPtr _ownedHandle = IntPtr.Zero;
    private static readonly object _stateLock = new();
    private static volatile bool _forceManaged;
    private static volatile bool _initialised;

    // Default deferred-free window. Parser dispatch is sub-microsecond;
    // 100 ms is six orders of magnitude headroom for any in-flight call to
    // drain before the OS handle is freed.
    private static volatile int _deferredUnloadWindowMs = 100;

    static NativeDispatch()
    {
        // Run the initial probe in the static constructor so the selection
        // line fires once, deterministically, on first type access.
        InitialProbe();
    }

    /// <summary>The native backend chosen at process startup, or after the most recent
    /// <see cref="Reload"/> / <see cref="Unload"/> / <see cref="SwapTo"/> call.</summary>
    public static NativeBackend ActiveBackend => _backend;

    /// <summary>True when the active backend is a mapped native library
    /// (<see cref="NativeBackend.Rust"/>).</summary>
    public static bool IsNativeLoaded => !ReferenceEquals(_backend, NativeBackend.Managed);

    /// <summary>
    /// How long <see cref="Unload"/> sleeps between unbinding the parser's
    /// function pointers and freeing the OS library handle. Default 100 ms.
    /// Set to <see cref="TimeSpan.Zero"/> to skip the window — only safe in
    /// single-threaded test scenarios where no in-flight dispatch is
    /// possible.
    /// </summary>
    public static TimeSpan DeferredUnloadWindow
    {
        get => TimeSpan.FromMilliseconds(_deferredUnloadWindowMs);
        set
        {
            var ms = (int)value.TotalMilliseconds;
            if (ms < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "DeferredUnloadWindow must be non-negative.");
            _deferredUnloadWindowMs = ms;
        }
    }

    /// <summary>
    /// Re-run the probe. Picks up a native dll dropped in alongside the host
    /// after process start.
    ///
    /// <para>
    /// If a backend is already loaded, <see cref="Reload"/> is a no-op
    /// because <see cref="NativeLibrary.TryLoad(string, System.Reflection.Assembly, System.Runtime.InteropServices.DllImportSearchPath?, out IntPtr)"/>
    /// returns the same OS-cached handle. To swap to a different backend,
    /// call <see cref="Unload"/> first or use <see cref="SwapTo"/>.
    /// </para>
    /// </summary>
    /// <returns>The backend after the probe.</returns>
    public static NativeBackend Reload()
    {
        lock (_stateLock)
        {
            // If we're forced to managed, the probe always picks Managed.
            if (_forceManaged)
            {
                EnsureManagedState_NoLock();
                EmitSelectionNote(NativeBackend.Managed);
                return NativeBackend.Managed;
            }

            // If a backend is already bound, the OS load is sticky — let
            // the auto-bind sticky flag clear so a missed probe gets
            // another chance, but don't unload first.
            RustTemplateParse.ResetAutoBindAttempt();

            var chosen = ProbeAndBind_NoLock();
            EmitSelectionNote(chosen);
            return chosen;
        }
    }

    /// <summary>
    /// Force the managed fallback even if a native library is on disk.
    /// Subsequent <see cref="Reload"/> calls also return Managed until
    /// process restart. If a native library is already bound,
    /// <see cref="RequireManaged"/> unloads it (with the deferred-free
    /// semantics of <see cref="Unload"/>).
    /// </summary>
    public static void RequireManaged()
    {
        lock (_stateLock)
        {
            _forceManaged = true;
            if (IsNativeLoaded)
            {
                Unload_NoLock();
            }
            else
            {
                EnsureManagedState_NoLock();
            }
            EmitSelectionNote(NativeBackend.Managed);
        }
    }

    /// <summary>
    /// Unload the active native backend back to the managed fallback.
    ///
    /// <para>
    /// Sequence: unbind the parser's function pointers (so no new dispatch
    /// starts on the loaded library), sleep for
    /// <see cref="DeferredUnloadWindow"/> so any in-flight dispatch
    /// completes, then free the OS handle. Sets
    /// <see cref="ActiveBackend"/> to <see cref="NativeBackend.Managed"/>.
    /// </para>
    ///
    /// <para>
    /// Idempotent — calling <see cref="Unload"/> when the active backend
    /// is already <see cref="NativeBackend.Managed"/> is a no-op.
    /// </para>
    /// </summary>
    public static void Unload()
    {
        lock (_stateLock)
        {
            Unload_NoLock();
            EmitSelectionNote(NativeBackend.Managed);
        }
    }

    /// <summary>
    /// Swap to a specific backend. Unloads the current backend (if native)
    /// then probes for the target. For
    /// <see cref="NativeBackend.Managed"/>, equivalent to <see cref="Unload"/>.
    /// For Rust, equivalent to <see cref="Unload"/> + <see cref="Reload"/>
    /// — only succeeds if the Rust library is on disk.
    /// </summary>
    /// <returns>The backend actually selected after the swap.</returns>
    public static NativeBackend SwapTo(NativeBackend target)
    {
        ArgumentNullException.ThrowIfNull(target);

        lock (_stateLock)
        {
            if (target == NativeBackend.Managed)
            {
                Unload_NoLock();
                EmitSelectionNote(NativeBackend.Managed);
                return NativeBackend.Managed;
            }

            // Unload first so the OS handle for the current backend
            // releases. Then re-probe — picks up Rust if it's on disk,
            // otherwise falls back to Managed.
            if (IsNativeLoaded)
            {
                Unload_NoLock();
            }

            RustTemplateParse.ResetAutoBindAttempt();

            var chosen = ProbeAndBind_NoLock();
            EmitSelectionNote(chosen);
            return chosen;
        }
    }

    // -- Internal lifecycle ----------------------------------------------------

    private static void InitialProbe()
    {
        lock (_stateLock)
        {
            if (_initialised) return;
            _initialised = true;
            var chosen = ProbeAndBind_NoLock();
            EmitSelectionNote(chosen);
        }
    }

    private static NativeBackend ProbeAndBind_NoLock()
    {
        // Try Rust.
        if (TryLoad(RustTemplateParse.DllName, out var rustHandle))
        {
            RustTemplateParse.Bind(rustHandle);
            _ownedHandle = rustHandle;
            _backend = NativeBackend.Rust;
            return NativeBackend.Rust;
        }

        // Fall back to managed.
        EnsureManagedState_NoLock();
        return NativeBackend.Managed;
    }

    private static void Unload_NoLock()
    {
        // Snapshot the active backend before mutating state.
        var current = _backend;

        if (ReferenceEquals(current, NativeBackend.Managed))
        {
            // Nothing to unload. Idempotent return.
            EnsureManagedState_NoLock();
            return;
        }

        // Step 1: clear the function pointers. New dispatches see null and
        // fail (Parse / Version throw DllNotFoundException). In-flight
        // dispatches that already loaded the pointer into a register keep
        // running against the still-mapped library.
        if (ReferenceEquals(current, NativeBackend.Rust))
        {
            RustTemplateParse.Unbind();
        }

        // Step 2: drain. Sleep for the deferred-free window so any
        // in-flight dispatch can complete before the OS unmaps the library.
        var window = _deferredUnloadWindowMs;
        if (window > 0)
        {
            Thread.Sleep(window);
        }

        // Step 3: free the handles we (or the auto-bind path) own.
        var owned = _ownedHandle;
        var autoLoaded = ReferenceEquals(current, NativeBackend.Rust)
            ? RustTemplateParse.AutoLoadedHandle
            : IntPtr.Zero;

        // Free both handles — the OS reference-counts loads, so freeing
        // the same library twice is correct when both NativeDispatch and
        // the auto-bind path independently loaded it. Skip Zero handles.
        if (owned != IntPtr.Zero)
        {
            try { NativeLibrary.Free(owned); } catch { /* best-effort */ }
            _ownedHandle = IntPtr.Zero;
        }
        if (autoLoaded != IntPtr.Zero && autoLoaded != owned)
        {
            try { NativeLibrary.Free(autoLoaded); } catch { /* best-effort */ }
        }

        // Reset auto-bind so a future Reload can retry from scratch.
        RustTemplateParse.ResetAutoBindAttempt();

        // Step 4: revert backend state.
        _backend = NativeBackend.Managed;
    }

    private static void EnsureManagedState_NoLock()
    {
        // The managed fallback has no allocated state to clean up, but
        // the auto-bind sticky flag should reflect "we're not native."
        RustTemplateParse.Unbind();
        _ownedHandle = IntPtr.Zero;
        _backend = NativeBackend.Managed;
    }

    private static void EmitSelectionNote(NativeBackend chosen)
    {
        // Honour the suppression env var so CI harnesses and tooling that
        // spawn many Herald processes can keep stderr clean.
        var suppress = Environment.GetEnvironmentVariable(SuppressEnvVar);
        if (!string.IsNullOrEmpty(suppress) && suppress != "0")
            return;

        try
        {
            Console.Error.WriteLine(
                $"[Herald] native template parser: {chosen.Name} (set {SuppressEnvVar}=1 to silence).");
        }
        catch
        {
            // stderr write failure is not a reason to fail startup; swallow.
        }
    }

    private static bool TryLoad(string libraryName, out IntPtr handle)
    {
        return NativeLibrary.TryLoad(libraryName, typeof(NativeDispatch).Assembly, null, out handle);
    }
}

public sealed record NativeBackend
{
    public static readonly NativeBackend Rust = new("Rust");
    public static readonly NativeBackend Managed = new("Managed");

    public string Name { get; }

    private NativeBackend(string name)
    {
        Name = name;
    }

    public override string ToString() => Name;
}
