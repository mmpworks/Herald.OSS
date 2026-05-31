#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.OSS.Tests.Helpers;
using MMP.Herald.Pipeline;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline.Kernel;

public sealed class ExternalSourceRegistrarTests
{
    private const string ReferenceSource = "ref-token-AAAA-BBBB-CCCC-DDDD-EEEE";
    private const string RawKey = "plugin-alpha";
    private const long Timestamp = 20260425103045L;

    private static (ExternalSourceRegistrar registrar,
        Dictionary<string, GenSourceGatedSink> gates,
        Dictionary<string, SpyLogger> spies) BuildHarness(params string[] aliases)
    {
        var gates = new Dictionary<string, GenSourceGatedSink>(StringComparer.OrdinalIgnoreCase);
        var spies = new Dictionary<string, SpyLogger>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in aliases)
        {
            var spy = new SpyLogger();
            spies[alias] = spy;
            gates[alias] = new GenSourceGatedSink(spy, ReferenceSource);
        }
        return (new ExternalSourceRegistrar(ReferenceSource, gates), gates, spies);
    }

    private static LogEvent EventWith(string? source) =>
        new LogEvent(
            TimeUtc: DateTimeOffset.UtcNow,
            Level: KnownLogLevels.Information,
            Category: LogCategory.App,
            MessageTemplate: "msg",
            Message: "msg",
            Properties: LogEvent.EmptyProperties,
            Context: LogEvent.EmptyContext,
            GenSource: source);

    [Fact]
    public void Register_returns_a_32_hex_char_derived_key()
    {
        var (reg, _, _) = BuildHarness("file");

        var derived = reg.Register(RawKey, Timestamp, "file");

        derived.Should().HaveLength(32);
        derived.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public void Derived_key_is_deterministic_for_same_inputs()
    {
        var (reg1, _, _) = BuildHarness("file");
        var (reg2, _, _) = BuildHarness("file");

        var d1 = reg1.Register(RawKey, Timestamp, "file");
        var d2 = reg2.Register(RawKey, Timestamp, "file");

        // Same reference + same key + same timestamp on a fresh registrar
        // produces the same derived key. This is what makes the timestamp
        // lock load-bearing — a different timestamp must change the derived
        // key, otherwise the lock is decorative.
        d1.Should().Be(d2);
    }

    [Fact]
    public void Different_timestamp_produces_different_derived_key()
    {
        var (reg1, _, _) = BuildHarness("file");
        var (reg2, _, _) = BuildHarness("file");

        var d1 = reg1.Register(RawKey, Timestamp, "file");
        var d2 = reg2.Register(RawKey, Timestamp + 1, "file");

        d1.Should().NotBe(d2);
    }

    [Fact]
    public void Different_reference_produces_different_derived_key()
    {
        var spy1 = new SpyLogger();
        var spy2 = new SpyLogger();
        var ref1 = "ref-AAAA";
        var ref2 = "ref-BBBB";
        var reg1 = new ExternalSourceRegistrar(ref1,
            new Dictionary<string, GenSourceGatedSink>
            {
                ["file"] = new GenSourceGatedSink(spy1, ref1),
            });
        var reg2 = new ExternalSourceRegistrar(ref2,
            new Dictionary<string, GenSourceGatedSink>
            {
                ["file"] = new GenSourceGatedSink(spy2, ref2),
            });

        var d1 = reg1.Register(RawKey, Timestamp, "file");
        var d2 = reg2.Register(RawKey, Timestamp, "file");

        d1.Should().NotBe(d2,
            "two separate pipelines with the same raw key + timestamp must " +
            "produce different derived keys — that's what stops a key from " +
            "one pipeline being valid against another");
    }

    [Fact]
    public void Registered_sink_accepts_event_stamped_with_derived_key()
    {
        var (reg, gates, spies) = BuildHarness("file", "json");
        var derived = reg.Register(RawKey, Timestamp, "file");

        gates["file"].Log(EventWith(derived));
        gates["json"].Log(EventWith(derived));

        spies["file"].Events.Should().ContainSingle();
        spies["json"].Events.Should().BeEmpty(
            "json wasn't named in the registration; the derived key isn't accepted there");
    }

    [Fact]
    public void Same_key_same_timestamp_can_add_more_sinks()
    {
        var (reg, gates, spies) = BuildHarness("file", "json", "console");

        var derived = reg.Register(RawKey, Timestamp, "file");
        var derivedAgain = reg.Register(RawKey, Timestamp, "json", "console");

        derivedAgain.Should().Be(derived,
            "re-registration with matching timestamp returns the same key");
        gates["file"].Log(EventWith(derived));
        gates["json"].Log(EventWith(derived));
        gates["console"].Log(EventWith(derived));

        spies["file"].Events.Should().ContainSingle();
        spies["json"].Events.Should().ContainSingle();
        spies["console"].Events.Should().ContainSingle();
    }

    [Fact]
    public void Same_key_different_timestamp_throws()
    {
        var (reg, _, _) = BuildHarness("file");
        reg.Register(RawKey, Timestamp, "file");

        var act = () => reg.Register(RawKey, Timestamp + 1, "file");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*different timestamp*");
    }

    [Fact]
    public void Same_key_different_timestamp_does_not_alter_existing_registration()
    {
        var (reg, gates, spies) = BuildHarness("file");
        var derived = reg.Register(RawKey, Timestamp, "file");

        // Adversarial: a rogue caller tries to overwrite the registration.
        try { reg.Register(RawKey, Timestamp + 1, "file"); }
        catch (InvalidOperationException) { /* expected */ }

        // Original key still accepted.
        gates["file"].Log(EventWith(derived));
        spies["file"].Events.Should().ContainSingle();
    }

    [Fact]
    public void Register_with_unknown_sink_alias_throws_and_does_not_partial_apply()
    {
        var (reg, gates, spies) = BuildHarness("file");

        var act = () => reg.Register(RawKey, Timestamp, "file", "missing");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*missing*");
        // The validation happens up front so 'file' is not registered either.
        reg.Registrations.Should().BeEmpty();
    }

    [Fact]
    public void Revoke_with_matching_timestamp_removes_acceptance()
    {
        var (reg, gates, spies) = BuildHarness("file");
        var derived = reg.Register(RawKey, Timestamp, "file");

        var removed = reg.TryRevoke(RawKey, Timestamp);

        removed.Should().BeTrue();
        gates["file"].Log(EventWith(derived));
        spies["file"].Events.Should().BeEmpty();
        reg.Registrations.Should().NotContainKey(RawKey);
    }

    [Fact]
    public void Revoke_with_wrong_timestamp_throws_and_keeps_registration_active()
    {
        var (reg, gates, spies) = BuildHarness("file");
        var derived = reg.Register(RawKey, Timestamp, "file");

        var act = () => reg.TryRevoke(RawKey, Timestamp + 1);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*different timestamp*");
        gates["file"].Log(EventWith(derived));
        spies["file"].Events.Should().ContainSingle(
            "revocation must not partially apply on bad timestamp");
    }

    [Fact]
    public void Revoke_unknown_key_returns_false()
    {
        var (reg, _, _) = BuildHarness("file");

        reg.TryRevoke("never-registered", Timestamp).Should().BeFalse();
    }

    [Fact]
    public void RebindGates_replays_registrations_against_new_gates()
    {
        var (reg, _, _) = BuildHarness("file", "json");
        var derived = reg.Register(RawKey, Timestamp, "file", "json");

        // Hot reload simulation: build fresh gates and replay.
        var newSpies = new Dictionary<string, SpyLogger>(StringComparer.OrdinalIgnoreCase);
        var newGates = new Dictionary<string, GenSourceGatedSink>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in new[] { "file", "json" })
        {
            var spy = new SpyLogger();
            newSpies[alias] = spy;
            newGates[alias] = new GenSourceGatedSink(spy, ReferenceSource);
        }

        reg.RebindGates(newGates);

        // Events with the original derived key reach the *new* gates because
        // the registrar replayed the registration.
        newGates["file"].Log(EventWith(derived));
        newGates["json"].Log(EventWith(derived));

        newSpies["file"].Events.Should().ContainSingle();
        newSpies["json"].Events.Should().ContainSingle();
    }

    [Fact]
    public void Registrations_snapshot_is_independent()
    {
        var (reg, _, _) = BuildHarness("file");
        reg.Register(RawKey, Timestamp, "file");

        var snapshot1 = reg.Registrations;
        var snapshot2 = reg.Registrations;

        snapshot1.Should().NotBeSameAs(snapshot2);
        snapshot1.Should().ContainKey(RawKey);
        snapshot2.Should().ContainKey(RawKey);
    }

    [Fact]
    public void Adversarial_pipeline_isolation_a_key_from_one_pipeline_is_not_valid_on_another()
    {
        var refA = "pipeline-A-aaaa";
        var refB = "pipeline-B-bbbb";
        var spyA = new SpyLogger();
        var spyB = new SpyLogger();
        var gateA = new GenSourceGatedSink(spyA, refA);
        var gateB = new GenSourceGatedSink(spyB, refB);

        var regA = new ExternalSourceRegistrar(refA,
            new Dictionary<string, GenSourceGatedSink> { ["file"] = gateA });
        var regB = new ExternalSourceRegistrar(refB,
            new Dictionary<string, GenSourceGatedSink> { ["file"] = gateB });

        var keyA = regA.Register(RawKey, Timestamp, "file");
        // Same raw key on pipeline B with same timestamp would still be a
        // separate registration with a different derived key.
        var keyB = regB.Register(RawKey, Timestamp, "file");

        keyA.Should().NotBe(keyB);

        // Adversary tries to use pipeline A's derived key on pipeline B's gate.
        gateB.Log(EventWith(keyA));
        spyB.Events.Should().BeEmpty();

        // Pipeline B's own derived key still works on its own gate.
        gateB.Log(EventWith(keyB));
        spyB.Events.Should().ContainSingle();
    }
}
