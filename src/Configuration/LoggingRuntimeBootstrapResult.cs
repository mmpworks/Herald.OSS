#nullable enable

using MMP.Herald.Configuration.Runtime;
using MMP.Herald.Levels;

namespace MMP.Herald.Configuration;
/// <summary>
/// Result of converting JSON-backed config into runtime structures.
/// </summary>
public sealed record LoggingRuntimeBootstrapResult(
    LoggingRuntimeConfiguration RuntimeConfiguration,
    ILogLevelRegistry LevelRegistry);