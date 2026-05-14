#nullable enable

using System;
using System.Collections.Generic;

namespace MMP.Herald.Routing;

/// <summary>
/// Extended sink provider that describes its configurable properties.
/// Implement this instead of <see cref="ILogSinkProvider"/> when your sink
/// needs runtime configuration from the builder, dashboard, or management API.
///
/// The <see cref="ConfigurationSchema"/> tells the dashboard what fields
/// to render, what types they accept, and what defaults to use. The
/// <see cref="GetConfiguration"/> and <see cref="ApplyConfiguration"/>
/// methods let the management API read and write the sink's settings
/// without knowing the concrete type.
///
/// Usage:
///   public class MyCustomSink : IConfigurableSinkProvider
///   {
///       public string SinkKind => "my_custom";
///       public IReadOnlyList&lt;SinkConfigField&gt; ConfigurationSchema => [
///           SinkConfigField.String("endpoint", "https://logs.example.com", "Target URL"),
///           SinkConfigField.Int("batchSize", 100, "Events per batch"),
///           SinkConfigField.Bool("compress", true, "Enable gzip"),
///       ];
///       // ...
///   }
///
/// Register with the builder:
///   builder.WithCustomSinkProvider(new MyCustomSink());
///
/// The dashboard discovers it via GET /api/sinkProviders and renders
/// a configuration form automatically.
/// </summary>
public interface IConfigurableSinkProvider : ILogSinkProvider
{
    /// <summary>
    /// Human-readable display name for the dashboard.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Short description of what this sink does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Schema of configurable fields. The dashboard renders these as form inputs.
    /// </summary>
    IReadOnlyList<SinkConfigField> ConfigurationSchema { get; }

    /// <summary>
    /// Get the current configuration values as a dictionary.
    /// Keys match the <see cref="SinkConfigField.Name"/> values from the schema.
    /// </summary>
    IReadOnlyDictionary<string, object?> GetConfiguration();

    /// <summary>
    /// Apply configuration values. Keys match the schema field names.
    /// Unknown keys are ignored. Invalid values return false with a message.
    /// Called by the management API during transaction commit.
    /// </summary>
    (bool Success, string? Error) ApplyConfiguration(IReadOnlyDictionary<string, object?> values);
}

/// <summary>
/// Describes a single configurable field on a sink provider.
/// Used by the dashboard to render configuration forms dynamically.
/// </summary>
/// <summary>
/// Describes a single configurable field on a sink or pipeline component.
///
/// Grouping: fields with the same <see cref="Group"/> value are visually grouped
/// in the dashboard and shown/hidden together by a boolean toggle field.
/// The toggle field is the Bool field whose Name matches the group
/// (e.g., <c>enableRolling</c> toggles group <c>"rolling"</c>).
/// The toggle itself has no Group (always visible). Grouped fields have
/// <c>Group = "rolling"</c> and are only shown when the toggle is true.
/// </summary>
public sealed record SinkConfigField(
    string Name,
    string FieldType,
    object? DefaultValue,
    string Description,
    bool Required = false,
    IReadOnlyList<string>? Options = null,
    string Help = "",
    string Group = "",
    string Row = "",
    int Span = 1)
{
    // -- Factory methods for common field types --

    public static SinkConfigField String(string name, string? defaultValue, string description, string help = "", bool required = false, string group = "") =>
        new(name, "string", defaultValue, description, required, Help: help, Group: group);

    public static SinkConfigField Int(string name, int defaultValue, string description, string help = "", bool required = false, string group = "") =>
        new(name, "int", defaultValue, description, required, Help: help, Group: group);

    public static SinkConfigField Bool(string name, bool defaultValue, string description, string help = "", string group = "") =>
        new(name, "bool", defaultValue, description, Help: help, Group: group);

    public static SinkConfigField Choice(string name, string defaultValue, string description, string help, string group, params string[] options) =>
        new(name, "choice", defaultValue, description, Options: options, Help: help, Group: group);

    public static SinkConfigField Choice(string name, string defaultValue, string description, string help, params string[] options) =>
        new(name, "choice", defaultValue, description, Options: options, Help: help);

    public static SinkConfigField Choice(string name, string defaultValue, string description, params string[] options) =>
        new(name, "choice", defaultValue, description, Options: options);

    public static SinkConfigField Password(string name, string description, string help = "", string group = "") =>
        new(name, "password", null, description, Required: true, Help: help, Group: group);
}
