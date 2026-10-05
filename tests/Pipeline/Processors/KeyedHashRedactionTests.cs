#nullable enable

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using MMP.Herald.Addons.Compliance;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Output.Rendering;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Pipeline.Processors;
using MMP.Herald.Templating;
using Xunit;

namespace MMP.Herald.OSS.Tests.Pipeline.Processors;

/// <summary>
/// <see cref="RedactionMode.KeyedHash"/>: HMAC-SHA256 with a key on the rule. An unkeyed hash of a
/// low-entropy identifier (an SSN has about 10^9 values) is reversed by trying every value; a keyed
/// hash is not, without the key.
/// </summary>
public sealed class KeyedHashRedactionTests
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef");
    private const string Ssn = "999-12-3456";

    private static LogEvent Event(string value) =>
        new(DateTimeOffset.UnixEpoch, new LogLevel("information", "Information"), new LogCategory("audit.test"),
            "{ssn}", value, new[] { new LogProperty("ssn", value) }, LogEvent.EmptyContext);

    private static string ExpectedKeyed(byte[] key, string value) =>
        "hmac-sha256:" + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value)), 0, 16).ToLowerInvariant();

    private static string Redact(CompiledRedactionRule rule, string value) =>
        (string)new CompiledRedactionProcessor(new List<CompiledRedactionRule> { rule }).Process(Event(value))!.Properties[0].ResolvedValue!;

    [Fact]
    public void Keyed_hash_is_hmac_sha256_of_the_value_under_the_rule_key()
    {
        var redacted = Redact(new CompiledRedactionRule("ssn", RedactionMode.KeyedHash) { HashKey = Key }, Ssn);

        redacted.Should().Be(ExpectedKeyed(Key, Ssn));
        redacted.Should().NotContain(Ssn);
    }

    [Fact]
    public void Keyed_hash_differs_from_the_unkeyed_hash_and_between_keys()
    {
        var otherKey = Encoding.UTF8.GetBytes("fedcba9876543210fedcba9876543210");
        var keyed = Redact(new CompiledRedactionRule("ssn", RedactionMode.KeyedHash) { HashKey = Key }, Ssn);
        var otherKeyed = Redact(new CompiledRedactionRule("ssn", RedactionMode.KeyedHash) { HashKey = otherKey }, Ssn);
        var unkeyed = Redact(new CompiledRedactionRule("ssn", RedactionMode.Hash), Ssn);

        keyed.Should().NotBe(otherKeyed);
        keyed.Should().NotContain(unkeyed["sha256:".Length..]);
        Redact(new CompiledRedactionRule("ssn", RedactionMode.KeyedHash) { HashKey = Key }, Ssn).Should().Be(keyed);
    }

    [Fact]
    public void Existing_hash_mode_is_unchanged()
    {
        var expected = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Ssn)), 0, 8).ToLowerInvariant();
        Redact(new CompiledRedactionRule("ssn", RedactionMode.Hash), Ssn).Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public void Compiled_processor_rejects_a_keyed_rule_without_a_usable_key(int keyLength)
    {
        var rule = new CompiledRedactionRule("ssn", RedactionMode.KeyedHash) { HashKey = keyLength == 0 ? null : new byte[keyLength] };
        var act = () => new CompiledRedactionProcessor(new List<CompiledRedactionRule> { rule });
        act.Should().Throw<ArgumentException>().WithMessage("*HashKey*");
    }

    [Fact]
    public void Fast_path_redactor_applies_the_keyed_hash()
    {
        var redactor = new FastPathRedactor(new List<CompiledRedactionRule>
        {
            new("ssn", RedactionMode.KeyedHash) { HashKey = Key },
        });

        var output = redactor.Apply(new[] { new LogProperty("ssn", Ssn) }.AsSpan());

        output[0].ResolvedValue.Should().Be(ExpectedKeyed(Key, Ssn));
    }

    [Fact]
    public void Fast_path_redactor_rejects_a_keyed_rule_without_a_key()
    {
        var act = () => new FastPathRedactor(new List<CompiledRedactionRule> { new("ssn", RedactionMode.KeyedHash) });
        act.Should().Throw<ArgumentException>().WithMessage("*HashKey*");
    }

    [Fact]
    public void Output_redaction_processor_rejects_a_keyed_rule_without_a_key()
    {
        var act = () => new RedactionProcessor(new List<RedactionRule> { new("ssn", RedactionMode.KeyedHash) });
        act.Should().Throw<ArgumentException>().WithMessage("*HashKey*");
    }

    [Fact]
    public void Dsl_keyed_hash_takes_the_key_from_the_parse_overload()
    {
        var rule = RedactionRuleParser.Parse("keyedHash ssn", Key);

        rule.Mode.Should().Be(RedactionMode.KeyedHash);
        rule.HashKey.Should().BeSameAs(Key);
        Redact(rule, Ssn).Should().Be(ExpectedKeyed(Key, Ssn));
    }

    [Fact]
    public void Dsl_keyed_hash_without_a_key_fails_when_the_processor_is_built()
    {
        var rule = RedactionRuleParser.Parse("keyedHash ssn when level:information");

        rule.Mode.Should().Be(RedactionMode.KeyedHash);
        var act = () => new CompiledRedactionProcessor(new List<CompiledRedactionRule> { rule });
        act.Should().Throw<ArgumentException>().WithMessage("*HashKey*");
    }
}
