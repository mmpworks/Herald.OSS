#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace MMP.Herald.Routing.Loopback;

/// <summary>
/// Process-wide pub/sub bus for loopback events. The interceptor
/// publishes one entry per pipeline+sink combination; subscribers
/// (typically the management-API SSE relay) receive entries for the
/// pipeline+sink they registered for.
///
/// <para>Topology is intentionally simple: a topic is the
/// (pipelineName, sinkName) pair. Subscribers receive every event
/// published to that topic. Cross-topic subscriptions ("watch every
/// sink in pipeline X") are not supported here — a caller that needs
/// that fans out subscriptions on its own.</para>
///
/// <para>Reads enumerate a snapshot of the subscriber list, so a
/// publish call does not block on subscriber registrations and a
/// subscriber that disposes mid-publish does not invalidate the
/// snapshot. The list of subscribers per topic is small (one per
/// SSE listener for that sink); the lock-on-mutation cost is
/// negligible compared to the per-event publish work.</para>
/// </summary>
public static class LoopbackEventBus
{
    private static readonly ConcurrentDictionary<string, TopicSubscribers> _topics =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Subscribe to events on (<paramref name="pipelineName"/>,
    /// <paramref name="sinkName"/>). Dispose the returned token to
    /// stop receiving events.
    /// </summary>
    public static IDisposable Subscribe(
        string pipelineName,
        string sinkName,
        Action<LoopbackLogEntry> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipelineName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sinkName);
        ArgumentNullException.ThrowIfNull(handler);

        var topic = _topics.GetOrAdd(Key(pipelineName, sinkName), _ => new TopicSubscribers());
        topic.Add(handler);
        return new Subscription(topic, handler);
    }

    /// <summary>
    /// Publish an entry to every subscriber of (<paramref name="pipelineName"/>,
    /// <paramref name="sinkName"/>). No-op when nobody is listening,
    /// which is the expected hot path on a server with no Dashboard
    /// open — the cost is one dictionary lookup + null check.
    /// </summary>
    public static void Publish(string pipelineName, string sinkName, LoopbackLogEntry entry)
    {
        if (!_topics.TryGetValue(Key(pipelineName, sinkName), out var topic)) return;
        topic.Dispatch(entry);
    }

    private static string Key(string pipelineName, string sinkName) =>
        pipelineName + "/" + sinkName;

    /// <summary>
    /// Per-topic subscriber list. Internal mutation is guarded so a
    /// concurrent dispatch + add cannot tear the iteration. The
    /// snapshot pattern keeps the dispatch loop lock-free.
    /// </summary>
    private sealed class TopicSubscribers
    {
        private readonly object _lock = new();
        private List<Action<LoopbackLogEntry>> _handlers = new();

        public void Add(Action<LoopbackLogEntry> handler)
        {
            lock (_lock)
            {
                var copy = new List<Action<LoopbackLogEntry>>(_handlers.Count + 1);
                copy.AddRange(_handlers);
                copy.Add(handler);
                _handlers = copy;
            }
        }

        public void Remove(Action<LoopbackLogEntry> handler)
        {
            lock (_lock)
            {
                var copy = new List<Action<LoopbackLogEntry>>(_handlers.Count);
                foreach (var existing in _handlers)
                {
                    if (!ReferenceEquals(existing, handler)) copy.Add(existing);
                }
                _handlers = copy;
            }
        }

        public void Dispatch(LoopbackLogEntry entry)
        {
            // Snapshot read: the field is replaced wholesale by Add /
            // Remove so iterating the captured reference is safe even
            // if a writer publishes a new list mid-loop.
            var snapshot = _handlers;
            for (var i = 0; i < snapshot.Count; i++)
            {
                try { snapshot[i](entry); }
                catch { /* a misbehaving subscriber must not break the bus */ }
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly TopicSubscribers _topic;
        private readonly Action<LoopbackLogEntry> _handler;
        private int _disposed;

        public Subscription(TopicSubscribers topic, Action<LoopbackLogEntry> handler)
        {
            _topic = topic;
            _handler = handler;
        }

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _topic.Remove(_handler);
        }
    }
}
