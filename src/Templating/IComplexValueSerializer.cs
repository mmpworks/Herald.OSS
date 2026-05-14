#nullable enable

namespace MMP.Herald.Templating;

/// <summary>
/// Plug point for rendering an object value into a string. Used by
/// <see cref="TransformDestructuringPolicy{T}"/> to serialize the
/// projection's output, by formatters that want richer-than-ToString
/// rendering of complex property values, and by any other extension
/// that needs an opt-in serialization strategy.
/// </summary>
/// <remarks>
/// <para>
/// Herald.Core ships <see cref="ToStringComplexValueSerializer"/> as
/// the default. It's AOT-clean (no reflection, no JsonSerializer) and
/// produces predictable <c>ToString</c> output. Consumers who want
/// richer behavior — graceful per-subtree depth limiting, real
/// structured JSON, source-generated trim-safe rendering — install a
/// plugin from <c>Herald.Plugins</c> and call
/// <see cref="Quick.QuickLogBuilder.WithComplexValueSerializer(IComplexValueSerializer)"/>
/// (or the plugin's own builder extension).
/// </para>
/// <para>
/// Implementations that use reflection or
/// <see cref="System.Text.Json.JsonSerializer"/> on arbitrary types
/// must mark their public surface with
/// <c>[RequiresUnreferencedCode]</c> and <c>[RequiresDynamicCode]</c>
/// so consumers see honest AOT signals at their own call site.
/// </para>
/// </remarks>
public interface IComplexValueSerializer
{
    /// <summary>
    /// Render <paramref name="value"/> as a string. Implementations
    /// decide the shape — <c>ToString</c>, JSON, dotted-path key/value
    /// dumps, anything else. The result is rendered verbatim into the
    /// destructuring path's output, so JSON implementations should
    /// return well-formed JSON.
    /// </summary>
    string Serialize(object value);
}
