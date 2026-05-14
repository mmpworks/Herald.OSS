#nullable enable

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing pipeline step entry for serialization.
/// Contains step identity, audit metadata, and configuration properties.
/// Display names, descriptions, and help text are attached to the PipelineStep
/// class in code and are never persisted.
/// The Config dictionary holds each step's runtime configuration values
/// (e.g. capacity, failureThreshold, bufferSize) keyed by field name.
/// </summary>
public sealed record JsonPipelineStepConfig(
    string StepName,
    string? Alias = null,
    string? Vendor = null,
    string? Version = null,
    System.Collections.Generic.IReadOnlyDictionary<string, object?>? Config = null);
