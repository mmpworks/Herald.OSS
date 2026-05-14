#nullable enable

using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Quick;

/// <summary>
/// Multi-tenancy in Herald.OSS is structural: each tenant builds its own
/// pipeline, and isolation comes from those pipelines not sharing sinks.
/// These tests pin the contract.
/// </summary>
public sealed class MultiTenantIsolationTests
{
    [Fact]
    public void Two_pipelines_produce_two_distinct_StructuredLoggers()
    {
        var tenantA = QuickLogBuilder.Create().WithConsoleSink().BuildAndCommit();
        var tenantB = QuickLogBuilder.Create().WithConsoleSink().BuildAndCommit();

        tenantA.Logger.Should().NotBeSameAs(tenantB.Logger);
    }

    [Fact]
    public void Bridge_capture_per_tenant_keeps_events_isolated()
    {
        var tenantASink = new CapturingLogger();
        var tenantBSink = new CapturingLogger();

        var tenantA = QuickLogBuilder.Create()
            .WithBridge(tenantASink)
            .WithMinimumLevel("trace")
            .BuildAndCommit();

        var tenantB = QuickLogBuilder.Create()
            .WithBridge(tenantBSink)
            .WithMinimumLevel("trace")
            .BuildAndCommit();

        tenantA.Logger.Info(LogCategory.App, "to-tenant-a");
        tenantB.Logger.Info(LogCategory.App, "to-tenant-b");

        tenantASink.Messages.Should().ContainSingle(m => m == "to-tenant-a");
        tenantASink.Messages.Should().NotContain(m => m == "to-tenant-b");

        tenantBSink.Messages.Should().ContainSingle(m => m == "to-tenant-b");
        tenantBSink.Messages.Should().NotContain(m => m == "to-tenant-a");
    }

    private sealed class CapturingLogger : ILogger
    {
        public ConcurrentBag<string> Messages { get; } = new();

        public void Log(LogEvent logEvent) => Messages.Add(logEvent.Message);

        public ValueTask LogAsync(LogEvent logEvent, CancellationToken cancellationToken = default)
        {
            Messages.Add(logEvent.Message);
            return ValueTask.CompletedTask;
        }
    }
}
