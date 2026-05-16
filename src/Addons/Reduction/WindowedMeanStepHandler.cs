// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Pipeline;

namespace MMP.Herald.Addons.Reduction;

/// <summary>
/// Pipeline step handler that installs a <see cref="WindowedMeanLogger"/>
/// decorator. Carries its own rule list so callers can register one
/// handler per rule set, or install multiple handlers under different
/// step names if they want to layer reductions.
///
/// <para>
/// <b>Where it sits.</b> Reductions belong before fan-out — they need
/// to absorb originals and emit summaries before the events scatter to
/// sinks. <see cref="StepName"/> uses <c>"windowedMean"</c>; the strategy
/// that includes this step decides where in the pipeline assembly order
/// it goes (typically right before the rendering / fan-out tail).
/// </para>
///
/// <para>
/// <b>Registration.</b> Plugins call
/// <see cref="PipelineStepHandlerRegistry.Register"/> at bootstrap.
/// Hosts that don't want the addon don't pay for it — the registry only
/// resolves what's been registered, and the decorator is only constructed
/// when the strategy includes the step.
/// </para>
/// </summary>
public sealed class WindowedMeanStepHandler : IPipelineStepHandler
{
    public const string StepNameKey = "windowedMean";

    private readonly IReadOnlyList<WindowedMeanRule> _rules;

    public WindowedMeanStepHandler(IReadOnlyList<WindowedMeanRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
    }

    public string StepName => StepNameKey;

    public void Apply(PipelineStepApplyContext context)
    {
        if (_rules.Count == 0) return;

        var decorator = new WindowedMeanLogger(context.Builder.CurrentPipeline, _rules);
        context.Builder.SetPipeline(decorator);
    }
}
