#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Destructuring;

/// <summary>
/// Bridges a Serilog-shaped <see cref="Core.IDestructuringPolicy"/> (which
/// receives a value-factory and returns a <see cref="LogEventPropertyValue"/>
/// tree) to Herald's native <see cref="MMP.Herald.Templating.IDestructuringPolicy"/>
/// (which returns a rendered <c>string?</c>).
///
/// <para>
/// <strong>Security contract.</strong>  This bridge MUST fire the user policy.
/// A silent no-op is a PII leak.  If the P1 value-factory is not accessible,
/// the constructor throws <see cref="InvalidOperationException"/> immediately
/// rather than registering a policy that silently drops redaction work.
/// </para>
///
/// <para>
/// <strong>Design decision OD-5.</strong>  The P1 projector exposes
/// <see cref="LogEventValueProjector.DefaultValueFactory"/> as an internal
/// static property accessible from this assembly (same assembly).  The bridge
/// therefore takes the tree-bridge path: the user policy receives the real
/// factory, builds its tree, and the bridge serialises the resulting node to
/// the string Herald's native policy chain expects.
/// </para>
///
/// <para>
/// Tree-to-string serialisation uses a compact Herald-idiomatic format that
/// mirrors what Serilog itself produces for structured values:
/// <c>SecretBearingFixture { Name: "alice" }</c>.  This matches the output a
/// downstream sink would see from real Serilog, so the bridge is a semantic
/// substitution, not a lossy approximation.
/// </para>
///
/// <para>
/// Cognitive complexity note: the serialisation helpers are split by value
/// node type (Scalar / Structure / Sequence / Dictionary) to keep each branch
/// at a single level of abstraction.
/// </para>
/// </summary>
internal sealed class SerilogDestructuringPolicyBridge
    : MMP.Herald.Templating.IDestructuringPolicy
{
    private readonly Core.IDestructuringPolicy _userPolicy;
    private readonly ILogEventPropertyValueFactory _factory;

    /// <summary>
    /// Construct the bridge.  Throws if the P1 value-factory is not accessible --
    /// this is the loud-throw guard that prevents silent no-op registration.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the P1 projector factory is null (would indicate a future
    /// refactor removed the seam).  The message names the safe alternative.
    /// </exception>
    public SerilogDestructuringPolicyBridge(Core.IDestructuringPolicy userPolicy)
    {
        ArgumentNullException.ThrowIfNull(userPolicy);

        // Guard: if the factory is ever null (future refactor removes the P1 seam),
        // throw now so the caller sees a loud, actionable error rather than a
        // silently no-op'd redaction policy that leaks PII at runtime.
        _factory = LogEventValueProjector.DefaultValueFactory
            ?? throw new InvalidOperationException(
                "Cannot bridge raw IDestructuringPolicy: the P1 value-model projector is not " +
                "accessible as a public factory. " +
                "Use Destructure.ByTransforming<T> instead, which maps cleanly to Herald's " +
                "native destructuring surface without requiring the mirror projector.");

        _userPolicy = userPolicy;
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode(
        "Bridged destructuring policy may walk arbitrary object graphs via reflection.")]
    public bool TryDestructure(object value, out string? result)
    {
        if (value is null)
        {
            result = null;
            return false;
        }

        LogEventPropertyValue tree;
        bool handled;

        try
        {
            handled = _userPolicy.TryDestructure(value, _factory, out tree!);
        }
        catch
        {
            // A throwing policy must not crash the logging pipeline.
            // Treat it as a non-match and let the default projector handle the value.
            result = null;
            return false;
        }

        if (!handled)
        {
            result = null;
            return false;
        }

        result = SerialiseTree(tree);
        return true;
    }

    // Serialise the policy-returned tree to the compact Herald string form.
    // Format mirrors Serilog's default rendering for structured values:
    //   ScalarValue        -> "value" (strings quoted) or value.ToString()
    //   StructureValue     -> TypeTag { Prop: val, Prop2: val2 }
    //   SequenceValue      -> [val, val2]
    //   DictionaryValue    -> {("key": val), ("key2": val2)}
    private static string SerialiseTree(LogEventPropertyValue node)
    {
        return node switch
        {
            ScalarValue sv   => SerialiseScalar(sv),
            StructureValue s => SerialiseStructure(s),
            SequenceValue sq => SerialiseSequence(sq),
            DictionaryValue d => SerialiseDictionary(d),
            _               => node.ToString() ?? "null"
        };
    }

    private static string SerialiseScalar(ScalarValue sv)
    {
        if (sv.Value is null) return "null";
        if (sv.Value is string s) return string.Concat("\"", s, "\"");
        return sv.Value.ToString() ?? "null";
    }

    // Renders: TypeTag { Prop1: val1, Prop2: val2 }
    // When TypeTag is null (anonymous type): { Prop1: val1, Prop2: val2 }
    private static string SerialiseStructure(StructureValue s)
    {
        var parts = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(s.TypeTag))
        {
            parts.Append(s.TypeTag);
            parts.Append(' ');
        }
        parts.Append("{ ");
        bool first = true;
        foreach (var prop in s.Properties)
        {
            if (!first) parts.Append(", ");
            first = false;
            parts.Append(prop.Name);
            parts.Append(": ");
            parts.Append(SerialiseTree(prop.Value));
        }
        parts.Append(" }");
        return parts.ToString();
    }

    // Renders: [val1, val2, val3]
    private static string SerialiseSequence(SequenceValue sq)
    {
        var parts = new System.Text.StringBuilder("[");
        bool first = true;
        foreach (var elem in sq.Elements)
        {
            if (!first) parts.Append(", ");
            first = false;
            parts.Append(SerialiseTree(elem));
        }
        parts.Append(']');
        return parts.ToString();
    }

    // Renders: {("key": val), ("key2": val2)}
    private static string SerialiseDictionary(DictionaryValue d)
    {
        var parts = new System.Text.StringBuilder("{");
        bool first = true;
        foreach (var entry in d.Elements)
        {
            if (!first) parts.Append(", ");
            first = false;
            parts.Append('(');
            parts.Append(SerialiseScalar(entry.Key));
            parts.Append(": ");
            parts.Append(SerialiseTree(entry.Value));
            parts.Append(')');
        }
        parts.Append('}');
        return parts.ToString();
    }
}
