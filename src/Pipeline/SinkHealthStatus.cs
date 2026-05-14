#nullable enable

using System;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Health status snapshot for a sink or sink wrapper.
/// </summary>
public sealed record SinkHealthStatus(
    string SinkName,
    SinkState State,
    int ConsecutiveFailures,
    DateTimeOffset? LastFailure,
    DateTimeOffset? LastSuccess);
