#nullable enable

using FluentAssertions;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline.Kernel;

/// <summary>
/// Pins the implicit <see cref="LogPropertyCompact"/> → <see cref="LogProperty"/>
/// conversion to preserve the packed CaptureMode axis. The operator forwards to
/// <see cref="LogPropertyCompact.ToLogProperty"/>; raw construction
/// (<c>new LogProperty(Name, Value)</c>) dropped CaptureMode for Destructure /
/// Stringify holes riding the compact slot.
/// </summary>
public sealed class LogPropertyCompactCaptureModeTests
{
    [Fact]
    public void Implicit_operator_preserves_CaptureMode()
    {
        var compact = LogPropertyCompact.From("obj", new object(), LogPropertyCaptureMode.Destructure);
        LogProperty prop = compact; // implicit operator
        prop.CaptureModeOrDefault.Should().Be(LogPropertyCaptureMode.Destructure,
            "implicit operator must preserve CaptureMode — use ToLogProperty() not raw construction");
    }
}
