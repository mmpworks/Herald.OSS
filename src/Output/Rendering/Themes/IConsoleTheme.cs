#nullable enable

namespace MMP.Herald.Output.Rendering.Themes;

/// <summary>
/// Resolves styling for any output element in console rendering.
/// The qualifier provides context: level key for level elements, property name for property elements, etc.
/// Returns null for unstyled elements.
/// </summary>
public interface IConsoleTheme
{
    OutputStyle? Resolve(OutputElementKind element, string? qualifier = null);
}
