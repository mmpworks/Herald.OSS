#nullable enable

// NO framework gate. This test runs on net8.0 as well as net9.0/net10.0.
// It pins the verdict that the typed Serilog path is true 0 B on net8 —
// byte-identical to net9 — because [InlineArray] stack residency is a net8
// language feature, not a JIT heuristic that could differ across runtimes.
//
// The SerilogLoggerAdapter and its generated typed overloads now live in the
// Herald.OSS assembly itself (src/Serilog merged into Herald.OSS on every TFM),
// so this test reaches them through the unconditional Herald.OSS ProjectReference
// — it does NOT depend on the net9/net10-only MMP.Herald.Serilog ProjectReference.
//
// If this ever reports non-zero on net8, the net8 zero-alloc claim has regressed
// (most likely the src/Serilog net8 compile exclusion was reintroduced, or the
// InlineArray buffer fill silently fell back to params object[]).

using System;
using FluentAssertions;
using MMP.Herald.OSS.Tests.TestSupport;
using MMP.Herald.Quick;
using MMP.Herald.Serilog;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog;

/// <summary>
/// net8 zero-alloc regression pin for the typed Serilog path. Mirrors the arity
/// sweep in <c>SerilogHotPathAllocTests</c> (which is net9+ gated for historical
/// reasons) but is deliberately ungated so the net8 result is asserted on every
/// build. Representative arity 4, all reference-type (string) arguments: a
/// reference-type value never boxes, so the typed slot is a clean 0 B on net8.
/// </summary>
public sealed class SerilogNet8ZeroAllocRegressionTests : IDisposable
{
    private readonly QuickLogResult _result;

    public SerilogNet8ZeroAllocRegressionTests()
    {
        // Same null-sink shape as SerilogHotPathAllocTests: WithNullSink discards
        // events, so no per-event LogEvent allocation enters the measured window.
        // Only the typed call path itself is under test.
        _result = QuickLogBuilder.Create()
            .WithNullSink()
            .WithMinimumLevel("verbose")
            .BuildAndCommit();
    }

    public void Dispose()
    {
        if (_result.AsyncResource is { } resource)
            resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    // ── Arity 4, all reference-type args — 0 B on net8 (Jared's measured pin) ──
    // Typed as SerilogLoggerAdapter (not ILogger) so the compiler resolves the
    // Information<T1,T2,T3,T4> typed overload rather than params object?[]?.
    // Reference-type args (string) never box, so the typed slot is exact 0 B.
    [Fact]
    public void TypedPath_arity4_reference_args_allocates_zero_bytes_on_net8()
    {
        var log = new SerilogLoggerAdapter(_result.Logger);

        // Interned compile-time literal: the hole-index cache is warm after the
        // first (warmup) call, so no re-parse enters the measured window.
        const string template = "user {User} role {Role} tenant {Tenant} action {Action}";
        const string user = "alice";
        const string role = "admin";
        const string tenant = "acme";
        const string action = "login";

        var bytes = AllocationProbe.BytesPerIteration(
            () => log.Information(template, user, role, tenant, action));

        bytes.Should().Be(0,
            "the typed arity-4 Serilog overload rides the InlineArray compact buffer with " +
            "no box for reference-type args — true 0 B on net8, byte-identical to net9. " +
            "A non-zero result means the src/Serilog net8 compile was re-excluded or the call " +
            "fell through to params object[].");
    }
}
