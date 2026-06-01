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

    // G2.2: LogPropertyCaptureMode is an open record keyed on Value, so a caller
    // can construct a mode outside the three canonical singletons. An out-of-domain
    // mode must pack as Default (graceful degradation), not throw or mis-pack.
    [Fact]
    public void Out_of_domain_capture_mode_degrades_to_Default()
    {
        var bogus = new LogPropertyCaptureMode("Bogus");

        var compact = LogPropertyCompact.From("v", 7, bogus);

        // Packed axis degraded to Default.
        compact.CaptureMode.Should().Be(CaptureMode.Default,
            "an unrecognized capture mode must pack as Default, not a corrupt byte");

        // And round-trips back to the Default singleton, not the bogus instance.
        compact.CaptureModeOrDefault.Should().Be(LogPropertyCaptureMode.Default,
            "an out-of-domain mode resolves to the Default singleton on read-back");

        // Inflating the compact slot takes the Default (no-mode) construction path.
        LogProperty prop = compact;
        prop.CaptureModeOrDefault.Should().Be(LogPropertyCaptureMode.Default,
            "the inflated record carries Default for an out-of-domain mode");
    }

    // G2.2: a null capture mode is also Default — same degradation path.
    [Fact]
    public void Null_capture_mode_packs_as_Default()
    {
        var compact = LogPropertyCompact.From("v", 7, (LogPropertyCaptureMode?)null);

        compact.CaptureMode.Should().Be(CaptureMode.Default,
            "a null capture mode packs as Default");
    }
}
