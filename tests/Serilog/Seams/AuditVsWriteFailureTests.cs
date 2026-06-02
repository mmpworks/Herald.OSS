#nullable enable

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using MMP.Herald.Failures;
using MMP.Herald.OSS.Tests.Infrastructure;
using MMP.Herald.Serilog;
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Debugging;
using MMP.Herald.Serilog.Events;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Seams;

/// <summary>
/// G-SEC.2 / G-SEC.3 — AuditTo throw-vs-WriteTo swallow oppositional pair.
///
/// <para>
/// G-SEC.2: <c>WriteTo.Sink</c> must swallow sink failures silently; <c>AuditTo.Sink</c>
/// must propagate them so compliance-critical paths can detect delivery failure.
/// These two facts are tested together as a single oppositional pair so the contract
/// cannot be satisfied by accident (e.g. both throwing or both swallowing).
/// </para>
///
/// <para>
/// G-SEC.3: redaction (via <c>Destructure.ByTransforming</c>) must fire <em>before</em>
/// the event is captured by any sink — including audit sinks.  An audit log that
/// contains unredacted PII is a compliance failure regardless of delivery guarantees.
/// </para>
/// </summary>
// SelfLog_Enable_then_Disable toggles the process-global SelfLog. Other collections
// running in parallel can enable/log through SelfLog mid-test and break the assertion.
// Pin to a non-parallel collection so the global-SelfLog tests never race.
[Collection(nameof(AuditVsWriteFailureTests))]
[CollectionDefinition(nameof(AuditVsWriteFailureTests), DisableParallelization = true)]
public sealed class AuditVsWriteFailureTests
{
    // ── G-SEC.2: oppositional pair ────────────────────────────────────────────

    /// <summary>
    /// The primary contract:
    /// <list type="bullet">
    ///   <item>WriteTo.Sink swallows exceptions — one broken sink must not crash the app.</item>
    ///   <item>AuditTo.Sink propagates exceptions as <see cref="AuditLogFailureException"/>
    ///         — delivery failure on a compliance path must surface to the caller.</item>
    /// </list>
    /// Both halves run in the same test so a single stub cannot satisfy one while
    /// accidentally breaking the other.
    /// </summary>
    [Fact]
    public void WriteTo_swallows_sink_failure_AuditTo_propagates_oppositional_pair()
    {
        var throwing = new ThrowingSink { ThrowOnEmit = true };

        // ── WriteTo path: must NOT throw ──────────────────────────────────────
        var writeLog = new LoggerConfiguration()
            .WriteTo.Sink(throwing)
            .CreateLogger();

        var writeAct = () => writeLog.Information("write-path test");
        writeAct.Should().NotThrow(
            "WriteTo swallows sink failures — one broken sink must not crash the application");

        // ── AuditTo path: must propagate ──────────────────────────────────────
        // The kernel re-throws AuditLogFailureException specifically, so the
        // sink's InvalidOperationException surfaces as InnerException of the wrapper.
        var auditLog = new LoggerConfiguration()
            .AuditTo.Sink(throwing)
            .CreateLogger();

        var auditAct = () => auditLog.Information("audit-path test");
        auditAct.Should().ThrowExactly<AuditLogFailureException>(
            "AuditTo must propagate sink failures — compliance contract requires the caller to know " +
            "when a log event was not delivered");
    }

    /// <summary>
    /// Complementary precision check: the exception wrapping is faithful.
    /// The inner exception must be the original throw from the sink, not a
    /// re-wrapped copy, so callers can inspect the root cause.
    /// </summary>
    [Fact]
    public void AuditTo_exception_carries_original_sink_exception_as_inner()
    {
        var throwing = new ThrowingSink { ThrowOnEmit = true };
        var log = new LoggerConfiguration()
            .AuditTo.Sink(throwing)
            .CreateLogger();

        var ex = Assert.Throws<AuditLogFailureException>(
            () => log.Information("inner exception check"));

        ex.InnerException.Should().NotBeNull(
            "the sink's original exception must be preserved as InnerException");
        ex.InnerException.Should().BeOfType<System.InvalidOperationException>(
            "ThrowingSink throws InvalidOperationException — it must survive the wrap unchanged");
    }

    /// <summary>
    /// Fluency check: <c>AuditTo.Sink()</c> returns the root <see cref="LoggerConfiguration"/>
    /// so the call chain compiles and behaves identically to <c>WriteTo.Sink()</c>.
    /// </summary>
    [Fact]
    public void AuditTo_Sink_returns_same_LoggerConfiguration_for_chaining()
    {
        var sink = new RecordingLogEventSink(new List<LogEvent>());
        var config = new LoggerConfiguration();

        var returned = config.AuditTo.Sink(sink);

        returned.Should().BeSameAs(config,
            "AuditTo.Sink must return the root LoggerConfiguration to support fluent chaining");
    }

    // ── G-SEC.3: redaction fires BEFORE audit capture ─────────────────────────

