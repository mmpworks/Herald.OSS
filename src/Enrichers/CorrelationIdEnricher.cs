#nullable enable

using System;
using System.Threading;
using MMP.Herald.Events;

namespace MMP.Herald.Enrichers;

/// <summary>
/// Enriches every log event with a correlation ID from AsyncLocal storage.
/// The correlation ID flows automatically through async call chains and
/// is inherited by child scopes.
///
/// Use <see cref="CorrelationScope"/> to set the ID at request/operation boundaries:
///
///   using var scope = CorrelationScope.Begin("req-abc-123");
///   // all log events in this scope carry correlationId = "req-abc-123"
///
/// For game use cases: set the correlation ID at the start of a quest,
/// multiplayer match, or network request to correlate all related events.
/// </summary>
public sealed class CorrelationIdEnricher : ILogEnricher
{
    public void Enrich(LogEventEnrichmentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = CorrelationScope.Current;
        if (correlationId is not null)
        {
            context.SetContextValue(Services.LogContextKeys.CorrelationId, correlationId);
        }
    }
}

/// <summary>
/// Sets a correlation ID that flows through async call chains via AsyncLocal.
/// Disposing the scope restores the previous correlation ID.
///
/// Scopes nest: inner scopes override the ID, disposal restores the outer scope's ID.
///
///   using var outer = CorrelationScope.Begin("match-42");
///   // correlationId = "match-42"
///   using var inner = CorrelationScope.Begin("round-7");
///   // correlationId = "round-7"
///   // after inner disposes: correlationId = "match-42"
/// </summary>
public sealed class CorrelationScope : IDisposable
{
    private static readonly AsyncLocal<string?> _current = new();
    private readonly string? _previous;

    private CorrelationScope(string correlationId)
    {
        _previous = _current.Value;
        _current.Value = correlationId;
    }

    /// <summary>The current correlation ID, or null if none is active.</summary>
    public static string? Current => _current.Value;

    /// <summary>Begin a new correlation scope with the given ID.</summary>
    public static CorrelationScope Begin(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return new CorrelationScope(correlationId);
    }

    /// <summary>Begin a new correlation scope with an auto-generated GUID-based ID.</summary>
    public static CorrelationScope Begin() =>
        new(Guid.NewGuid().ToString("N"));

    public void Dispose() => _current.Value = _previous;
}
