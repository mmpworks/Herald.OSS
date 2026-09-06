#nullable enable

using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.OSS.Tests.Helpers;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.Tests.Pipeline.Kernel;

/// <summary>
/// Coverage for <see cref="MaterializingKernelSink"/>: the kernel path
/// materialises a heap <see cref="LogEvent"/> from the buffer before
/// forwarding, the chain path forwards a <see cref="LogEvent"/> directly.
/// </summary>
public sealed class MaterializingKernelSinkTests
{
    [Fact]
    public void Kernel_path_converts_buffer_to_LogEvent_at_boundary()
    {
        var spy = new SpyLogger(); // intentionally not IKernelSink
        var adapter = new MaterializingKernelSink(spy);

        var props = new[] { new LogProperty("N", 42) };
        var buffer = new LogEventBuffer(
            timeUtc: new System.DateTimeOffset(2026, 4, 21, 0, 0, 0, System.TimeSpan.Zero),
            level: KnownLogLevels.Info,
            category: LogCategory.App,
            messageTemplate: "hello {N}",
            message: "hello 42",
            properties: props);

        adapter.Log(in buffer);

        spy.Events.Should().HaveCount(1);
        spy.Events[0].Level.Should().Be(KnownLogLevels.Info);
        spy.Events[0].MessageTemplate.Should().Be("hello {N}");
        spy.Events[0].Properties.Should().HaveCount(1);
        spy.Events[0].Properties[0].Name.Should().Be("N");
    }

    [Fact]
    public void Chain_path_passes_a_LogEvent_straight_through()
    {
        var spy = new SpyLogger();
        var adapter = new MaterializingKernelSink(spy);

        var evt = new LogEvent(
            TimeUtc: System.DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Info,
            Category: LogCategory.App,
            MessageTemplate: "hello",
            Message: "hello",
            Properties: System.Array.Empty<LogProperty>(),
            Context: LogEvent.EmptyContext);

        adapter.Log(evt);

        spy.Events.Should().HaveCount(1);
    }
}
