#nullable enable

namespace MMP.Herald.Configuration.Json;
/// <summary>
/// JSON-facing async policy.
/// </summary>
public sealed record JsonAsyncLogPolicyConfig(
    bool Enabled,
    int Capacity = Services.PipelineDefaults.AsyncCapacity,
    string DropStrategy = Services.KnownDropStrategies.DropWrite,
    bool DeferRendering = false);