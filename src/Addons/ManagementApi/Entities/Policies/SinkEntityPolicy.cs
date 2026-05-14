#nullable enable

using MMP.Herald.Configuration.Json;
using MMP.Herald.Quick;
using MMP.Herald.Services;

namespace MMP.Herald.Addons.ManagementApi.Entities.Policies;

/// <summary>
/// Sink restore — branchy on the saved <c>kind</c>, dispatch lives in
/// one place. Built-in path covers <c>console</c> + the file family
/// (text / json / protobuf); network sinks route through
/// <see cref="NetworkSinkDispatch"/>'s URI and host-port tables so
/// adding a plugin sink does not modify the policy.
///
/// The file family hands off to
/// <see cref="HeraldManagementApi.ResolveFileSinkFromConfig"/> +
/// <see cref="HeraldManagementApi.RestoreSinkRuntimeOverride"/> so the
/// bag-vs-legacy contract stays in one place. This policy does not
/// duplicate that resolution — it just dispatches.
///
/// Sinks do not clear-then-replay: a fresh builder starts with no
/// sinks, and the restored set is whatever the JSON declares.
/// Re-issuing <c>WithConsoleSink</c> / <c>WithFileSink</c> for the
/// same kind reuses the existing entry per the QuickLogBuilder
/// upsert-on-kind contract.
/// </summary>
internal sealed class SinkEntityPolicy : IEntityKindPolicy
{
    public string Kind => "sink";

    public bool HasSectionInConfig(JsonLoggingConfig config) =>
        config.Sinks is { Count: > 0 };

    public void RestoreFromConfig(QuickLogBuilder builder, JsonLoggingConfig config)
    {
        if (config.Sinks is null) return;
        foreach (var sink in config.Sinks)
            RestoreOne(builder, sink);
    }

    // Cognitive Complexity note: the four-way dispatch reads cleanly
    // as a switch on Kind, then a small set of guards for the network
    // sinks that need URI vs Host+Port disambiguation. Lifting each
    // arm into a helper would just spread the same conditional shape
    // across more files without lowering the branch count.
    private static void RestoreOne(QuickLogBuilder builder, JsonLogSinkConfig sink)
    {
        if (sink.Kind == KnownSinkKinds.Console)
        {
            builder.WithConsoleSink(minLevel: sink.MinLevel);
            return;
        }

        if (IsFileFamily(sink.Kind))
        {
            RestoreFileSink(builder, sink);
            return;
        }

        if (NetworkSinkDispatch.UriSinks.TryGetValue(sink.Kind, out var uriApply)
            && sink.Uri is not null)
        {
            uriApply(builder, sink.Uri, sink.MinLevel);
            return;
        }

        if (NetworkSinkDispatch.HostPortSinks.TryGetValue(sink.Kind, out var hpSpec)
            && sink.Host is not null)
        {
            hpSpec.Apply(builder, sink.Host, sink.Port ?? hpSpec.DefaultPort, sink.MinLevel);
        }
    }

    private static bool IsFileFamily(string kind) =>
        kind == KnownSinkKinds.TextFile
        || kind == KnownSinkKinds.JsonFile
        || kind == KnownSinkKinds.ProtobufFile;

    private static void RestoreFileSink(QuickLogBuilder builder, JsonLogSinkConfig sink)
    {
        var resolved = HeraldManagementApi.ResolveFileSinkFromConfig(sink);
        if (resolved is null) return;

        builder.WithFileSink(resolved.Path, sink.Kind, sink.MinLevel);
        if (resolved.Rolling is not null)
        {
            builder.WithFileSink(
                resolved.Path,
                resolved.Rolling.Interval ?? "daily",
                maxBytes: resolved.Rolling.MaxBytes,
                maxRetainedFiles: resolved.Rolling.MaxRetainedFiles,
                fileNameSuffix: resolved.Rolling.FileNameSuffix,
                minLevel: sink.MinLevel,
                retentionDays: resolved.Rolling.RetentionDays,
                totalSizeCapBytes: resolved.Rolling.TotalSizeCapBytes);
        }
        HeraldManagementApi.RestoreSinkRuntimeOverride(builder, sink);
    }
}
