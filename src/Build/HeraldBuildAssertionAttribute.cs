// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;

namespace MMP.Herald.Build;

/// <summary>
/// Assembly-level marker emitted by Herald.OSS's interceptor generator into
/// every consumer assembly that opts into source-generated dispatch. Allows a
/// host process to confirm at runtime that a referenced assembly was built
/// with the expected Herald interceptor surface.
///
/// <para>
/// Read with <c>Assembly.GetCustomAttribute&lt;HeraldBuildAssertionAttribute&gt;()</c>.
/// All properties are init-only strings or bools; no reflection-into-types is
/// required to read them, which keeps the lookup AOT- and trim-safe.
/// </para>
///
/// <para>
/// <b>Why this exists.</b> Herald's interceptors run at compile time only; once
/// the consumer ships, an operator inspecting the binary can't tell from the
/// public API surface whether call sites were baked or whether the runtime
/// resolver path is doing the work. The build-assertion lets diagnostic
/// tooling — health checks, /api/system/build endpoints, support-bundle
/// collectors — surface that information with no behavioural side effect.
/// </para>
///
/// <para>
/// <b>Stability.</b> Property names and types are part of the V1 contract.
/// Future additions extend additively; existing properties are not renamed
/// or repurposed.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class HeraldBuildAssertionAttribute : Attribute
{
    /// <summary>
    /// True when the consumer's assembly was built with Herald's interceptor
    /// generator enabled (the default). False when the consumer set
    /// <c>&lt;HeraldInterceptorsEnabled&gt;false&lt;/HeraldInterceptorsEnabled&gt;</c>
    /// to opt out — every literal-template call site stays on the runtime
    /// resolver path in that build.
    /// </summary>
    public bool InterceptorsEnabled { get; init; }

    /// <summary>
    /// True when <c>HeraldStrictMode</c> was set in the consumer's MSBuild,
    /// escalating Herald analyzer warnings (HRLDxxxx) to errors. Informational —
    /// the strictness setting only affects build-time behaviour.
    /// </summary>
    public bool StrictMode { get; init; }

    /// <summary>
    /// Comma-separated list of built-in policies whose literal property names
    /// are baked into this assembly's interceptors. V1 always emits
    /// <c>"Pascal,Snake,Camel"</c> when <see cref="InterceptorsEnabled"/> is
    /// true; the property surfaces the explicit set so a future release that
    /// adds a built-in (e.g. <c>KebabCase</c>) reads as a different value
    /// without bumping the attribute shape.
    /// </summary>
    public string BakedPolicies { get; init; } = "";

    /// <summary>
    /// Total count of Herald call sites the generator baked into this assembly.
    /// Zero when <see cref="InterceptorsEnabled"/> is false. Useful for
    /// operator surfaces — a sudden drop in this number across two builds
    /// suggests an opt-out or a major refactor of the dispatch path.
    /// </summary>
    public int InterceptedCallSites { get; init; }

    /// <summary>
    /// The value of <c>&lt;HeraldNamingPolicyAssertion&gt;</c> at build time, or
    /// the empty string when unset. V1.1 supports the single value
    /// <c>"Default"</c> — the consumer's build-time promise that no runtime
    /// naming-policy override is in play, allowing the interceptor generator to
    /// emit per-call-site single-lane interceptors that skip the
    /// <see cref="MMP.Herald.Templating.BuiltinPolicy"/> dispatch entirely.
    ///
    /// <para>
    /// HRLD0051 fires at build time when an asserting assembly calls
    /// <c>WithNamingPolicy(...)</c> or <c>InstallNamingPolicy(...)</c>. A
    /// violation that escapes (e.g., via reflection) results in baked Pascal
    /// names regardless of the runtime policy — documented as an assertion-
    /// contract violation, not a runtime error.
    /// </para>
    /// </summary>
    public string NamingPolicyAssertion { get; init; } = "";
}
