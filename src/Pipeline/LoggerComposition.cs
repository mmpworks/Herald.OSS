#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Pipeline.Kernel;

namespace MMP.Herald.Pipeline;
/// <summary>
/// Small composition result that gives callers the logger plus the
/// disposable layers that need release when the pipeline tears down.
///
/// <para>
/// <see cref="AsyncResource"/> carries the (single or grouped)
/// async-disposable wrapper produced by the pipeline assembly —
/// <see cref="AsyncLogger"/>, <see cref="BatchingLogger"/>, etc.
/// <see cref="SyncResources"/> carries any sync <see cref="IDisposable"/>
/// sinks or decorators tracked via
/// <see cref="PipelineAssemblyBuilder.TrackSyncResource"/>; file
/// sinks and other stream-backed handles land here. Disposal walks
/// AsyncResource first (drain semantics), then SyncResources
/// (handle release).
/// </para>
/// </summary>
public sealed record LoggerComposition(
    StructuredLogger Logger,
    IAsyncDisposable? AsyncResource,
    SwappableLogger? SwappableLogger = null,
    SwappableSinkRouter? SinkRouter = null,
    KernelDiagnostic? KernelDiagnostic = null,
    IReadOnlyList<IDisposable>? SyncResources = null);
