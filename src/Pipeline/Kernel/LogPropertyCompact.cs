#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Kind tag for the typed slot a <see cref="LogPropertyCompact"/>
/// carries. The kernel pipeline uses the tag to dispatch primitive
/// values directly from the slot without boxing; sinks that don't
/// care about the typed shape can read the lazily-boxed
/// <see cref="LogPropertyCompact.Value"/> property instead.
/// </summary>
public enum LogPropertyKind : byte
{
    Null = 0,
    Int32 = 1,
    Int64 = 2,
    Double = 3,
    Boolean = 4,
    DateTime = 5,
    /// <summary>String reference stored in <see cref="LogPropertyCompact.RefValue"/>.</summary>
    String = 6,
    /// <summary>Generic reference type stored in <see cref="LogPropertyCompact.RefValue"/>.</summary>
    Reference = 7,
    /// <summary>Pre-boxed value type stored in <see cref="LogPropertyCompact.RefValue"/>. Used by the legacy <c>(string, object?)</c> constructor path.</summary>
    Boxed = 8,
}

/// <summary>
/// Packed capture-mode axis carried by <see cref="LogPropertyCompact"/>.
/// One byte; rides in the struct's existing padding so the compact slot
/// stays 32 bytes. Maps to <see cref="MMP.Herald.Templating.LogPropertyCaptureMode"/>
/// for the full-record path.
/// </summary>
public enum CaptureMode : byte
{
    Default = 0,
    Destructure = 1,
    Stringify = 2,
}

