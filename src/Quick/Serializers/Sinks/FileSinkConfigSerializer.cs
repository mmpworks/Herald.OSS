#nullable enable

using System.Collections.Generic;
using MMP.Herald.Configuration.Json;
using MMP.Herald.Configuration.Sinks;

namespace MMP.Herald.Quick.Serializers.Sinks;

/// <summary>
/// Config serializer for the built-in file sink. Emits a single entry
/// when a file sink is configured on the builder; the concrete JSON
/// kind (text_file / json_file / protobuf_file) is taken from the
/// builder's recorded file kind.
///
/// <para>v2 contract: when the registered file-sink provider exposes a
/// <c>configuration-{kind}.mmpform</c> with a <c>__properties</c>
/// block, this serializer fills the JSON's <c>properties</c> bag with
/// every contract key. The fluent-API state (path, rolling, etc.)
/// supplies user-set values; everything else falls back to the
/// contract default. The resulting JSON satisfies the "every
/// __properties key must appear" invariant without the legacy
/// flat-key emit being involved.</para>
/// </summary>
internal sealed class FileSinkConfigSerializer : ISinkConfigSerializer
{
    // Registry discriminator — the emitted sink kind is dynamic, so this
    // is just a stable identifier for registration.
    public string SinkKind => "file";

    public IEnumerable<(JsonLogSinkConfig Sink, JsonLogRouteConfig Route)> BuildSinkRoutes(
        QuickLogBuilder builder, SinkSerializerContext context)
    {
        if (builder.LogFilePath is null)
            yield break;

        var properties = BuildPropertyBag(builder);
        var runtime = builder.SinkRuntimeOverrides.Get(builder.LogFileKind);

        // The override snapshot stores "none" as the explicit-clear
        // sentinel so MergeWith can tell "clear me" apart from "don't
        // touch me". On the JSON side, null is the cleared shape, so
        // collapse the sentinel back to null here. Any other value
        // wins over the builder's seed minLevel.
        var resolvedMinLevel = runtime?.MinLevel switch
        {
            null  => builder.LogFileMinLevel,
            "none" => null,
            var x => x,
        };

        yield return (
            new JsonLogSinkConfig(
                builder.LogFileKind,
                builder.LogFileKind,
                Path: builder.LogFilePath,
                Alias: "default",
                Vendor: "MMP",
                Version: HeraldVersion.Version,
                MinLevel: resolvedMinLevel,
                Rolling: builder.LogFileRolling,
                RunState: runtime?.RunState,
                TeeLiveToFile: runtime?.TeeLiveToFile ?? false,
                TeeLiveToUrl: runtime?.TeeLiveToUrl ?? false,
                Properties: properties),
            new JsonLogRouteConfig(builder.LogFileKind, context.DefaultRoutePredicate));
    }

    public IEnumerable<Dictionary<string, object?>> BuildFanOutEntries(QuickLogBuilder builder)
    {
        if (builder.LogFilePath is null)
            yield break;

        var entry = new Dictionary<string, object?>
        {
            ["kind"] = builder.LogFileKind,
            ["minLevel"] = builder.LogFileMinLevel,
        };

        // v2: the property bag is the canonical sink-config carrier in
        // the fanOut display payload too. Listing the bag under
        // "properties" keeps Core-managed metadata (kind, minLevel)
        // visually distinct from sink-owned config the way the dashboard
        // expects.
        var properties = BuildPropertyBag(builder);
        if (properties.Count > 0)
            entry["properties"] = properties;

        yield return entry;
    }

    // Produces the full v2 property bag for the file sink: every
    // __properties key from the registered provider's mmpform, with
    // user-set values overlaid on the contract defaults. Returns an
    // empty dictionary when no provider is registered (legacy boots
    // that haven't called WithFileSinkProviders yet) or when the
    // provider ships no mmpform — the legacy Path / Rolling fields on
    // JsonLogSinkConfig still reach the runtime in that case.
    private static IReadOnlyDictionary<string, object?> BuildPropertyBag(QuickLogBuilder builder)
    {
        var contract = LoadContract(builder, builder.LogFileKind);
        if (contract.Count == 0)
            return new Dictionary<string, object?>(System.StringComparer.Ordinal);

        var userValues = FileSinkUserValuesBuilder.From(builder.LogFilePath, builder.LogFileRolling);
        return SinkPropertyBagBuilder.Build(contract, userValues);
    }

    // Pull __properties out of the provider's embedded mmpform when the
    // provider is registered with the builder. The provider lives in
    // the consumer's sink package (e.g. Herald.Sinks.File), so Core
    // does not import it directly — only ILogSinkProvider's contract.
    private static IReadOnlyList<MmpformPropertyDefinition> LoadContract(QuickLogBuilder builder, string sinkKind)
    {
        var provider = builder.SinkProviders.Get(sinkKind);
        var formText = provider?.GetFormSchemaText();
        return MmpformPropertiesParser.Parse(formText);
    }
}
