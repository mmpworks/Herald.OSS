#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Addons.ManagementApi;
using Xunit;

namespace MMP.Herald.OSS.Tests.Addons.ManagementApi;

/// <summary>
/// Tests for <see cref="HttpHeaderSanitizer"/> (principal-review queue
/// #4). Closes the CRLF / header-injection vector on every network
/// sink that ships in OSS — operator-supplied headers are now
/// validated against the RFC 7230 token grammar and stripped of any
/// CR / LF / NUL in the value.
/// </summary>
public sealed class HttpHeaderSanitizerTests
{
    // ── Name validation ──────────────────────────────────────────────

    [Theory]
    [InlineData("Authorization")]
    [InlineData("X-Api-Key")]
    [InlineData("Content-Type")]
    [InlineData("If-None-Match")]
    [InlineData("X-Trace-Id-123")]
    public void IsValidHeaderName_accepts_RFC_7230_tokens(string name)
    {
        HttpHeaderSanitizer.IsValidHeaderName(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" Authorization")]      // leading whitespace
    [InlineData("Authorization\r\n")]   // embedded CRLF
    [InlineData("Auth\nBearer")]        // injection attempt
    [InlineData("Au:thorization")]      // colon = field separator
    [InlineData("Auth orization")]      // internal whitespace
    [InlineData("Authé")]          // non-ASCII (é)
    public void IsValidHeaderName_rejects_outside_token_set(string name)
    {
        HttpHeaderSanitizer.IsValidHeaderName(name).Should().BeFalse();
    }

    // ── Value safety ─────────────────────────────────────────────────

    [Theory]
    [InlineData("Bearer abc123")]
    [InlineData("application/json")]
    [InlineData("")]                    // empty IS safe, just no payload
    [InlineData("two words ok")]
    public void IsSafeHeaderValue_accepts_plain_values(string value)
    {
        HttpHeaderSanitizer.IsSafeHeaderValue(value).Should().BeTrue();
    }

    [Theory]
    [InlineData("token\r\nX-Injected: yes")]   // classic CRLF injection
    [InlineData("token\nX-Injected: yes")]     // LF-only on some clients
    [InlineData("token\rX-Injected: yes")]     // CR-only on some clients
    [InlineData("token\0null")]                // NUL terminator
    public void IsSafeHeaderValue_rejects_CRLF_or_NUL(string value)
    {
        HttpHeaderSanitizer.IsSafeHeaderValue(value).Should().BeFalse();
    }

    // ── Sanitize dictionary ──────────────────────────────────────────

    [Fact]
    public void Sanitize_returns_null_for_null_input()
    {
        HttpHeaderSanitizer.Sanitize(null).Should().BeNull();
    }

    [Fact]
    public void Sanitize_returns_null_for_empty_input()
    {
        var empty = new Dictionary<string, string>();
        HttpHeaderSanitizer.Sanitize(empty).Should().BeNull();
    }

    [Fact]
    public void Sanitize_preserves_safe_entries_and_drops_injection_attempts()
    {
        // Mix of safe headers and three injection patterns. The
        // sanitizer should retain the safe two and silently drop the
        // three poisoned entries — matching the existing contract
        // that bad headers never crash sink wiring.
        var input = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer abc",
            ["X-Api-Key"]     = "key-123",
            ["Bad Name"]      = "ok-value",                   // invalid name
            ["X-Inject"]      = "ok\r\nX-Evil: yes",          // CRLF in value
            ["Au:th"]         = "value-but-colon-in-name",    // invalid name
        };

        var result = HttpHeaderSanitizer.Sanitize(input);

        result.Should().NotBeNull();
        result!.Should().HaveCount(2);
        result.Should().ContainKey("Authorization").WhoseValue.Should().Be("Bearer abc");
        result.Should().ContainKey("X-Api-Key").WhoseValue.Should().Be("key-123");
        result.Should().NotContainKey("Bad Name");
        result.Should().NotContainKey("X-Inject");
        result.Should().NotContainKey("Au:th");
    }

    [Fact]
    public void Sanitize_returns_null_when_every_entry_is_invalid()
    {
        // All-bad input collapses to null so callers that branch on
        // "any headers?" stay on the no-headers path.
        var input = new Dictionary<string, string>
        {
            ["Bad Name"] = "value",
            ["X-Evil"]   = "value\r\nX-Smuggle: 1",
        };

        HttpHeaderSanitizer.Sanitize(input).Should().BeNull();
    }
}
