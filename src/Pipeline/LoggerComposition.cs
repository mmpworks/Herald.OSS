#nullable enable

using System;

namespace MMP.Herald.Pipeline;
/// <summary>
/// Small composition result that gives callers the logger and optional async resource to dispose.
/// </summary>
public sealed record LoggerComposition(
    StructuredLogger Logger,
    IAsyncDisposable? AsyncResource,
    SwappableLogger? SwappableLogger = null,
    SwappableSinkRouter? SinkRouter = null);