#nullable enable

using FluentAssertions;
using MMP.Herald.Diagnostics;
using Xunit;

namespace MMP.Herald.OSS.Tests.Diagnostics;

/// <summary>
/// Pins the reserved <see cref="HeraldGenSource"/> token values so a
/// future contributor doesn't silently rename the constant and break
/// downstream bridges that route or filter on the token. Per
/// Richard's note during 0.2.2 review: the constant becomes part of
/// the API surface the moment it ships; a one-line pin keeps the
/// shape stable.
/// </summary>
public sealed class HeraldGenSourceTests
{
    [Fact]
    public void RuntimeNotice_token_value_is_stable()
    {
        HeraldGenSource.RuntimeNotice.Should().Be("@herald.runtime.notice",
            "downstream gates and bridges may compare against this exact string");
    }
}
