#nullable enable

using System;
using System.Runtime.InteropServices;

namespace MMP.Herald.Interop;

/// <summary>
/// Bindings for the Rust native template parser. The C, Go, and Zig
/// backends were retired alongside the benchmark trim — Rust is the
/// one native parser <see cref="NativeDispatch"/> probes for, with
/// the managed C# parser as the fallback. Lifecycle shape: Bind /
/// Unbind / Parse / Version, auto-bind on first call, deferred-free
/// on Unload via <see cref="NativeDispatch"/>.
/// </summary>
public static class RustTemplateParse
{
    /// <summary>
    /// The native library name without extension.
    /// <see cref="NativeDispatch"/> uses this for the runtime probe.
    /// </summary>
    public const string DllName = "herald_native_rust";

    private static unsafe volatile delegate* unmanaged[Cdecl]<uint> _versionFn;
    private static unsafe volatile delegate* unmanaged[Cdecl]<
        byte*, uint, ParsedToken*, uint, ParseResult*, uint> _parseFn;

    [StructLayout(LayoutKind.Sequential)]
    public struct ParsedToken
    {
        public byte Kind;         // 0 = text, 1 = property
        public byte CaptureMode;  // 0 = default, 1 = destructure, 2 = stringify
        public byte Pad0;
        public byte Pad1;

        public uint NameStart;
        public uint NameEnd;
        public uint FormatStart;
        public uint FormatEnd;
        public uint ValueStart;
        public uint ValueEnd;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ParseResult
    {
        public uint TokenCount;
        public uint ErrorCode;    // 0 = success, 1 = unclosed, 2 = empty, 3 = buf too small
        public uint ErrorOffset;
        public uint Pad;
    }

    private static volatile bool _autoBindAttempted;
    private static IntPtr _autoLoadedHandle = IntPtr.Zero;
    private static readonly object _autoBindLock = new();

    /// <summary>Bind function pointers from a loaded native library handle.</summary>
    public static unsafe void Bind(IntPtr libraryHandle)
    {
        _versionFn = (delegate* unmanaged[Cdecl]<uint>)
            NativeLibrary.GetExport(libraryHandle, "herald_native_version");
        _parseFn = (delegate* unmanaged[Cdecl]<byte*, uint, ParsedToken*, uint, ParseResult*, uint>)
            NativeLibrary.GetExport(libraryHandle, "herald_parse_template");
    }

    /// <summary>Drop function pointers. Pair with <see cref="NativeLibrary.Free(IntPtr)"/>.</summary>
    public static unsafe void Unbind()
    {
        _versionFn = null;
        _parseFn = null;
    }

    /// <summary>True when function pointers are currently bound.</summary>
    public static unsafe bool IsBound => _parseFn != null;

    /// <summary>The handle owned by the auto-bind path, or zero if none.</summary>
    public static IntPtr AutoLoadedHandle => _autoLoadedHandle;

    /// <summary>Read the native parser's version number.</summary>
    /// <exception cref="DllNotFoundException">No bound library.</exception>
    public static unsafe uint Version()
    {
        var fn = _versionFn;
        if (fn == null)
        {
            EnsureAutoBind();
            fn = _versionFn;
            if (fn == null) throw new DllNotFoundException(DllName);
        }
        return fn();
    }

    /// <summary>
    /// Parse a template using the Rust native parser.
    /// </summary>
    /// <exception cref="DllNotFoundException">
    /// Auto-bind ran and failed (no <c>herald_native_rust</c> dll alongside
    /// the host), and no explicit <see cref="Bind"/> has succeeded since.
    /// </exception>
    public static int Parse(byte[] templateUtf8, ParsedToken[] tokenBuffer, out ParseResult result)
    {
        ArgumentNullException.ThrowIfNull(templateUtf8);
        ArgumentNullException.ThrowIfNull(tokenBuffer);

        unsafe
        {
            var fn = _parseFn;
            if (fn == null)
            {
                EnsureAutoBind();
                fn = _parseFn;
                if (fn == null) throw new DllNotFoundException(DllName);
            }

            fixed (byte* tPtr = templateUtf8)
            fixed (ParsedToken* bPtr = tokenBuffer)
            {
                // Defensive checked cast at the managed boundary — a
                // negative int would otherwise wrap to ~4 GB on the
                // native side.
                ParseResult res;
                var count = fn(
                    tPtr,
                    checked((uint)templateUtf8.Length),
                    bPtr,
                    checked((uint)tokenBuffer.Length),
                    &res);
                result = res;
                return (int)count;
            }
        }
    }

    private static void EnsureAutoBind()
    {
        if (_autoBindAttempted) return;
        lock (_autoBindLock)
        {
            if (_autoBindAttempted) return;
            _autoBindAttempted = true;
            if (NativeLibrary.TryLoad(DllName, typeof(RustTemplateParse).Assembly, null, out var handle))
            {
                _autoLoadedHandle = handle;
                Bind(handle);
            }
        }
    }

    /// <summary>
    /// Reset the auto-bind sticky flag so the next <see cref="Parse"/> /
    /// <see cref="Version"/> retries the library load. Used by
    /// <see cref="NativeDispatch.Reload"/>.
    /// </summary>
    public static void ResetAutoBindAttempt()
    {
        lock (_autoBindLock)
        {
            _autoBindAttempted = false;
            _autoLoadedHandle = IntPtr.Zero;
        }
    }
}
