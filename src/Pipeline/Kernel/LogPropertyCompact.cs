#nullable enable

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Value-type equivalent of <see cref="Templating.LogProperty"/> for hot-path
/// scenarios. Four of LogProperty's five fields (capture mode, format,
/// visibility, lazy factory) are unused by the overwhelming majority of call
/// sites; holding just <c>Name</c> and <c>Value</c> in a struct lets the
/// kernel pipeline transport properties through a
/// <c>ReadOnlySpan&lt;LogPropertyCompact&gt;</c> without paying the per-
/// property heap allocation that <see cref="Templating.LogProperty"/>
/// currently requires.
///
/// <para>
/// This is infrastructure — the production path (StructuredLogger,
/// LogEvent, every sink) still uses <see cref="Templating.LogProperty"/>.
/// The compact form exists so future work can:
/// </para>
/// <list type="bullet">
///   <item>Add <c>LogEventBuffer</c> overloads that accept compact spans.</item>
///   <item>Migrate the zero-alloc call-site handlers
///         (<c>StructuredLogInterpolatedStringHandler</c> and friends)
///         to emit compact values.</item>
///   <item>Give sinks that do not need the full <see cref="Templating.LogProperty"/>
///         shape a parallel code path that stays struct-based.</item>
/// </list>
///
/// <para>
/// Implicit-conversion helpers below let callers pass compact values
/// wherever a <see cref="Templating.LogProperty"/> is expected — the
/// conversion pays one allocation but keeps callers flexible until the
/// broader migration lands.
/// </para>
/// </summary>
public readonly record struct LogPropertyCompact(string Name, object? Value)
{
    /// <summary>
    /// Inflate this compact property to the full record. Allocates a new
    /// <see cref="Templating.LogProperty"/>. Callers on a hot path should
    /// defer this conversion until the sink boundary.
    /// </summary>
    public Templating.LogProperty ToLogProperty() => new(Name, Value);

    public static implicit operator Templating.LogProperty(LogPropertyCompact compact) =>
        new(compact.Name, compact.Value);
}
