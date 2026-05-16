#nullable enable

using System.IO;
using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;

namespace MMP.Herald.Routing;

/// <summary>
/// Plugin interface for creating a specific kind of log sink.
/// Each provider handles one sink kind (e.g., "console", "text_file").
/// </summary>
public interface ILogSinkProvider
{
    /// <summary>
    /// The lowercase sink kind this provider handles (e.g., "console", "text_file").
    /// </summary>
    string SinkKind { get; }

    /// <summary>
    /// Minimum Herald edition required to use this sink.
    /// Informational in Herald.OSS — no gate enforces it here. A downstream
    /// commercial wrapper reads this value to refuse sink registrations
    /// above the running tier; the Dashboard renders an edition badge.
    /// Default: Community (available in all editions).
    /// </summary>
    HeraldEdition MinimumEdition => HeraldEdition.Community;

    /// <summary>
    /// Creates a concrete sink from the given runtime definition.
    /// </summary>
    ILogger CreateSink(
        LoggingRuntimeSinkDefinition definition,
        ILogLevelRegistry levelRegistry,
        ILogOutputTransformerRegistry transformerRegistry);

    /// <summary>
    /// Return this sink's CAPABILITY.yaml manifest as raw text, or
    /// <c>null</c> when no manifest is embedded. The default implementation
    /// reads the embedded resource named <c>CAPABILITY.yaml</c> from the
    /// concrete provider's assembly — every sink in the Herald.Sinks repo
    /// gets that resource embedded by <c>Directory.Build.props</c>, so 93
    /// sinks inherit a working manifest reader without touching their own
    /// code. A custom provider that lives outside Herald.Sinks can either
    /// embed its own <c>CAPABILITY.yaml</c> alongside its dll or override
    /// this method to return the manifest from another source.
    /// </summary>
    /// <remarks>
    /// The Dashboard renders the <c>dashboard_config</c> block of this
    /// manifest into a sink-configuration form, so a fresh sink dropped
    /// into a plugin directory can describe its own UI without any
    /// per-sink form code in the Dashboard.
    /// </remarks>
    string? GetCapabilityYaml()
    {
        using var stream = GetType().Assembly.GetManifestResourceStream("CAPABILITY.yaml");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Return this sink's form-schema text, or <c>null</c> when no form
    /// file is embedded. Mirrors <see cref="GetCapabilityYaml"/>: same
    /// embedded-resource pattern, same opt-in.
    /// </summary>
    /// <remarks>
    /// Resource lookup is two-step so a single assembly that ships
    /// multiple providers can ship distinct forms for each:
    /// <list type="number">
    ///   <item>
    ///     Try <c>configuration-{SinkKind}.mmpform</c>. Used when the
    ///     package ships per-kind forms — for example
    ///     <c>Herald.Sinks.File</c> ships
    ///     <c>configuration-text_file.mmpform</c> alongside
    ///     <c>configuration-json_file.mmpform</c>.
    ///   </item>
    ///   <item>
    ///     Fall back to a single <c>configuration.mmpform</c>. Used by
    ///     sinks that ship one form per package, which is the simpler
    ///     and most common case.
    ///   </item>
    /// </list>
    /// The Dashboard renders this text via FormBuilderDSL when the
    /// CAPABILITY manifest carries a <c>formSchema:</c> field; sinks
    /// without an embedded form file return <c>null</c> and the dashboard
    /// falls through to the v1 <c>dashboard_config:</c> path.
    /// </remarks>
    string? GetFormSchemaText()
    {
        var asm = GetType().Assembly;
        using var stream = asm.GetManifestResourceStream($"configuration-{SinkKind}.mmpform")
                        ?? asm.GetManifestResourceStream("configuration.mmpform");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
