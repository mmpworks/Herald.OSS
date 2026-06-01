#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using MMP.Herald.Enrichers;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Services;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Enrichers;

/// <summary>
/// W3b — exception custom-payload flattening. The native
/// <see cref="ExceptionDetailEnricher"/> already emits the flat
/// <c>exception.type/message/depth/chain</c> properties; the data-loss gap was
/// that a custom field (e.g. <c>OrderException.OrderId = 4071</c>) was dropped.
///
/// <para>
/// W3b flattens the custom payload into FLAT dotted-key SCALAR properties:
///   <c>exception.data.OrderId</c>, <c>exception.inner.data.RetryCount</c>,
///   <c>exception.data[0].X</c> (AggregateException children).
/// </para>
///
/// <para>
/// <b>Assertion surface (deviation from the spec's literal JSON-sink walk).</b>
/// Echo's corrected assertion targets <c>properties.GetProperty("exception.data.OrderId")
/// .GetProperty("value")</c> off a JSON sink. That <c>{value, capture_mode}</c> event
/// envelope is produced by the JSON-sink formatters (JsonFormatter / Utf8JsonFormatter),
/// which live in the MMP.Herald.Sinks.* repo — they are not present in Herald.OSS, so an
/// end-to-end JSON parse cannot run here. We assert on the enricher's actual output
/// contract instead: the emitted <see cref="LogProperty"/> list keyed by name. This is
/// the same data the formatter renders, asserted one layer earlier and without an
/// out-of-repo dependency. The intent is honoured: the dotted key is a TOP-LEVEL flat
/// property, NOT a nested object walk.
/// </para>
/// </summary>
public sealed class ExceptionDetailEnricherCustomDataTests
{
    // ── Plain custom-prop exception (the canonical data-loss case) ────────────

    [Fact]
    public void Custom_public_property_survives_as_flat_dotted_key()
    {
        var props = EnrichWith(MakeThrown(new OrderException("checkout failed", orderId: 4071)));

        // Top-level flat key — not a nested .data.OrderId walk.
        props.Should().ContainKey("exception.data.OrderId");
        props["exception.data.OrderId"].ResolvedValue.Should().Be(4071);
    }

    [Fact]
    public void Flat_exception_properties_are_untouched_by_w3b()
    {
        var props = EnrichWith(MakeThrown(new OrderException("checkout failed", orderId: 4071)));

        // Regression guard: W3b extends, it does not disturb the load-bearing flat set.
        props.Should().ContainKey("exception.type");
        props.Should().ContainKey("exception.message");
        props["exception.message"].ResolvedValue.Should().Be("checkout failed");
        props.Should().ContainKey("exception.chain");
    }

    // ── Wrapped (inner-chain) custom property survives under inner. prefix ─────

    [Fact]
    public void Inner_chain_custom_property_survives_under_inner_prefix()
    {
        var inner = MakeThrown(new RetryException("transient", retryCount: 3));
        var outer = MakeThrown(new InvalidOperationException("wrapped failure", inner));

        var props = EnrichWith(outer);

        props.Should().ContainKey("exception.inner.data.RetryCount");
        props["exception.inner.data.RetryCount"].ResolvedValue.Should().Be(3);
    }

    [Fact]
    public void Two_levels_of_inner_stack_inner_segments()
    {
        var deepest = MakeThrown(new RetryException("deepest", retryCount: 9));
        var middle = MakeThrown(new OrderException("middle", orderId: 7, inner: deepest));
        var outer = MakeThrown(new InvalidOperationException("outer", middle));

        var props = EnrichWith(outer);

        props.Should().ContainKey("exception.inner.data.OrderId");
        props["exception.inner.data.OrderId"].ResolvedValue.Should().Be(7);
        props.Should().ContainKey("exception.inner.inner.data.RetryCount");
        props["exception.inner.inner.data.RetryCount"].ResolvedValue.Should().Be(9);
    }

    // ── AggregateException children → indexed segment data[i] ─────────────────

    [Fact]
    public void Aggregate_children_use_indexed_data_segment()
    {
        var first = MakeThrown(new TaggedException(tag: "foo"));
        var second = MakeThrown(new RetryException("second", retryCount: 5));
        var aggregate = MakeThrown(new AggregateException(first, second));

        var props = EnrichWith(aggregate);

        props.Should().ContainKey("exception.data[0].X");
        props["exception.data[0].X"].ResolvedValue.Should().Be("foo");
        props.Should().ContainKey("exception.data[1].RetryCount");
        props["exception.data[1].RetryCount"].ResolvedValue.Should().Be(5);
    }

    // ── Throwing getter → sentinel, never a crash ─────────────────────────────

    [Fact]
    public void Throwing_getter_yields_sentinel_and_does_not_crash()
    {
        var context = MakeContext(MakeThrown(new HostileException()));
        var enricher = new ExceptionDetailEnricher();

        var act = () => enricher.Enrich(context);

        act.Should().NotThrow("a throwing getter must never crash the log call");

        var props = ByName(context);
        props.Should().ContainKey("exception.data.Detonator");
        props["exception.data.Detonator"].ResolvedValue.Should().Be("<threw: InvalidOperationException>");
    }

    // ── Exception.Data dictionary entries also flatten ────────────────────────

    [Fact]
    public void Exception_data_dictionary_entries_flatten_as_scalar_leaves()
    {
        var exception = MakeThrown(new InvalidOperationException("with data"));
        exception.Data["CorrelationId"] = "abc-123";

        var props = EnrichWith(exception);

        props.Should().ContainKey("exception.data.CorrelationId");
        props["exception.data.CorrelationId"].ResolvedValue.Should().Be("abc-123");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Force a real stack trace so the exception behaves like a thrown one.</summary>
    private static T MakeThrown<T>(T exception) where T : Exception
    {
        try { throw exception; }
        catch (T caught) { return caught; }
    }

    private static LogEventEnrichmentContext MakeContext(Exception exception)
    {
        var context = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [LogContextKeys.Exception] = exception,
        };
        return new LogEventEnrichmentContext(
            KnownLogLevels.Error,
            LogCategory.App,
            "operation failed",
            properties: null,
            context: context);
    }

    private static IReadOnlyDictionary<string, LogProperty> EnrichWith(Exception exception)
    {
        var context = MakeContext(exception);
        new ExceptionDetailEnricher().Enrich(context);
        return ByName(context);
    }

    private static IReadOnlyDictionary<string, LogProperty> ByName(LogEventEnrichmentContext context) =>
        context.Properties
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

    // ── Test exception types ──────────────────────────────────────────────────

    private sealed class OrderException : Exception
    {
        public OrderException(string message, int orderId, Exception? inner = null)
            : base(message, inner) => OrderId = orderId;

        public int OrderId { get; }
    }

    private sealed class RetryException : Exception
    {
        public RetryException(string message, int retryCount)
            : base(message) => RetryCount = retryCount;

        public int RetryCount { get; }
    }

    private sealed class TaggedException : Exception
    {
        public TaggedException(string tag) => X = tag;

        public string X { get; }
    }

    private sealed class HostileException : Exception
    {
        // A property whose getter detonates — the defensive backstop must hold.
        public string Detonator =>
            throw new InvalidOperationException("getter boom");
    }
}
