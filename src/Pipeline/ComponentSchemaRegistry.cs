#nullable enable

using System.Collections.Generic;
using MMP.Herald.Quick;
using MMP.Herald.Routing;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Static registry of default configuration schemas for pipeline components.
/// References the <c>DefaultSchema</c> on each component class — single source of truth.
///
/// Used when a component is in the strategy but not instantiated (e.g. async
/// is in the step list but not enabled). The dashboard can still show what
/// fields WOULD be configurable.
///
/// Live components provide runtime values via IComponentMetadata.ConfigurationSchema
/// (which uses <c>DefaultSchema[n] with { DefaultValue = runtimeValue }</c>).
///
/// <para>
/// <b>Hosting model.</b> Forwards every call to <see cref="HeraldHost.Default"/>'s
/// <see cref="ComponentSchemaKindRegistry"/>. Tests and multi-tenant hosts
/// that want isolated registry state construct their own <c>HeraldHost</c>
/// and consume <c>host.ComponentSchemas</c> directly.
/// </para>
/// </summary>
public static class ComponentSchemaRegistry
{
    /// <summary>
    /// Get the default schema for a step name. Returns null if no schema is registered.
    /// </summary>
    public static IReadOnlyList<SinkConfigField>? GetSchema(string stepName) =>
        HeraldHost.Default.ComponentSchemas.GetSchema(stepName);

    /// <summary>
    /// Register a custom schema for a plugin step.
    /// </summary>
    public static void Register(string stepName, IReadOnlyList<SinkConfigField> schema) =>
        HeraldHost.Default.ComponentSchemas.Register(stepName, schema);
}
