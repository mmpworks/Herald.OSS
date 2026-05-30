#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Destructuring;

/// <summary>
/// Adapts a ByTransforming lambda into a Serilog-shaped IDestructuringPolicy.
/// Applies the projection function and then uses the value factory to build
/// the resulting LogEventPropertyValue tree.
/// This enables the ByTransforming path to participate in capture-time
/// policy application (via SerilogDestructuringApplicator) so secrets
/// stripped by the projection are excluded from LogEvent.Properties.
/// </summary>
internal sealed class ByTransformingPolicyAdapter<T> : IDestructuringPolicy
{
    private readonly Func<T, object?> _transform;

    internal ByTransformingPolicyAdapter(Func<T, object?> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        _transform = transform;
    }

    [RequiresUnreferencedCode(
        "ByTransforming projection may walk arbitrary object graphs via reflection.")]
    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        out LogEventPropertyValue result)
    {
        if (value is not T typed)
        {
            result = null!;
            return false;
        }

        var projected = _transform(typed);
        // Null projection means the caller wants to render nothing for this type.
        // Use an empty StructureValue (same shape as a zero-property object).
        result = projected is null
            ? new StructureValue(Array.Empty<LogEventProperty>())
            : propertyValueFactory.CreatePropertyValue(projected, destructureObjects: true);
        return true;
    }
}
