#nullable enable

namespace MMP.Herald.Templating;

/// <summary>
/// Default <see cref="IComplexValueSerializer"/> shipped with
/// Herald.Core. Renders any value via <see cref="object.ToString()"/>.
/// </summary>
/// <remarks>
/// <para>
/// AOT-clean — no reflection, no <see cref="System.Text.Json.JsonSerializer"/>,
/// nothing the trim or AOT analyzer flags. Output is whatever the
/// type's <c>ToString</c> produces; for records and anonymous types
/// that's a property dump, for ad-hoc classes it's the type name.
/// </para>
/// <para>
/// Consumers who want richer output — graceful per-subtree depth
/// limiting, structured JSON, source-generated rendering — install a
/// plugin from <c>Herald.Plugins</c> (e.g.
/// <c>MMP.Herald.Plugins.Serialization.Reflection</c>) and replace
/// the default via
/// <c>QuickLogBuilder.WithComplexValueSerializer(...)</c>.
/// </para>
/// </remarks>
public sealed class ToStringComplexValueSerializer : IComplexValueSerializer
{
    /// <summary>Singleton instance — the serializer is stateless.</summary>
    public static ToStringComplexValueSerializer Instance { get; } = new();

    /// <inheritdoc />
    public string Serialize(object value) => value.ToString() ?? "null";
}
