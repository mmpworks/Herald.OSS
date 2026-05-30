#nullable enable

using System;
using System.Collections.Generic;

namespace MMP.Herald.OSS.Tests.TestSupport;

/// <summary>
/// A portable event description that can drive both the real-Serilog parity oracle
/// and Herald's Serilog-compat renderer with identical inputs.
///
/// <para>
/// All fields are init-only so callers use object-initializer syntax and the spec
/// is immutable once constructed. The default property set is intentionally minimal;
/// use <see cref="WithProperties"/> or object initializers for richer corpus entries.
/// </para>
/// </summary>
public sealed class CanonicalEventSpec
{
    /// <summary>
    /// UTC timestamp with a non-zero local offset so UTC-vs-local divergence is
    /// caught in CI (MED-1 pre-mortem finding). Fixed value gives deterministic
    /// oracle output across machines and TZs.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } =
        new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.FromHours(5));  // non-UTC fixed offset

    /// <summary>
    /// Herald/Serilog level key, lower-case. Recognised values:
    /// <c>"verbose"</c>, <c>"debug"</c>, <c>"information"</c>,
    /// <c>"warning"</c>, <c>"error"</c>, <c>"fatal"</c>.
    /// Anything else is treated as <c>"information"</c> by the oracle.
    /// </summary>
    public string LevelKey { get; init; } = "information";

    /// <summary>The Serilog message template (holes like <c>"{UserId}"</c>).</summary>
    public string MessageTemplate { get; init; } = "";

    /// <summary>
    /// Name → value pairs for template holes, in declaration order.
    /// <para>
    /// The capture-mode tuple field (<c>bool destructure</c>) controls whether the
    /// value is passed to real Serilog via the destructure operator <c>{@}</c> — the
    /// oracle rewrites the template hole to <c>{@Name}</c> when <c>destructure</c>
    /// is <c>true</c> so the oracle and Herald see the same capture-mode intent.
    /// </para>
    /// </summary>
    public IReadOnlyList<(string Name, object? Value, bool Destructure)> Properties { get; init; } =
        Array.Empty<(string, object?, bool)>();

    /// <summary>Optional exception attached to the event.</summary>
    public Exception? Exception { get; init; }

    // ── Convenience factory for simple (non-destructured) scalar properties ───

    /// <summary>
    /// Create a spec whose properties are all simple scalars (no destructure).
    /// </summary>
    public static CanonicalEventSpec Simple(
        string messageTemplate,
        string levelKey = "information",
        params (string Name, object? Value)[] properties)
    {
        var props = new (string, object?, bool)[properties.Length];
        for (var i = 0; i < properties.Length; i++)
            props[i] = (properties[i].Name, properties[i].Value, false);

        return new CanonicalEventSpec
        {
            LevelKey        = levelKey,
            MessageTemplate = messageTemplate,
            Properties      = props,
        };
    }

    /// <summary>
    /// A representative corpus covering the pre-mortem risk areas.
    ///
    /// <para>
    /// CRIT-3 flag: includes a string-valued property AND a destructured object
    /// (<c>{@User}</c>) so the <c>:lj</c> compound specifier's JSON branch and the
    /// no-quotes branch are exercised in Tasks 2–6.
    /// </para>
    /// </summary>
    public static IReadOnlyList<CanonicalEventSpec> RepresentativeCorpus() =>
    [
        // Simple scalar — the happy-path baseline.
        Simple("Hello {Name}", properties: ("Name", "World")),

        // Integer scalar — all four rendering branches produce "1" for this,
        // so it is intentionally minimal (sanity only, not a discriminating test).
        Simple("User {UserId} logged in", properties: ("UserId", 42)),

        // String-valued property — discriminates the :lj literal-quotes branch
        // (CRIT-3 flag: string property required in corpus).
        Simple("Result is {Status}", properties: ("Status", "ok")),

        // Destructured object — discriminates the :lj JSON branch (CRIT-3 flag).
        // The oracle rewrites {User} → {@User} because Destructure = true.
        new CanonicalEventSpec
        {
            MessageTemplate = "Logged in as {@User}",
            Properties      = [("User", new { Name = "Alice", Age = 30 }, true)],
        },

        // Warning level — discriminates the Warning→WRN casing table (CRIT-1 flag).
        Simple("Threshold exceeded", levelKey: "warning"),

        // Fatal level.
        Simple("Process crashed", levelKey: "fatal"),

        // Event with an exception — discriminates {Exception} trailing-newline (HIGH-3 flag).
        new CanonicalEventSpec
        {
            LevelKey        = "error",
            MessageTemplate = "Unhandled failure",
            Exception       = new InvalidOperationException("test exception"),
        },
    ];
}
