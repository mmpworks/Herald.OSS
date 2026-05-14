#nullable enable

namespace MMP.Herald.Output.Rendering;

/// <summary>
/// Style definition for a log category (also called a "channel" on the
/// presentation side). Applied to the category fragment in the console
/// transformer, and exposed to downstream UIs through the management API
/// so consumers can paint channel chips / row tints against the same
/// palette the log stream uses.
///
/// Parallels <see cref="LogLevelStyle"/> and <see cref="PropertyStyle"/>;
/// the three resolvers together cover every coloured fragment the
/// configurable console theme emits.
/// </summary>
public sealed record CategoryStyle(
    string? ColorName = null,
    bool UseBold = false,
    bool UseItalic = false,
    string? BackgroundColorName = null);
