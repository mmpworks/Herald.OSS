#nullable enable

using System;
using BenchmarkDotNet.Attributes;
using MMP.Herald.Events;
using MMP.Herald.Quick;
using global::Serilog;
using HeraldLogCategory = MMP.Herald.Events.LogCategory;

namespace MMP.Herald.OSS.Benchmarks.Comparisons.HeraldRow;

/// <summary>
/// Destructure-policy shootout vs Serilog. Both libraries support a
/// "transform this type when it appears under capture mode
/// <c>{@Name}</c>" projection. This bench compares the per-call cost
/// of an emit that triggers the projection.
///
/// <para>
/// Workload: one <see cref="Order"/> instance with a nested
/// <see cref="OrderAddress"/>. Both libraries register a projection
/// that flattens the order into a small anonymous shape (id,
/// customer, total, city) and discard through their null sinks.
/// </para>
/// </summary>
[MemoryDiagnoser]
public class DestructureBenchmarks
{
    private QuickLogResult _herald = null!;
    private global::Serilog.Core.Logger _serilog = null!;
    private static readonly Order _order = new(
        Id: 42,
        Customer: "alice@example.com",
        Total: 99.99,
        PlacedUtc: new DateTime(2026, 5, 14, 19, 0, 0, DateTimeKind.Utc),
        ShipTo: new OrderAddress("123 Pine St", "Brooklyn"));

    [GlobalSetup]
    public void Setup()
    {
        _herald = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("trace")
            .Destructure<Order>(o => new
            {
                o.Id,
                o.Customer,
                o.Total,
                City = o.ShipTo.City
            })
            .BuildAndCommit();

        _serilog = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Destructure.ByTransforming<Order>(o => new
            {
                o.Id,
                o.Customer,
                o.Total,
                City = o.ShipTo.City
            })
            .WriteTo.Sink(new SerilogNullSink())
            .CreateLogger();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _serilog.Dispose();
        if (_herald.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Benchmark(Baseline = true)]
    public void Herald_DestructureOrder()
    {
        _herald.Logger.Information(HeraldLogCategory.App, "Order placed: {@Order}", _order);
    }

    [Benchmark]
    public void Serilog_DestructureOrder()
    {
        _serilog.Information("Order placed: {@Order}", _order);
    }

    /// <summary>POCO under destructuring. Five fields including one nested object.</summary>
    public sealed record Order(
        int Id,
        string Customer,
        double Total,
        DateTime PlacedUtc,
        OrderAddress ShipTo);

    /// <summary>Nested member of <see cref="Order"/>.</summary>
    public sealed record OrderAddress(string Street, string City);

    /// <summary>Serilog null sink — drops events without rendering.</summary>
    private sealed class SerilogNullSink : global::Serilog.Core.ILogEventSink
    {
        public void Emit(global::Serilog.Events.LogEvent logEvent) { }
    }
}
