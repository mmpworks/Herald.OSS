#nullable enable

namespace MMP.Herald.Templating;

/// <summary>
/// Policy that matches values of type <typeparamref name="T"/> and
/// short-circuits the destructuring chain by rendering the value
/// through <see cref="object.ToString()"/>. Parallels Serilog's
/// <c>.Destructure.AsScalar&lt;T&gt;()</c>.
/// </summary>
/// <remarks>
/// <para>
/// Useful when a parent-type policy (e.g. one registered via
/// <c>Destructure&lt;IEntity&gt;(...)</c>) would otherwise project a
/// specific subtype, but you want that subtype to fall through to its
/// own <c>ToString</c> instead. Register the AsScalar policy
/// <em>before</em> the parent-type policy — the chain is first-match-wins,
/// so the AsScalar entry wins for matching values.
/// </para>
/// <para>
/// Zero allocations beyond what <c>ToString</c> itself produces.
/// AOT-clean — no reflection, no JsonSerializer.
/// </para>
/// </remarks>
public sealed class AsScalarDestructuringPolicy<T> : IDestructuringPolicy
{
    /// <inheritdoc />
    public bool TryDestructure(object value, out string? result)
    {
        if (value is not T)
        {
            result = null;
            return false;
        }

        result = value.ToString() ?? "null";
        return true;
    }
}