/// <summary>
/// Value-type equivalent of <see cref="Templating.LogProperty"/> for hot-path
/// scenarios. Carries a name plus a typed slot — primitives live in
/// <see cref="ScalarBits"/> with a discriminator in <see cref="Kind"/>;
/// references live in <see cref="RefValue"/>.
///
/// <para>
/// The typed slot lets the kernel pipeline transport common primitive
/// values (int / long / double / bool / DateTime) without boxing. The
/// <see cref="Value"/> property returns a lazily-boxed <see cref="object"/>
/// for callers that prefer the legacy shape; primitive-aware sinks
/// (e.g. <see cref="MMP.Herald.Formatting.Utf8JsonFormatter"/>) read
/// <see cref="Kind"/> + the appropriate field directly.
/// </para>
///
/// <para>
/// Typical fill site (from the generated typed-args dispatcher):
/// </para>
/// <code>
/// buf[0] = LogPropertyCompact.From(name, userId);   // int → ScalarBits, no box
/// buf[1] = LogPropertyCompact.From(name, email);    // string → RefValue
/// buf[2] = LogPropertyCompact.From(name, latency);  // double → ScalarBits, no box
/// </code>
///
/// <para>
/// The 2-arg constructor <c>new LogPropertyCompact(name, value)</c>
/// remains for legacy callers; it stores the value in
/// <see cref="RefValue"/> (boxing value types) and tags
/// <see cref="Kind"/> as <see cref="LogPropertyKind.Boxed"/> /
/// <see cref="LogPropertyKind.Reference"/> / <see cref="LogPropertyKind.Null"/>
/// based on the runtime type. Sinks reading <see cref="Value"/> are
/// unaffected.
/// </para>
///
/// <para>
/// <b>Capture-mode carried; Format and Visibility not.</b> The typed slot
/// carries <see cref="Name"/>, <see cref="Kind"/>, <see cref="ScalarBits"/>,
/// <see cref="RefValue"/>, and — packed into the struct's existing padding —
/// the <see cref="CaptureMode"/> axis. A <c>{@Name}</c> (Destructure) or
/// <c>{$Name}</c> (Stringify) hole rides the compact slot directly; the
/// struct stays 32 bytes. The slot does <b>not</b> carry the remaining two
/// non-default axes of <see cref="Templating.LogProperty"/> —
/// <see cref="Templating.LogProperty.Format"/> or
/// <see cref="Templating.LogProperty.Visibility"/>. The design holds the
/// compact path to the common shape (no format, rendered visibility) and
/// routes any caller that needs a Format or Visibility axis through the
/// full <see cref="Templating.LogProperty"/> path.
/// </para>
///
/// <para>
/// In practice the compact path is filled exclusively by the
/// generated typed-args dispatcher and a handful of internal hot-path
/// call sites. The <c>HERALD014</c> analyzer flags any compact-path caller
/// that constructs a <see cref="Templating.LogProperty"/> with a
/// <b>Format or Visibility</b> axis (e.g. <c>LogProperty.Silent(...)</c>,
/// <c>LogProperty.Lazy(...)</c>, the named-axis constructor) and passes it
/// through the compact-path API. Severity is Warning under the default
/// build; consumers who enable
/// <c>&lt;HeraldStrictMode&gt;true&lt;/HeraldStrictMode&gt;</c>
/// (HRLD0002) escalate the warning to an error. See
/// <see cref="ToLogProperty"/> for the canonical-equivalence statement
/// that follows from this contract.
/// </para>
/// </summary>
public readonly struct LogPropertyCompact : IEquatable<LogPropertyCompact>
{
    /// <summary>Property name.</summary>
    public readonly string Name;

    /// <summary>Discriminator for the typed slot.</summary>
    public readonly LogPropertyKind Kind;

    /// <summary>
    /// Packed capture-mode axis. Occupies one of the 7 padding bytes next
    /// to <see cref="Kind"/>, so carrying it costs zero size growth — the
    /// struct stays 32 bytes on x64. <see cref="CaptureMode.Default"/> for
    /// the common (bare-hole) case.
    /// </summary>
    public readonly CaptureMode CaptureMode;

    /// <summary>
    /// Bit-cast storage for primitive value types. Zero for kinds that
    /// store their payload in <see cref="RefValue"/> instead.
    /// </summary>
    public readonly long ScalarBits;

    /// <summary>
    /// Reference-type payload for string / object / pre-boxed values.
    /// Null for primitive kinds and the null kind.
    /// </summary>
    public readonly object? RefValue;

    /// <summary>
    /// Legacy constructor: stores the value as a boxed reference. Value
    /// types reaching this constructor box at the call site. Use the
    /// generic <see cref="From{T}(string, T)"/> factory to get the
    /// typed-slot path that avoids the box for known primitive types.
    /// </summary>
    public LogPropertyCompact(string name, object? value)
    {
        Name = name;
        if (value is null)
        {
            Kind = LogPropertyKind.Null;
            ScalarBits = 0;
            RefValue = null;
        }
        else if (value is string)
        {
            Kind = LogPropertyKind.String;
            ScalarBits = 0;
            RefValue = value;
        }
        else if (value.GetType().IsValueType)
        {
            // Value type passed as object — already boxed at the call site.
            // Tag as Boxed so Value just returns the existing reference.
            Kind = LogPropertyKind.Boxed;
            ScalarBits = 0;
            RefValue = value;
        }
        else
        {
            Kind = LogPropertyKind.Reference;
            ScalarBits = 0;
            RefValue = value;
        }
    }

    private LogPropertyCompact(
        string name, LogPropertyKind kind, long scalarBits, object? refValue,
        CaptureMode captureMode = CaptureMode.Default)
    {
        Name = name;
        Kind = kind;
        ScalarBits = scalarBits;
        RefValue = refValue;
        CaptureMode = captureMode;
    }

    /// <summary>
    /// Generic factory. The .NET JIT specializes one body per value-type
    /// <typeparamref name="T"/> at first use, so the type-checks below
    /// fold to compile-time constants for each specialization. For
    /// <c>T = int</c> only the <see cref="LogPropertyKind.Int32"/> arm
    /// survives, and the value flows straight into <see cref="ScalarBits"/>
    /// without boxing. Reference types fall through to the legacy
    /// boxed-reference constructor.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LogPropertyCompact From<T>(string name, T value)
    {
        if (typeof(T) == typeof(int))
            return new LogPropertyCompact(name, LogPropertyKind.Int32, Unsafe.As<T, int>(ref value), null);
        if (typeof(T) == typeof(long))
            return new LogPropertyCompact(name, LogPropertyKind.Int64, Unsafe.As<T, long>(ref value), null);
        if (typeof(T) == typeof(double))
            return new LogPropertyCompact(name, LogPropertyKind.Double, BitConverter.DoubleToInt64Bits(Unsafe.As<T, double>(ref value)), null);
        if (typeof(T) == typeof(bool))
            return new LogPropertyCompact(name, LogPropertyKind.Boolean, Unsafe.As<T, bool>(ref value) ? 1 : 0, null);
        if (typeof(T) == typeof(DateTime))
            return new LogPropertyCompact(name, LogPropertyKind.DateTime, Unsafe.As<T, DateTime>(ref value).Ticks, null);

        if (typeof(T) == typeof(string))
            return new LogPropertyCompact(name, LogPropertyKind.String, 0, Unsafe.As<T, string?>(ref value));

        // Fallback: value type that didn't match a specialized arm
        // (custom struct, unsigned int, decimal) OR reference type
        // not specialized above. Routes through the legacy constructor,
        // which boxes value types once at this boundary.
        return new LogPropertyCompact(name, (object?)value);
    }

    /// <summary>
    /// Generic factory that also packs a capture-mode axis. When
    /// <paramref name="captureMode"/> resolves to <see cref="CaptureMode.Default"/>
    /// this is identical to <see cref="From{T}(string, T)"/> — same typed slot,
    /// same zero-box path. A Destructure / Stringify hole rides the compact slot
    /// with the mode set in the padding byte; no full-record allocation.
    /// </summary>
    public static LogPropertyCompact From<T>(
        string name, T value, MMP.Herald.Templating.LogPropertyCaptureMode? captureMode)
    {
        var mode = ToPackedMode(captureMode);
        if (mode == CaptureMode.Default) return From(name, value);
        var slot = From(name, value);
        return new LogPropertyCompact(slot.Name, slot.Kind, slot.ScalarBits, slot.RefValue, mode);
    }

    private static CaptureMode ToPackedMode(MMP.Herald.Templating.LogPropertyCaptureMode? mode)
    {
        if (mode is null) return CaptureMode.Default;
        if (ReferenceEquals(mode, MMP.Herald.Templating.LogPropertyCaptureMode.Destructure)
            || mode == MMP.Herald.Templating.LogPropertyCaptureMode.Destructure) return CaptureMode.Destructure;
        if (mode == MMP.Herald.Templating.LogPropertyCaptureMode.Stringify) return CaptureMode.Stringify;
        return CaptureMode.Default;
    }

    /// <summary>
    /// Lazily-boxed value. Returns the same reference shape callers
    /// got from the legacy <c>(string, object?)</c> constructor. The
    /// box is materialised only when <see cref="Value"/> is read;
    /// primitive-aware sinks that read <see cref="Kind"/> +
    /// <see cref="ScalarBits"/> directly never trigger the box.
    /// </summary>
    public object? Value => Kind switch
    {
        LogPropertyKind.Null     => null,
        LogPropertyKind.Int32    => (int)ScalarBits,
        LogPropertyKind.Int64    => ScalarBits,
        LogPropertyKind.Double   => BitConverter.Int64BitsToDouble(ScalarBits),
        LogPropertyKind.Boolean  => ScalarBits != 0,
        LogPropertyKind.DateTime => new DateTime(ScalarBits, DateTimeKind.Utc),
        _                        => RefValue,
    };

    /// <summary>
    /// The packed <see cref="CaptureMode"/> mapped back to the full-record
    /// <see cref="MMP.Herald.Templating.LogPropertyCaptureMode"/> singleton.
    /// </summary>
    public MMP.Herald.Templating.LogPropertyCaptureMode CaptureModeOrDefault => CaptureMode switch
    {
        CaptureMode.Destructure => MMP.Herald.Templating.LogPropertyCaptureMode.Destructure,
        CaptureMode.Stringify   => MMP.Herald.Templating.LogPropertyCaptureMode.Stringify,
        _                       => MMP.Herald.Templating.LogPropertyCaptureMode.Default,
    };

    /// <summary>
    /// Inflate this compact property to the full record. Allocates a new
    /// <see cref="Templating.LogProperty"/>. Callers on a hot path should
    /// defer this conversion until the sink boundary.
    ///
    /// <para>
    /// <b>Canonical equivalence.</b> The inflated record carries
    /// <see cref="Name"/>, <see cref="Value"/>, and the packed
    /// <see cref="CaptureMode"/> (propagated when non-default). Format and
    /// visibility default to the <see cref="Templating.LogProperty"/>
    /// defaults (null on the record fields, which resolve to no format and
    /// <c>Rendered</c> visibility through
    /// <see cref="Templating.LogProperty.VisibilityOrDefault"/>). Because the
    /// compact slot cannot represent the Format or Visibility axes (see the
    /// type-level remark above), the inflated record is canonically
    /// equivalent to the record a caller would have constructed directly with
    /// the same <c>(Name, Value, CaptureMode)</c> triple. No information is
    /// lost — there is no Format/Visibility information present to lose.
    /// </para>
    /// </summary>
    public Templating.LogProperty ToLogProperty() =>
        CaptureMode == CaptureMode.Default
            ? new(Name, Value)
            : new(Name, Value, CaptureModeOrDefault);

    // Forward to ToLogProperty() so the packed CaptureMode axis is preserved.
    // Raw construction (new(Name, Value)) dropped CaptureMode for Destructure /
    // Stringify holes riding the compact slot.
    public static implicit operator Templating.LogProperty(LogPropertyCompact compact) =>
        compact.ToLogProperty();

    public bool Equals(LogPropertyCompact other) =>
        Name == other.Name && Kind == other.Kind
        && CaptureMode == other.CaptureMode
        && ScalarBits == other.ScalarBits
        && Equals(RefValue, other.RefValue);

    public override bool Equals(object? obj) => obj is LogPropertyCompact other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Name, Kind, CaptureMode, ScalarBits, RefValue);

    public override string ToString() =>
        $"LogPropertyCompact {{ Name = {Name}, Value = {Value?.ToString() ?? "null"} }}";

    public static bool operator ==(LogPropertyCompact left, LogPropertyCompact right) => left.Equals(right);
    public static bool operator !=(LogPropertyCompact left, LogPropertyCompact right) => !left.Equals(right);
}
