#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using MMP.Herald.Templating;

namespace MMP.Herald;

/// <summary>
/// Typed logger facade. <typeparamref name="T"/> supplies an implicit
/// <see cref="LogCategory"/> derived from <see cref="Type.Name"/>, so callers
/// stop repeating the category at every call site:
///
/// <code>
/// public sealed class OrderService
/// {
///     private readonly ILogger&lt;OrderService&gt; _log;
///     public OrderService(ILogger&lt;OrderService&gt; log) { _log = log; }
///
///     public void Place(Order o)
///     {
///         _log.Information("Placing order {orderId}", new LogProperty("orderId", o.Id));
///     }
/// }
/// </code>
///
/// <para>
/// The shape lines up with <c>Microsoft.Extensions.Logging.ILogger&lt;T&gt;</c>
/// so teams migrating from MEL feel at home. The non-generic
/// <see cref="MMP.Herald.ILogger"/> remains the low-level pipeline port —
/// the two interfaces are siblings, not replacements for each other.
/// </para>
///
/// <para>
/// Implementations forward through a single <see cref="Pipeline.StructuredLogger"/>;
/// every typed logger for the same bootstrap shares the same pipeline, sinks,
/// scope provider, and level registry. Resolving <c>ILogger&lt;A&gt;</c> and
/// <c>ILogger&lt;B&gt;</c> costs a single object allocation per type.
/// </para>
/// </summary>
public interface ILogger<T>
{
    /// <summary>
    /// The category derived from <typeparamref name="T"/>. Computed once per
    /// closed generic type and reused across every typed logger instance.
    /// </summary>
    LogCategory Category { get; }

    /// <summary>
    /// True when <paramref name="level"/> meets or exceeds the underlying
    /// pipeline's minimum level. Cheap — use to guard expensive property
    /// construction before logging.
    /// </summary>
    bool IsEnabled(LogLevel level);

    void Verbose(string messageTemplate, params LogProperty[] properties);
    void Debug(string messageTemplate, params LogProperty[] properties);
    void Information(string messageTemplate, params LogProperty[] properties);
    void Warning(string messageTemplate, params LogProperty[] properties);
    void Error(string messageTemplate, params LogProperty[] properties);
    void Fatal(string messageTemplate, params LogProperty[] properties);

    void Verbose(Exception exception, string messageTemplate, params LogProperty[] properties);
    void Debug(Exception exception, string messageTemplate, params LogProperty[] properties);
    void Information(Exception exception, string messageTemplate, params LogProperty[] properties);
    void Warning(Exception exception, string messageTemplate, params LogProperty[] properties);
    void Error(Exception exception, string messageTemplate, params LogProperty[] properties);
    void Fatal(Exception exception, string messageTemplate, params LogProperty[] properties);

    /// <summary>
    /// Return a new typed logger that merges <paramref name="defaultContext"/>
    /// into every subsequent event. The category stays the same — the new
    /// logger still reports <typeparamref name="T"/> as its category. Use
    /// this at scope boundaries (per request, per tenant, per transaction)
    /// where the added context applies to a known slice of events.
    ///
    /// <para>
    /// The returned logger shares the underlying pipeline with the current
    /// one; only the default-context snapshot differs. Allocation is one
    /// wrapper + one merged context dictionary per call — cheap to call
    /// on every request.
    /// </para>
    /// </summary>
    ILogger<T> ForContext(IReadOnlyDictionary<string, object?> defaultContext);

    /// <summary>
    /// Push an <see cref="ILogScope"/> onto the ambient async-local scope
    /// stack. The scope's keys land in <see cref="LogEvent.Context"/> of
    /// every event produced inside the scope's <c>using</c> block,
    /// regardless of which logger emits them — the scope provider is
    /// shared across every logger built from the same bootstrap.
    /// </summary>
    ILogScope BeginScope(IReadOnlyDictionary<string, object?> values);
}
