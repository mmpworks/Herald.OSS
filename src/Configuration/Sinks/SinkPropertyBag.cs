// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;

namespace MMP.Herald.Configuration.Sinks;

/// <summary>
/// Read-side mirror of <see cref="SinkPropertyBagBuilder"/>. Pulls
/// typed values out of a v2 sink-config property bag — the
/// dictionary that lands on <c>LoggingRuntimeSinkDefinition.Properties</c>
/// after the dashboard / QuickLogBuilder merges the operator's input
/// with the mmpform contract defaults.
///
/// <para>
/// Every primitive is null-tolerant on the bag itself. A null bag, a
/// missing key, a null value, a wrong-type value, or a non-parseable
/// string all return <c>null</c> from the primitive. That keeps the
/// helper composable: callers stitch primitives together with the
/// null-coalesce operator and let the type system carry the
/// "absent or value" state without ceremony. Mappers stay pure;
/// providers validate required fields with their own
/// <c>ArgumentException</c> message naming the operator-facing form
/// field. Per Pass-3 design, the helper is the last line of defence
/// — the form validator catches operator typos upstream.
/// </para>
///
/// <para>
/// <b>Symmetric placement.</b> <see cref="SinkPropertyBagBuilder"/>
/// builds the bag from the mmpform contract + user values. This class
/// reads the bag the builder produced. The two helpers live in the
/// same namespace so the symmetry is visible at the file-system level
/// — write-side and read-side, side by side.
/// </para>
///
/// <para>
/// <b>Public surface.</b> Third-party sink authors and Herald
/// Compliance plugins consume this class directly when authoring
/// their own runtime-config mappers. The primitives are a stable
/// contract — semantics are pinned by both the XML doc above each
/// method and the tests in <c>Herald.OSS.Tests</c>.
/// </para>
///
/// <para>
/// <b>AOT and trim safety.</b> Every primitive uses
/// stack-allocated patterns plus the BCL's
/// <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/>
/// (trim-safe since .NET 6). No reflection, no
/// <see cref="System.Reflection.Emit"/>, no
/// <see cref="System.Text.Json.JsonSerializer"/>. The helper
/// inherits Herald.OSS's <c>IsAotCompatible</c> +
/// <c>EnableTrimAnalyzer</c> settings without exemption.
/// </para>
/// </summary>
public static class SinkPropertyBag
{
    // ── Scalar readers ──────────────────────────────────────────────

