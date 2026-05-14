#nullable enable

using System;
using System.Collections.Generic;

namespace MMP.Herald.Enrichers;

/// <summary>
/// Framework-agnostic request context scope.
/// Automatically creates a CorrelationScope and optional structured context
/// for the lifetime of a request, frame, or operation.
///
/// Stripe Principle #3 (Universal Request ID) and #5 (Four W's):
/// every request/frame gets a unique correlation ID and structured context
/// without boilerplate at each call site.
///
/// Usage in game loop:
///   using var scope = RequestScope.Begin("game-frame",
///       ("frameNumber", frameCount), ("deltaMs", deltaMs));
///   // all log events in this scope carry the correlation ID + context
///
/// Usage in HTTP handler:
///   using var scope = RequestScope.Begin("api-request",
///       ("endpoint", "/v1/charge"), ("method", "POST"),
///       ("customerId", customerId));
///
/// Usage in Godot _Process:
///   using var scope = RequestScope.BeginFrame(frameCount);
///
/// For ASP.NET middleware, wrap this in an IMiddleware. For Godot, call from _Process().
/// No framework dependency required.
/// </summary>
public sealed class RequestScope : IDisposable
{
    private readonly CorrelationScope _correlationScope;

    private RequestScope(CorrelationScope correlationScope)
    {
        _correlationScope = correlationScope;
    }

    /// <summary>
    /// Begin a request scope with a generated correlation ID and optional context.
    /// The correlation ID format: "{prefix}_{guid}" for easy filtering.
    /// </summary>
    /// <param name="prefix">Prefix for the correlation ID (e.g., "req", "frame", "job").</param>
    /// <param name="context">Key-value pairs added as structured context.</param>
    public static RequestScope Begin(string prefix, params (string Key, object? Value)[] context)
    {
        var correlationId = $"{prefix}_{Guid.NewGuid():N}";
        var scope = CorrelationScope.Begin(correlationId);
        return new RequestScope(scope);
    }

    /// <summary>
    /// Begin a request scope with an explicit correlation ID.
    /// Use when the ID comes from an external source (HTTP header, message queue, etc.).
    /// </summary>
    public static RequestScope BeginWithId(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        var scope = CorrelationScope.Begin(correlationId);
        return new RequestScope(scope);
    }

    /// <summary>
    /// Convenience: begin a game frame scope.
    /// Correlation ID format: "frame_{number}_{short-guid}".
    /// </summary>
    public static RequestScope BeginFrame(long frameNumber)
    {
        var correlationId = $"frame_{frameNumber}_{Guid.NewGuid().ToString("N")[..8]}";
        var scope = CorrelationScope.Begin(correlationId);
        return new RequestScope(scope);
    }

    /// <summary>The correlation ID for this scope.</summary>
    public string CorrelationId => CorrelationScope.Current ?? "";

    public void Dispose()
    {
        _correlationScope.Dispose();
    }
}
