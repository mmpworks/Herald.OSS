#nullable enable

using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using MMP.Herald;
using Xunit;

namespace MMP.Herald.OSS.Tests;

/// <summary>
/// Reflection-based presence pin for the Stage 0 Phase 1 friend-grant
/// transplant: ensures the five InternalsVisibleTo entries the umbrella
/// ecosystem depends on stay on the Herald.OSS assembly. A future edit
/// that drops one of these silently breaks Sci, Pro, Enterprise, or the
/// management API surface; this test fires before that ships.
///
/// <para>
/// Phase 1.5 extends this pin to assert the PublicKey qualifier per
/// grant once the R17 key-rotation runbook lands and the qualifiers are
/// added to the csproj.
/// </para>
/// </summary>
public sealed class HeraldOssFriendGrantTests
{
    [Fact]
    public void HeraldOss_FriendGrants_AreAllPresent()
    {
        var attrs = typeof(HeraldVersion).Assembly
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName.Split(',')[0])
            .ToHashSet();

        attrs.Should().Contain("Herald.OSS.Tests");
        attrs.Should().Contain("Herald.Sci");
        attrs.Should().Contain("MMP.Herald.Pro");
        attrs.Should().Contain("MMP.Herald.Enterprise");
        attrs.Should().Contain("Herald.ManagementApi");
    }
}
