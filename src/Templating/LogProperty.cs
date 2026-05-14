#nullable enable

using System;

namespace MMP.Herald.Templating;

/// <summary>
/// A named structured property captured alongside a rendered log message.
/// Raw values stay attached to the event so formatting remains a later concern.
///
/// Properties have two orthogonal axes:
/// - CaptureMode controls how the value is serialized (Default, Destructure, Stringify).
/// - Visibility controls whether the value appears in rendered message text
///   (Rendered) or is carried silently as metadata (Silent).
///
/// Properties support lazy evaluation: when Value is a Func&lt;object?&gt;, it is
/// invoked on first access via ResolvedValue. Use LogProperty.Lazy() to create
/// lazy properties whose values are deferred past filtering.
/// </summary>
// Converted from `sealed record` to `readonly record struct` so an array
// of LogProperty values holds its payload inline rather than as a pointer
// chain of heap-allocated property records. Every materialised LogEvent
// that carries N properties drops from N * (~40-48 B) of per-property
// heap allocs to a single contiguous N * sizeof(LogProperty) array-inline
// layout. The external API is unchanged — positional syntax, factory
// methods, CaptureModeOrDefault / VisibilityOrDefault / IsSilent /
// ResolvedValue all behave identically. The only semver-visible
// difference: LogProperty now has a default (zeroed) value, where a
// record class reference could be null. Callers that relied on the
// reference-type nullable distinction get `LogProperty?` (nullable
// value type) instead — same code, same semantics.
public readonly record struct LogProperty(
    string Name,
    object? Value,
    LogPropertyCaptureMode? CaptureMode = null,
    string? Format = null,
    LogPropertyVisibility? Visibility = null)
{
    public LogPropertyCaptureMode CaptureModeOrDefault =>
        CaptureMode ?? LogPropertyCaptureMode.Default;

    public LogPropertyVisibility VisibilityOrDefault =>
        Visibility ?? LogPropertyVisibility.Rendered;

    /// <summary>
    /// True when this property should not appear in rendered message text.
    /// </summary>
    public bool IsSilent => VisibilityOrDefault == LogPropertyVisibility.Silent;

    /// <summary>
    /// Resolves the property value. If Value is a Func&lt;object?&gt;, invokes it.
    /// Otherwise returns Value directly. All rendering and serialization paths
    /// should use this instead of Value to support lazy evaluation.
    ///
    /// If the Func throws, the exception is caught and a descriptive fallback
    /// string is returned instead. Logging should never crash because a property
    /// factory failed.
    ///
    /// Note: the Func is invoked on every access (not cached). LogProperty is an
    /// immutable record and cannot hold mutable cache state. If the factory is
    /// expensive and the result is needed more than once, wrap it in Lazy&lt;T&gt;
    /// before passing to LogProperty.Lazy().
    ///
    /// Thread safety: concurrent calls to ResolvedValue are safe as long as the
    /// Func&lt;object?&gt; itself is thread-safe. The pattern check (Value is Func)
    /// reads an immutable field and does not race.
    /// </summary>
    public object? ResolvedValue
    {
        get
        {
            if (Value is not Func<object?> factory) return Value;

            try
            {
                return factory();
            }
            catch (Exception ex)
            {
                return $"[Lazy property '{Name}' threw {ex.GetType().Name}: {ex.Message}]";
            }
        }
    }

    /// <summary>
    /// Create a silent property - carried as metadata but not rendered in messages.
    /// </summary>
    public static LogProperty Silent(string name, object? value, LogPropertyCaptureMode? captureMode = null) =>
        new(name, value, captureMode, Visibility: LogPropertyVisibility.Silent);

    /// <summary>
    /// Create a lazy property whose value is computed only when accessed via ResolvedValue.
    /// Use for expensive operations (scene graph traversal, complex serialization)
    /// that should be deferred past filtering.
    /// </summary>
    public static LogProperty Lazy(string name, Func<object?> valueFactory,
        LogPropertyCaptureMode? captureMode = null,
        string? format = null,
        LogPropertyVisibility? visibility = null) =>
        new(name, valueFactory, captureMode, format, visibility);
}