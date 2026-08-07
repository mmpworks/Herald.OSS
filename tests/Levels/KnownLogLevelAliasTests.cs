#nullable enable

// 0.13.0 alias contract — the short-vocabulary names (Trace/Info/Warn/Critical)
// are ALIASES onto the Serilog-vocabulary canonical instances, not new levels.
// Reference identity is the whole contract: same instance, same key, same
// registry behavior. A failing identity test means someone turned an alias
// into a real level — that is a wire-visible change and must not ship silently.

using FluentAssertions;
using MMP.Herald.Levels;
using Xunit;

namespace MMP.Herald.OSS.Tests.Levels;

public sealed class KnownLogLevelAliasTests
{
    public static readonly TheoryData<string, LogLevel, LogLevel> AliasPairs = new()
    {
        { "Trace->Verbose", KnownLogLevels.Trace, KnownLogLevels.Verbose },
        { "Info->Information", KnownLogLevels.Info, KnownLogLevels.Information },
        { "Warn->Warning", KnownLogLevels.Warn, KnownLogLevels.Warning },
        { "Critical->Fatal", KnownLogLevels.Critical, KnownLogLevels.Fatal },
    };

    [Theory]
    [MemberData(nameof(AliasPairs))]
    public void Alias_is_reference_identical_to_canonical(string name, LogLevel alias, LogLevel canonical)
    {
        ReferenceEquals(alias, canonical).Should().BeTrue(
            $"{name}: an alias must be the same instance, not a lookalike level");
    }

    [Theory]
    [MemberData(nameof(AliasPairs))]
    public void Alias_carries_canonical_wire_key(string name, LogLevel alias, LogLevel canonical)
    {
        alias.Key.Should().Be(canonical.Key, $"{name}: aliases never introduce a new wire key");
    }

    [Fact]
    public void Alias_count_is_exactly_four()
    {
        // Exhaustive guard: the alias surface is Trace/Info/Warn/Critical and
        // nothing else. New aliases require a deliberate edit here.
        AliasPairs.Should().HaveCount(4);
    }
}
