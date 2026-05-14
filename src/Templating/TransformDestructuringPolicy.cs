#nullable enable

using System;

namespace MMP.Herald.Templating;

/// <summary>
/// A <see cref="IDestructuringPolicy"/> that matches values of type
/// <typeparamref name="T"/> and projects them through a caller-supplied
/// function before they reach the renderer. Parallels Serilog's
/// <c>.Destructure.ByTransforming&lt;T&gt;(r =&gt; new { r.Path, r.Method })</c>
/// shape.
/// </summary>
/// <remarks>
/// <para>
/// The policy is the building block <see cref="Quick.QuickLogBuilder.Destructure{T}"/>
/// wraps — operators rarely construct this directly, but it's public so
/// addon authors can register bespoke policies through the same
/// registry path as the transform-style rule.
/// </para>
/// <para>
/// The projection runs per matching value during rendering. The
/// returned object is rendered to a string via the supplied
/// <see cref="IComplexValueSerializer"/>. Herald.Core ships
/// <see cref="ToStringComplexValueSerializer"/> as the AOT-clean
/// default — output is the projection's <c>ToString</c>. For richer
/// output (structured JSON, graceful per-subtree depth limiting,
/// string truncation), install
/// <c>MMP.Herald.Plugins.Serialization.Reflection</c> or a
/// source-generated equivalent and register it via
/// <c>QuickLogBuilder.WithComplexValueSerializer(...)</c>.
/// </para>
/// </remarks>
public sealed class TransformDestructuringPolicy<T> : IDestructuringPolicy
{
    private readonly Func<T, object?> _projection;
    private readonly IComplexValueSerializer _serializer;

    /// <summary>
    /// Build a policy that matches values of type <typeparamref name="T"/>
    /// and renders them via <see cref="ToStringComplexValueSerializer"/>.
    /// </summary>
    public TransformDestructuringPolicy(Func<T, object?> projection)
        : this(projection, ToStringComplexValueSerializer.Instance)
    {
    }

    /// <summary>
    /// Build a policy with an explicit <see cref="IComplexValueSerializer"/>.
    /// The builder uses this overload to thread a consumer-installed
    /// plugin through to every <c>Destructure&lt;T&gt;(...)</c> policy.
    /// </summary>
    public TransformDestructuringPolicy(
        Func<T, object?> projection,
        IComplexValueSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(serializer);
        _projection = projection;
        _serializer = serializer;
    }

    /// <inheritdoc />
    public bool TryDestructure(object value, out string? result)
    {
        if (value is not T typed)
        {
            result = null;
            return false;
        }

        var projected = _projection(typed);
        // Null projection is a legal way for the caller to say "render
        // nothing" for this type — honour it with a literal "null" so
        // the output shape stays deterministic.
        result = projected is null ? "null" : _serializer.Serialize(projected);
        return true;
    }
}
