#nullable enable
#if NET9_0_OR_GREATER

using System;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.OSS.Tests.Infrastructure;
using MMP.Herald.Serilog.Configuration;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Events;
using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.Seams;

public sealed class DestructuringPolicyBridgeTests
{
    // G-SEC.1 -- ship-blocking, full-output scan

    [Fact]
    public void Destructure_With_redacting_policy_strips_secret_from_entire_output()
    {
        var received = new List<LogEvent>();
        var recording = new RecordingLogEventSink(received);
        var log = new LoggerConfiguration()
            .Destructure.With(new PasswordStrippingPolicy())
            .WriteTo.Sink(recording)
            .CreateLogger();

        log.Information("login {@User}", new SecretBearingFixture { Name = "alice", Password = "hunter2" });

        var evt = Assert.Single(received);
        var leaks = SecretScanner.FindAll(evt, "hunter2");
        leaks.Should().BeEmpty(
            "PasswordStrippingPolicy must remove Password; non-empty = bridge silently no-op (PII leak)");
    }

    [Fact]
    public void Removing_the_policy_registration_makes_the_secret_leak()
    {
        var received = new List<LogEvent>();
        var recording = new RecordingLogEventSink(received);
        var log = new LoggerConfiguration()
            .WriteTo.Sink(recording)
            .CreateLogger();

        log.Information("login {@User}", new SecretBearingFixture { Name = "alice", Password = "hunter2" });

        var evt = Assert.Single(received);
        var leaks = SecretScanner.FindAll(evt, "hunter2");
        leaks.Should().NotBeEmpty(
            "without a policy, default projection exposes all public properties including Password");
    }

    // Destructure.ByTransforming -- clean path

    [Fact]
    public void Destructure_ByTransforming_strips_password_via_projection_form()
    {
        var received = new List<LogEvent>();
        var recording = new RecordingLogEventSink(received);
        var log = new LoggerConfiguration()
            .Destructure.ByTransforming<SecretBearingFixture>(f => new { f.Name })
            .WriteTo.Sink(recording)
            .CreateLogger();

        log.Information("login {@User}", new SecretBearingFixture { Name = "alice", Password = "hunter2" });

        var evt = Assert.Single(received);
        SecretScanner.FindAll(evt, "hunter2").Should().BeEmpty(
            "ByTransforming with a Name-only projection must exclude Password entirely");
    }

    [Fact]
    public void Destructure_ByTransforming_preserves_non_secret_fields()
    {
        var received = new List<LogEvent>();
        var recording = new RecordingLogEventSink(received);
        var log = new LoggerConfiguration()
            .Destructure.ByTransforming<SecretBearingFixture>(f => new { f.Name })
            .WriteTo.Sink(recording)
            .CreateLogger();

        log.Information("user {@User}", new SecretBearingFixture { Name = "alice", Password = "hunter2" });

        var evt = Assert.Single(received);
        evt.RenderMessage().Should().Contain("alice",
            "Name must survive the projection; only Password is stripped");
    }

    // Destructure.With -- IDestructuringPolicy bridge

    [Fact]
    public void Destructure_With_policy_that_does_not_match_passes_value_through()
    {
        var received = new List<LogEvent>();
        var recording = new RecordingLogEventSink(received);
        var log = new LoggerConfiguration()
            .Destructure.With(new PasswordStrippingPolicy())
            .WriteTo.Sink(recording)
            .CreateLogger();

        log.Information("count={@Count}", 42);

        var evt = Assert.Single(received);
        evt.RenderMessage().Should().Contain("42",
            "a policy that does not match int must pass through to default rendering");
    }

    // G-VM.2 -- cyclic/self-referential guard

    [Fact]
    public void Destructure_With_policy_over_self_referential_object_does_not_overflow()
    {
        var received = new List<LogEvent>();
        var recording = new RecordingLogEventSink(received);
        var log = new LoggerConfiguration()
            .Destructure.With(new CyclicRedactingPolicy())
            .WriteTo.Sink(recording)
            .CreateLogger();

        var cyclic = new CyclicFixture { Name = "bob", Password = "s3cr3t" };
        cyclic.Self = cyclic;

        var act = () => log.Information("node {@Node}", cyclic);
        act.Should().NotThrow(
            "the policy bridge must not recurse unboundedly into a cyclic object graph");

        var evt = Assert.Single(received);
        SecretScanner.FindAll(evt, "s3cr3t").Should().BeEmpty(
            "the redacting policy must fire even when the object has a self-reference");
    }

    // CRIT-3 -- registration does not throw when projector is present

    [Fact]
    public void Destructure_With_valid_policy_does_not_throw_at_registration()
    {
        var act = () =>
        {
            var _ = new LoggerConfiguration()
                .Destructure.With(new PasswordStrippingPolicy());
        };
        act.Should().NotThrow(
            "when the P1 projector factory is accessible, Destructure.With must register silently");
    }

    // AsScalar path

    [Fact]
    public void Destructure_AsScalar_renders_type_as_scalar_property()
    {
        var received = new List<LogEvent>();
        var recording = new RecordingLogEventSink(received);
        var log = new LoggerConfiguration()
            .Destructure.AsScalar<SecretBearingFixture>()
            .WriteTo.Sink(recording)
            .CreateLogger();

        var fixture = new SecretBearingFixture { Name = "charlie", Password = "hunter2" };
        log.Information("user {@User}", fixture);

        var evt = Assert.Single(received);
        evt.Properties.Should().ContainKey("User",
            "the AsScalar property must be present in the event dictionary");
    }

    // Test doubles

    private sealed class RecordingLogEventSink(List<LogEvent> received) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => received.Add(logEvent);
    }

    private sealed class PasswordStrippingPolicy : MMP.Herald.Serilog.Core.IDestructuringPolicy
    {
        public bool TryDestructure(
            object value,
            ILogEventPropertyValueFactory propertyValueFactory,
            out LogEventPropertyValue result)
        {
            if (value is not SecretBearingFixture fixture)
            {
                result = null!;
                return false;
            }

            var nameValue = propertyValueFactory.CreatePropertyValue(
                fixture.Name, destructureObjects: false);
            result = new StructureValue(
                new[] { new LogEventProperty("Name", nameValue) },
                typeTag: "SecretBearingFixture");
            return true;
        }
    }

    private sealed class CyclicFixture
    {
        public string Name { get; set; } = "";
        public string Password { get; set; } = "";
        public CyclicFixture? Self { get; set; }
    }
    private sealed class CyclicRedactingPolicy : MMP.Herald.Serilog.Core.IDestructuringPolicy
    {
        public bool TryDestructure(
            object value,
            ILogEventPropertyValueFactory propertyValueFactory,
            out LogEventPropertyValue result)
        {
            if (value is not CyclicFixture fixture)
            {
                result = null!;
                return false;
            }

            var nameValue = propertyValueFactory.CreatePropertyValue(
                fixture.Name, destructureObjects: false);
            result = new StructureValue(
                new[] { new LogEventProperty("Name", nameValue) },
                typeTag: "CyclicFixture");
            return true;
        }
    }

}

#endif
