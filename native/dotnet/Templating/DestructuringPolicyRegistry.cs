#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Templating;

/// <summary>
/// Ordered registry of destructuring policies evaluated during structured rendering.
/// The first policy that returns true wins; falls back to the built-in collection/ToString rendering.
/// </summary>
public sealed class DestructuringPolicyRegistry
{
    private readonly IReadOnlyList<IDestructuringPolicy> _policies;

    public DestructuringPolicyRegistry(IReadOnlyList<IDestructuringPolicy> policies)
    {
        _policies = policies;
    }

    public static DestructuringPolicyRegistry Empty { get; } = new([]);

    public bool TryDestructure(object value, out string? result)
    {
        foreach (var policy in _policies)
        {
            if (policy.TryDestructure(value, out result))
            {
                return true;
            }
        }

        result = null;
        return false;
    }
}
