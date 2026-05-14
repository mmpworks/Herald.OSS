#nullable enable

using System;
using MMP.Herald.Pipeline;
using MMP.Herald.Quick;

namespace MMP.Herald;

/// <summary>
/// Mints <see cref="ILogger{T}"/> instances from a single shared
/// <see cref="StructuredLogger"/>. One factory per bootstrap is the expected
/// pattern; the factory is cheap (one field) and safe to register as a
/// singleton in a DI container.
///
/// <para>
/// Every typed logger the factory hands out forwards through the same
/// underlying pipeline, so adding more <c>ILogger&lt;T&gt;</c> registrations
/// costs only the per-T allocation in <see cref="Create{T}"/> — no new
/// pipeline, no new sinks, no new state.
/// </para>
/// </summary>
public sealed class TypedLoggerFactory
{
    private readonly StructuredLogger _underlying;

    public TypedLoggerFactory(StructuredLogger underlying)
    {
        ArgumentNullException.ThrowIfNull(underlying);
        _underlying = underlying;
    }

    /// <summary>
    /// Convenience constructor: pull the <see cref="StructuredLogger"/> out of
    /// a <see cref="QuickLogResult"/> so callers do not have to know the
    /// internal wiring.
    /// </summary>
    public TypedLoggerFactory(QuickLogResult result)
        : this(EnsureLogger(result))
    {
    }

    /// <summary>
    /// Build a typed logger for <typeparamref name="T"/>. The underlying
    /// pipeline is shared across every type the factory mints.
    /// </summary>
    public ILogger<T> Create<T>() => new TypedLogger<T>(_underlying);

    private static StructuredLogger EnsureLogger(QuickLogResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Logger;
    }
}

/// <summary>
/// One-line shortcut for callers that already have a <see cref="QuickLogResult"/>
/// and want a typed logger without standing up a factory. Most production code
/// should still register <see cref="TypedLoggerFactory"/> as a singleton in DI
/// and resolve <c>ILogger&lt;T&gt;</c> through it; this extension is for
/// scripts, tests, and one-off entry points.
/// </summary>
public static class QuickLogResultExtensions
{
    public static ILogger<T> CreateTypedLogger<T>(this QuickLogResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new TypedLogger<T>(result.Logger);
    }
}
