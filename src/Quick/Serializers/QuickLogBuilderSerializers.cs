#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace MMP.Herald.Quick.Serializers;

/// <summary>
/// Registry of per-step and per-sink config serializers. Lets each pipeline
/// component own its own serialization shape instead of a central switch
/// statement in <see cref="QuickLogBuilder"/>.
///
/// Defaults are registered in a static constructor. Plugin decorators and
/// custom sinks can extend the registry at runtime via <see cref="RegisterStep"/>
/// and <see cref="RegisterSink"/>.
///
/// Thread-safety: steps use a concurrent dictionary; sink registration is
/// guarded by a lock and reads are served from an immutable snapshot.
/// </summary>
public static class QuickLogBuilderSerializers
{
    private static readonly ConcurrentDictionary<string, IStepConfigSerializer> _steps =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly object _sinksLock = new();
    private static IReadOnlyList<ISinkConfigSerializer> _sinks = Array.Empty<ISinkConfigSerializer>();

    static QuickLogBuilderSerializers()
    {
        RegisterDefaults();
    }

    /// <summary>
    /// Look up a step serializer by name. Returns <c>null</c> if no serializer
    /// is registered — the caller should fall back to the schema-default path.
    /// </summary>
    public static IStepConfigSerializer? GetStep(string stepName) =>
        _steps.TryGetValue(stepName, out var s) ? s : null;

    /// <summary>
    /// Snapshot of all registered sink serializers, in registration order.
    /// Safe to iterate without locking.
    /// </summary>
    public static IReadOnlyList<ISinkConfigSerializer> Sinks => _sinks;

    /// <summary>
    /// Register (or replace) a step serializer. Replacement is by
    /// <see cref="IStepConfigSerializer.StepName"/> (case-insensitive).
    /// </summary>
    public static void RegisterStep(IStepConfigSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        _steps[serializer.StepName] = serializer;
    }

    /// <summary>
    /// Register a sink serializer. Registration is append-only; duplicate
    /// <see cref="ISinkConfigSerializer.SinkKind"/> values throw so mistakes
    /// surface at startup instead of producing duplicate config entries.
    /// </summary>
    public static void RegisterSink(ISinkConfigSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        lock (_sinksLock)
        {
            foreach (var existing in _sinks)
            {
                if (string.Equals(existing.SinkKind, serializer.SinkKind, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Sink serializer for kind '{serializer.SinkKind}' is already registered.");
                }
            }
            var next = new List<ISinkConfigSerializer>(_sinks.Count + 1);
            next.AddRange(_sinks);
            next.Add(serializer);
            _sinks = next;
        }
    }

    // Registered in the order sinks appear in the prior BuildSinksAndRoutes
    // method so JSON output ordering stays identical.
    private static void RegisterDefaults()
    {
        RegisterStep(new Steps.AsyncStepConfigSerializer());
        RegisterStep(new Steps.BatchingStepConfigSerializer());
        RegisterStep(new Steps.FilteringStepConfigSerializer());
        RegisterStep(new Steps.SwappableStepConfigSerializer());
        RegisterStep(new Steps.EventProcessingStepConfigSerializer());
        RegisterStep(new Steps.FanOutStepConfigSerializer());
        RegisterStep(new Steps.FlightRecorderStepConfigSerializer());
        RegisterStep(new Steps.PostFilteringStepConfigSerializer());

        RegisterSink(new Sinks.ConsoleSinkConfigSerializer());
        RegisterSink(new Sinks.NullSinkConfigSerializer());
        RegisterSink(new Sinks.FileSinkConfigSerializer());
        RegisterSink(new Sinks.ChannelSinkConfigSerializer());
        RegisterSink(new Sinks.AuditSinkConfigSerializer());
        RegisterSink(new Sinks.BridgeSinkConfigSerializer());
        RegisterSink(new Sinks.NetworkSinkConfigSerializer());
        RegisterSink(new Sinks.CustomSinkProviderConfigSerializer());
    }
}