    /// <summary>
    /// Read a string from the bag. Returns <c>null</c> when the bag
    /// is null, the key is absent, the value is null, or the value's
    /// <see cref="object.ToString"/> produces an empty string.
    ///
    /// <para>
    /// The empty-string-to-null coercion is contract behaviour, not
    /// implementation detail. A blank form field in the dashboard
    /// arrives in the bag as an empty string; coercing to null lets
    /// the per-sink mapper fall through to a legacy slot (or a
    /// per-field default) instead of stomping the legacy value with
    /// a blank. Every Pass-1 and Pass-2 mapper relies on this shape.
    /// </para>
    /// </summary>
    public static string? ReadString(IReadOnlyDictionary<string, object?>? bag, string key)
    {
        if (bag is null) return null;
        if (!bag.TryGetValue(key, out var raw) || raw is null) return null;
        var text = raw.ToString();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>
    /// Read a boolean from the bag. Returns <c>null</c> on missing
    /// key, null value, or value that does not parse as a boolean.
    /// Accepts a boxed <see cref="bool"/> as-is and a string carrier
    /// (<c>"true"</c> / <c>"false"</c>, case-insensitive). Any other
    /// type returns <c>null</c> so the caller falls back to its
    /// default.
    /// </summary>
    public static bool? ReadBool(IReadOnlyDictionary<string, object?>? bag, string key)
    {
        if (bag is null) return null;
        if (!bag.TryGetValue(key, out var raw) || raw is null) return null;
        if (raw is bool b) return b;
        return bool.TryParse(raw.ToString(), out var parsed) ? parsed : (bool?)null;
    }

    /// <summary>
    /// Read a 32-bit integer from the bag. Returns <c>null</c> on
    /// missing key, null value, or unparseable value. Accepts boxed
    /// <see cref="int"/>, <see cref="long"/> (truncated to
    /// <see cref="int.MaxValue"/> / <see cref="int.MinValue"/>),
    /// <see cref="double"/> (truncated toward zero), and
    /// invariant-culture numeric strings. The truncation matches
    /// what operators expect — a JSON literal of <c>30.7</c> lands
    /// as <c>30</c> instead of failing the read.
    /// </summary>
    public static int? ReadInt(IReadOnlyDictionary<string, object?>? bag, string key)
    {
        var l = ReadLong(bag, key);
        if (l is null) return null;
        // Cognitive Complexity note: the long→int clamp is one line
        // instead of three switch arms because every numeric carrier
        // is already normalised by ReadLong. Truncation matches the
        // long→int conversion behaviour the rest of the codebase
        // already accepts.
        var v = l.Value;
        if (v > int.MaxValue) return int.MaxValue;
        if (v < int.MinValue) return int.MinValue;
        return (int)v;
    }

    /// <summary>
    /// Read a 64-bit integer from the bag. Returns <c>null</c> on
    /// missing key, null value, or unparseable value. Accepts boxed
    /// <see cref="long"/>, <see cref="int"/>, <see cref="double"/>
    /// (truncated toward zero), and invariant-culture numeric
    /// strings.
    /// </summary>
    public static long? ReadLong(IReadOnlyDictionary<string, object?>? bag, string key)
    {
        if (bag is null) return null;
        if (!bag.TryGetValue(key, out var raw) || raw is null) return null;

        // Cognitive Complexity note: switch covers the JSON
        // deserialiser's three numeric carriers (long / int / double)
        // plus the string fallback. One pass, one return per arm.
        // Mirrors the helper FileSinkRuntimeConfig has shipped since
        // the File sink first wired the v2 bag — same semantics,
        // moved upstream.
        return raw switch
        {
            long l => l,
            int i => i,
            double d => (long)d,
            string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
            _ => (long?)null
        };
    }

    /// <summary>
    /// Read a double from the bag. Returns <c>null</c> on missing
    /// key, null value, or unparseable value. Accepts boxed
    /// <see cref="double"/>, <see cref="long"/>, <see cref="int"/>,
    /// and invariant-culture numeric strings (including fractional
    /// values like <c>"7.5"</c>). Needed by sinks that surface
    /// fractional knobs (File's retention day-count carries
    /// fractions in the dashboard's slider control).
    /// </summary>
    public static double? ReadDouble(IReadOnlyDictionary<string, object?>? bag, string key)
    {
        if (bag is null) return null;
        if (!bag.TryGetValue(key, out var raw) || raw is null) return null;

        return raw switch
        {
            double d => d,
            long l => l,
            int i => i,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => n,
            _ => (double?)null
        };
    }

    /// <summary>
    /// Read an enum value from the bag by member name. Returns
    /// <c>null</c> when the bag has no string value at <paramref name="key"/>
    /// or when <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/>
    /// rejects the string. Parsing is case-insensitive so an mmpform
    /// value of <c>"local0"</c> resolves against an enum member
    /// <c>Local0</c>.
    ///
    /// <para>
    /// <b>Use when</b> the mmpform vocabulary matches the enum member
    /// names case-insensitively. <b>Skip when</b> the vocabulary
    /// differs (e.g., mmpform <c>at_most_once</c> vs
    /// <c>MqttQualityOfServiceLevel.AtMostOnce</c>) — keep the
    /// per-sink switch parser in those cases. Renaming enum values
    /// just to fit this primitive is not worth the cross-codebase
    /// churn.
    /// </para>
    ///
    /// <para>
    /// <b>AOT note.</b> <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/>
    /// is trim-safe since .NET 6 — the BCL marks the value-parsing
    /// path as preserved without per-call <c>DynamicallyAccessedMembers</c>
    /// hints.
    /// </para>
    /// </summary>
    public static T? ReadEnum<T>(IReadOnlyDictionary<string, object?>? bag, string key)
        where T : struct, Enum
    {
        var text = ReadString(bag, key);
        if (text is null) return null;
        return Enum.TryParse<T>(text, ignoreCase: true, out var value) ? value : (T?)null;
    }

    // ── Composite readers ───────────────────────────────────────────

    /// <summary>
    /// Read a nested string map from the bag. Returns <c>null</c>
    /// when the bag has no value at <paramref name="key"/> or the
    /// value is not a nested <see cref="IReadOnlyDictionary{TKey, TValue}"/>.
    /// Otherwise projects the nested entries to a
    /// <see cref="Dictionary{TKey, TValue}"/> keyed
    /// <see cref="StringComparer.OrdinalIgnoreCase"/>, dropping null
    /// values.
    ///
    /// <para>
    /// Used by sinks that read a nested object out of the bag —
    /// today File's <c>pathTokens</c> is the only consumer. Distinct
    /// from <see cref="ReadKeyValuePairs"/>, which parses a
    /// comma-separated string instead of a nested dictionary; the
    /// two are not interchangeable because they handle different
    /// bag value types.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, string>? ReadStringMap(
        IReadOnlyDictionary<string, object?>? bag, string key)
    {
        if (bag is null) return null;
        if (!bag.TryGetValue(key, out var raw) || raw is null) return null;
        if (raw is not IReadOnlyDictionary<string, object?> nested) return null;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in nested)
        {
            if (pair.Value is null) continue;
            result[pair.Key] = pair.Value.ToString() ?? string.Empty;
        }
        return result.Count == 0 ? null : result;
    }

    /// <summary>
    /// Parse a comma-separated <c>key=value</c> string out of the
    /// bag. Returns <c>null</c> when the bag has no string at
    /// <paramref name="key"/> or when no valid pairs survive
    /// parsing. Pairs are split on the first <c>=</c>; entries
    /// without an <c>=</c> or with a blank key drop silently so a
    /// stray comma in the operator's input does not crash the
    /// pipeline boot. The form validator catches the typo upstream.
    ///
    /// <para>
    /// Used by sinks that surface a map-of-strings as a single
    /// textfield in the dashboard (the form layer does not render
    /// a per-entry editor yet). GoogleCloudLogging
    /// <c>resource_labels</c>, PagerDuty
    /// <c>custom_details_template</c>, and GenericWebhook
    /// <c>headers</c> all use this shape. Distinct from
    /// <see cref="ReadStringMap"/>, which expects a nested
    /// dictionary in the bag.
    /// </para>
    ///
    /// <para>
    /// <b>Format is fixed.</b> Comma separator, <c>=</c> assignment,
    /// whitespace trimmed around each pair and around each side of
    /// the <c>=</c>. Sinks that need a different separator (e.g.,
    /// semicolon-delimited headers) keep their own local parser
    /// rather than reach for a parameterised primitive — see the
    /// Pass-3 design spec for the rationale.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, string>? ReadKeyValuePairs(
        IReadOnlyDictionary<string, object?>? bag, string key)
    {
        var raw = ReadString(bag, key);
        if (raw is null) return null;

        var pairs = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pairs.Length == 0) return null;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in pairs)
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var name = pair[..eq].Trim();
            var value = pair[(eq + 1)..].Trim();
            if (string.IsNullOrEmpty(name)) continue;
            result[name] = value;
        }
        return result.Count == 0 ? null : result;
    }

    // ── Utilities ───────────────────────────────────────────────────

    /// <summary>
    /// Coerce a legacy-slot string to "absent or value" by returning
    /// <c>null</c> when the input is null or whitespace. Lets the
    /// bag-first / legacy-fallback idiom read cleanly:
    /// <code>
    /// var endpoint = SinkPropertyBag.ReadString(bag, "endpoint")
    ///             ?? SinkPropertyBag.Nullify(definition.Uri);
    /// </code>
    ///
    /// <para>
    /// Kept separate from <see cref="ReadString"/> because the two
    /// take different inputs — <see cref="ReadString"/> reads from
    /// the bag; <see cref="Nullify"/> normalises a legacy
    /// <c>string?</c> slot value the caller already has in hand.
    /// Merging them would force every bag-read call site to either
    /// know about legacy slots or pass <c>null</c> for them, and
    /// neither change pays back.
    /// </para>
    /// </summary>
    public static string? Nullify(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Parse a human byte size literal like <c>10MB</c>,
    /// <c>1GB</c>, <c>500KB</c>, <c>42B</c>, or a plain number.
    /// Returns <c>null</c> on empty / unparseable / zero / negative
    /// input — every "absent" answer flows through the same null,
    /// so the caller's <c>?? default</c> path stays clean.
    ///
    /// <para>
    /// Suffix matching is case-insensitive. The fractional path is
    /// honoured (<c>1.5GB</c> rounds to the nearest byte). The
    /// helper ships here because File is the only current consumer
    /// (<c>maxFileSize</c> + <c>totalSizeCap</c>) but the shape is
    /// what every future per-sink batching mmpform that names a
    /// "max body bytes" will use.
    /// </para>
    /// </summary>
    public static long? ParseHumanByteSize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Cognitive Complexity note: suffix detection runs once, in
        // descending length order so "GB" beats "B". The fallthrough
        // is a plain numeric parse with multiplier 1. Keeps the path
        // straight-line without nested branches.
        var s = raw.Trim();
        long multiplier = 1;
        if (s.EndsWith("GB", StringComparison.OrdinalIgnoreCase)) { multiplier = 1_073_741_824L; s = s[..^2]; }
        else if (s.EndsWith("MB", StringComparison.OrdinalIgnoreCase)) { multiplier = 1_048_576L; s = s[..^2]; }
        else if (s.EndsWith("KB", StringComparison.OrdinalIgnoreCase)) { multiplier = 1_024L; s = s[..^2]; }
        else if (s.EndsWith("B",  StringComparison.OrdinalIgnoreCase)) { multiplier = 1L; s = s[..^1]; }

        if (!double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return null;
        if (n <= 0) return null;
        return (long)Math.Round(n * multiplier);
    }
}
