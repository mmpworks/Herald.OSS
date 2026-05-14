#nullable enable

namespace MMP.Herald.Templating;

/// <summary>
/// Converts a structured value to its string representation when the {@Name} capture mode is used.
/// Policies are evaluated in registration order; the first match wins.
/// Implement this to customize how complex types render in log messages.
/// </summary>
public interface IDestructuringPolicy
{
    bool TryDestructure(object value, out string? result);
}
