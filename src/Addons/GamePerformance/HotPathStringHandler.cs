#nullable enable

using System;
using System.Runtime.CompilerServices;
using System.Text;
using MMP.Herald.Levels;
using MMP.Herald.Pooling;

namespace MMP.Herald.Addons.GamePerformance;

/// <summary>
/// Custom interpolated string handler for HotPathLogger that achieves
/// zero allocation on rejection and pooled string building on acceptance.
///
/// How it works:
///   1. C# transforms $"Frame {n}: {ms}ms" into handler constructor + Append calls
///   2. Constructor receives the HotPathLogger + level via InterpolatedStringHandlerArgument
///   3. If IsEnabled returns false, sets shouldAppend=false → compiler skips all Appends
///   4. Zero heap allocation on rejection (handler is a stack-allocated ref struct)
///   5. On acceptance: appends to a pooled StringBuilder
///
/// Usage — the compiler transforms this automatically:
///   bare.Log(LogCategory.App, KnownLogLevels.Information, $"Frame {n}: {ms:F1}ms");
/// </summary>
[InterpolatedStringHandler]
public ref struct HotPathStringHandler
{
    private StringBuilder? _builder;
    private readonly bool _enabled;

    /// <summary>
    /// Constructor called by the C# compiler.
    /// 'logger' and 'level' come from InterpolatedStringHandlerArgument on the method.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HotPathStringHandler(int literalLength, int formattedCount,
        HotPathLogger logger, LogLevel level, out bool shouldAppend)
    {
        // Known-level fast path — read the precomputed accept bool via
        // ReferenceEquals on the singleton level instance. Turns the
        // common case (Debug/Info/Warn/Error/Trace/Critical) into a
        // cheap reference-equality chain + field read, skipping the
        // logger.IsEnabled hop's interface dispatch + dictionary
        // lookup. ReferenceEquals inlines to a single pointer compare.
        //
        // Falls back to the dynamic IsEnabled path for custom levels
        // (rare in the HotPath preset's target workloads).
        if (ReferenceEquals(level, KnownLogLevels.Information))
            _enabled = logger.IsInformationAcceptable;
        else if (ReferenceEquals(level, KnownLogLevels.Debug))
            _enabled = logger.IsDebugAcceptable;
        else if (ReferenceEquals(level, KnownLogLevels.Warning))
            _enabled = logger.IsWarningAcceptable;
        else if (ReferenceEquals(level, KnownLogLevels.Error))
            _enabled = logger.IsErrorAcceptable;
        else if (ReferenceEquals(level, KnownLogLevels.Verbose))
            _enabled = logger.IsVerboseAcceptable;
        else if (ReferenceEquals(level, KnownLogLevels.Fatal))
            _enabled = logger.IsFatalAcceptable;
        else
            _enabled = logger.IsEnabled(level);

        shouldAppend = _enabled;
        _builder = null; // lazy rent on first Append — zero alloc if rejected
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private StringBuilder EnsureBuilder()
    {
        return _builder ??= StringBuilderPool.Rent();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendLiteral(string s) { if (_enabled) EnsureBuilder().Append(s); }

    // Generic AppendFormatted<T> used to route through Append(object), which
    // boxes every value-type argument (int / long / double / ...) on its way
    // to Append's ToString path. The typeof(T) ladder below is folded at
    // generic specialization time — for T = int the JIT emits a direct call
    // to Append(int) with no box, and the branches for other Ts are dead
    // code. Unsafe.As<T, TConcrete>(ref value) reinterprets the generic slot
    // in place without an intermediate object box.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendFormatted<T>(T value)
    {
        if (!_enabled) return;
        var sb = EnsureBuilder();
        if (typeof(T) == typeof(int)) sb.Append(Unsafe.As<T, int>(ref value));
        else if (typeof(T) == typeof(long)) sb.Append(Unsafe.As<T, long>(ref value));
        else if (typeof(T) == typeof(double)) sb.Append(Unsafe.As<T, double>(ref value));
        else if (typeof(T) == typeof(float)) sb.Append(Unsafe.As<T, float>(ref value));
        else if (typeof(T) == typeof(bool)) sb.Append(Unsafe.As<T, bool>(ref value));
        else if (typeof(T) == typeof(char)) sb.Append(Unsafe.As<T, char>(ref value));
        else if (typeof(T) == typeof(uint)) sb.Append(Unsafe.As<T, uint>(ref value));
        else if (typeof(T) == typeof(ulong)) sb.Append(Unsafe.As<T, ulong>(ref value));
        else if (typeof(T) == typeof(short)) sb.Append(Unsafe.As<T, short>(ref value));
        else if (typeof(T) == typeof(byte)) sb.Append(Unsafe.As<T, byte>(ref value));
        else if (value is string s) sb.Append(s);
        else sb.Append(value);  // Reference types don't box; only non-primitive value types fall here.
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendFormatted<T>(T value, string? format)
    {
        if (!_enabled) return;
        var sb = EnsureBuilder();
        if (format is null)
        {
            // No format — same typeof ladder as the unformatted overload.
            if (typeof(T) == typeof(int)) sb.Append(Unsafe.As<T, int>(ref value));
            else if (typeof(T) == typeof(long)) sb.Append(Unsafe.As<T, long>(ref value));
            else if (typeof(T) == typeof(double)) sb.Append(Unsafe.As<T, double>(ref value));
            else if (typeof(T) == typeof(float)) sb.Append(Unsafe.As<T, float>(ref value));
            else if (typeof(T) == typeof(bool)) sb.Append(Unsafe.As<T, bool>(ref value));
            else if (value is string s) sb.Append(s);
            else sb.Append(value);
            return;
        }
        // Format specified — go through IFormattable if the value supports it,
        // otherwise fall back to Append(object).
        if (value is IFormattable formattable)
            sb.Append(formattable.ToString(format, null));
        else
            sb.Append(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendFormatted(string? value) { if (_enabled) EnsureBuilder().Append(value); }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendFormatted<T>(T value, int alignment, string? format = null)
    {
        if (!_enabled) return;
        var formatted = format is not null && value is IFormattable f
            ? f.ToString(format, null) : value?.ToString() ?? "";
        EnsureBuilder().Append(alignment > 0
            ? formatted.PadLeft(alignment)
            : formatted.PadRight(-alignment));
    }

    /// <summary>Returns the built string and returns the StringBuilder to the pool.</summary>
    internal string ToStringAndReturn()
    {
        if (_builder is null) return "";
        return StringBuilderPool.ReturnAndGetString(_builder);
    }

    internal bool IsEnabled => _enabled;
}
