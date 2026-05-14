#nullable enable

using MMP.Herald.Configuration.Json;
using MMP.Herald.Quick;

namespace MMP.Herald.Addons.ManagementApi.Entities.Policies;

/// <summary>
/// Per-step display aliases. The source of truth in JSON is
/// <c>PipelineSteps[].Alias</c> — aliases are not their own list
/// because every alias belongs to a specific pipeline step. The
/// policy walks the step list, skips entries with no alias, and
/// calls <see cref="QuickLogBuilder.WithAlias"/> per non-empty one.
///
/// No clear-then-replay: <c>WithAlias</c> is upsert-by-step-name, so
/// the destination's alias set converges on the source's set
/// step-by-step. A step that lost its alias between save and restore
/// keeps its old alias on the destination because no payload tells
/// the restore path "this step now has no alias." That asymmetry
/// matches the broader pipeline-step contract — steps are removed
/// when omitted from <c>PipelineSteps</c>, but aliases ride along
/// with their step.
/// </summary>
internal sealed class AliasEntityPolicy : IEntityKindPolicy
{
    public string Kind => "alias";

    public bool HasSectionInConfig(JsonLoggingConfig config)
    {
        if (config.PipelineSteps is null) return false;
        foreach (var step in config.PipelineSteps)
        {
            if (!string.IsNullOrEmpty(step.Alias)) return true;
        }
        return false;
    }

    public void RestoreFromConfig(QuickLogBuilder builder, JsonLoggingConfig config)
    {
        if (config.PipelineSteps is null) return;
        foreach (var step in config.PipelineSteps)
        {
            if (!string.IsNullOrEmpty(step.Alias))
                builder.WithAlias(step.StepName, step.Alias);
        }
    }
}