    /// <summary>
    /// Both WriteTo and AuditTo sinks must receive the <em>redacted</em> event.
    /// <c>Destructure.ByTransforming</c> strips Password before the event reaches
    /// either sink.  An audit log containing unredacted PII is a compliance failure
    /// regardless of delivery guarantees.
    /// </summary>
    [Fact]
    public void AuditTo_event_has_redaction_applied_before_capture()
    {
        var writeReceived = new List<LogEvent>();
        var auditReceived = new List<LogEvent>();

        var writeSink = new RecordingLogEventSink(writeReceived);
        var auditSink = new RecordingLogEventSink(auditReceived);

        // Redaction: project SecretBearingFixture to { Name } only — Password is stripped.
        var log = new LoggerConfiguration()
            .Destructure.ByTransforming<SecretBearingFixture>(f => new { f.Name })
            .WriteTo.Sink(writeSink)
            .AuditTo.Sink(auditSink)
            .CreateLogger();

        log.Information("login {@User}",
            new SecretBearingFixture { Name = "alice", Password = "hunter2" });

        // WriteTo path must not see the secret.
        SecretScanner.FindAll(writeReceived.Single(), "hunter2").Should().BeEmpty(
            "redaction must fire before WriteTo captures the event");

        // AuditTo path must also not see the secret — audit does not bypass redaction.
        SecretScanner.FindAll(auditReceived.Single(), "hunter2").Should().BeEmpty(
            "redaction must fire before AuditTo captures the event — " +
            "audit log must not contain unredacted PII");
    }

    /// <summary>
    /// Complementary check: the non-secret field (<c>Name</c>) survives the projection
    /// in both sinks, confirming the projection fires correctly rather than dropping
    /// the whole property.
    /// </summary>
    [Fact]
    public void AuditTo_redacted_event_preserves_non_secret_fields()
    {
        var writeReceived = new List<LogEvent>();
        var auditReceived = new List<LogEvent>();

        var log = new LoggerConfiguration()
            .Destructure.ByTransforming<SecretBearingFixture>(f => new { f.Name })
            .WriteTo.Sink(new RecordingLogEventSink(writeReceived))
            .AuditTo.Sink(new RecordingLogEventSink(auditReceived))
            .CreateLogger();

        log.Information("login {@User}",
            new SecretBearingFixture { Name = "alice", Password = "hunter2" });

        writeReceived.Single().RenderMessage().Should().Contain("alice",
            "Name must survive the projection in the WriteTo sink");
        auditReceived.Single().RenderMessage().Should().Contain("alice",
            "Name must survive the projection in the AuditTo sink");
    }

    // ── SelfLog content check (HIGH-4 / Task 6 / G-GAP.4) ───────────────────────

    /// <summary>
    /// HIGH-4 fix: the swallow path must report to <see cref="SelfLog"/> AND the
    /// message must contain actionable content — sink name or exception type.
    ///
    /// <para>
    /// A content-free assertion (just <c>HasReport = true</c>) cannot detect a stub
    /// that fires with a placeholder. This test pins the content contract.
    /// </para>
    /// </summary>
    [Fact]
    public void WriteTo_swallow_is_reported_to_SelfLog_with_meaningful_content()
    {
        var messages = new List<string>();
        SelfLog.Enable(msg => messages.Add(msg));

        try
        {
            var throwing = new ThrowingSink { ThrowOnEmit = true };

            var log = new LoggerConfiguration()
                .WriteTo.Sink(throwing)
                .CreateLogger();

            log.Information("test");

            messages.Should().NotBeEmpty("swallow must report to SelfLog");
            messages.Should().Contain(
                m => m.Contains("ThrowingSink") || m.Contains("InvalidOperationException"),
                "the report must contain the exception type or sink name — not a placeholder");
        }
        finally
        {
            SelfLog.Disable();
        }
    }

    /// <summary>
    /// When SelfLog is disabled (default), the swallow path remains completely silent —
    /// no exception propagates, no side effect occurs.
    /// </summary>
    [Fact]
    public void WriteTo_swallow_is_silent_when_SelfLog_is_disabled()
    {
        SelfLog.Disable();

        var throwing = new ThrowingSink { ThrowOnEmit = true };

        var log = new LoggerConfiguration()
            .WriteTo.Sink(throwing)
            .CreateLogger();

        var act = () => log.Information("silent swallow");
        act.Should().NotThrow("swallowed exceptions must never propagate in normal mode");
    }

    /// <summary>
    /// Enable → emit → disable → emit again: only the first emit is reported.
    /// Verifies the toggle is respected after the logger is built.
    /// </summary>
    [Fact]
    public void SelfLog_Enable_then_Disable_stops_reporting()
    {
        var messages = new List<string>();
        SelfLog.Enable(msg => messages.Add(msg));

        var log = new LoggerConfiguration()
            .WriteTo.Sink(new ThrowingSink { ThrowOnEmit = true })
            .CreateLogger();

        log.Information("while enabled");
        var afterEnable = messages.Count;

        SelfLog.Disable();
        log.Information("while disabled");
        var afterDisable = messages.Count;

        afterEnable.Should().Be(1, "one report expected while SelfLog is enabled");
        afterDisable.Should().Be(1, "no additional reports expected after SelfLog is disabled");
    }

    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class RecordingLogEventSink(List<LogEvent> list) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => list.Add(logEvent);
    }
}

