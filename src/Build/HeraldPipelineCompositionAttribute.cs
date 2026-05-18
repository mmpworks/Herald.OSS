// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;

namespace MMP.Herald.Build;

/// <summary>
/// Assembly-level marker emitted by the Herald.OSS <c>build/Herald.OSS.targets</c>
/// MSBuild file. Carries the value of the consumer's
/// <c>&lt;HeraldPipelineComposition&gt;</c> property so a host process can
/// observe at runtime which pipeline composition shape the assembly was built
/// against.
///
/// <para>
/// <b>Why this exists.</b> V1.1 lets a consumer commit at build time to a
/// pipeline composition (currently only <c>Dynamic</c>; V2 reserves
/// <c>SingleKernelSink</c> for the elided-dispatch path). The runtime can't
/// reverse-engineer that choice from the public API surface — the assembly
/// attribute makes it observable for diagnostic tooling, health endpoints,
/// and support bundles.
/// </para>
///
/// <para>
/// <b>Allowed values.</b> V1.1: <c>Dynamic</c>. Future versions will
/// additively accept new values. Unknown values trigger
/// <c>HRLD0010</c> at build time so an operator never ships an unintelligible
/// composition tag.
/// </para>
///
/// <para>
/// <b>Stability.</b> Constructor signature and <see cref="Composition"/>
/// property are part of the V1.1 contract. Future additions extend
/// additively.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class HeraldPipelineCompositionAttribute : Attribute
{
    /// <summary>
    /// Create the attribute with the resolved <c>&lt;HeraldPipelineComposition&gt;</c>
    /// MSBuild value.
    /// </summary>
    /// <param name="composition">
    /// The composition tag. V1.1 emits <c>"Dynamic"</c> by default; future
    /// versions add additional values.
    /// </param>
    public HeraldPipelineCompositionAttribute(string composition) => Composition = composition;

    /// <summary>
    /// The composition value as it appeared in MSBuild after defaulting. V1.1
    /// emits <c>"Dynamic"</c> when the property is unset.
    /// </summary>
    public string Composition { get; }
}
