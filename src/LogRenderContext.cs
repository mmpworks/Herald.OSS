#nullable enable

using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Output.Aliases;

namespace MMP.Herald;

/// <summary>
/// Input passed to output transformers and level expansions.
/// </summary>
public sealed record LogRenderContext(
    LogEvent Event,
    LogOutputAlias Alias,
    ILogLevelRegistry LevelRegistry);