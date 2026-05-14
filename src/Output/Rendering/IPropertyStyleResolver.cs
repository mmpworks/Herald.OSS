#nullable enable

namespace MMP.Herald.Output.Rendering;

/// <summary>
/// Resolves per-property styling for console output.
/// Returns null for properties without configured styling (default: unstyled).
/// </summary>
public interface IPropertyStyleResolver
{
    PropertyStyle? Resolve(string propertyName);
}
