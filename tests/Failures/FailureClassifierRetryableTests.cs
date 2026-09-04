#nullable enable

using System;
using System.Net;
using System.Net.Http;
using FluentAssertions;
using MMP.Herald.Failures;
using Xunit;

namespace MMP.Herald.OSS.Tests.Failures;

/// <summary>
/// A 408 (Request Timeout) or 429 (Too Many Requests) response is recoverable.
/// Classifying either as permanent makes the circuit breaker drop the write
/// instead of retrying it, so a rate-limited sink loses every event it is
/// asked to throttle.
/// </summary>
public sealed class FailureClassifierRetryableTests
{
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public void Recoverable_4xx_is_transient(HttpStatusCode status) {
        var ex = new HttpRequestException("recoverable", null, status);
        FailureClassifier.Classify(ex).Should().Be(FailureCategory.Transient);
    }

    [Fact]
    public void Null_status_is_transient() {
        FailureClassifier.Classify(new HttpRequestException("no status"))
            .Should().Be(FailureCategory.Transient);
    }

    /// <summary>
    /// Fuzz over the whole HTTP status range. The partition is the rule, not a
    /// list of examples: 5xx plus 408 and 429 are transient, every other 4xx is
    /// permanent, and 1xx-3xx stay unclassified.
    /// </summary>
    [Fact]
    public void Fuzz_status_partition_matches_the_rule() {
        const int seed = 20260904;
        var random = new Random(seed);

        for (var i = 0; i < 20_000; i++)
        {
            var code = random.Next(100, 600);
            var ex = new HttpRequestException("fuzz", null, (HttpStatusCode)code);

            var expected = code switch
            {
                >= 500 => FailureCategory.Transient,
                408 or 429 => FailureCategory.Transient,
                >= 400 => FailureCategory.Permanent,
                _ => FailureCategory.Unknown
            };

            FailureClassifier.Classify(ex).Should().Be(
                expected,
                "status {0} must classify as {1} (seed {2})", code, expected, seed);
        }
    }
}
