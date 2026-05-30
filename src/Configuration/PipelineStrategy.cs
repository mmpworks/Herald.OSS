#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace MMP.Herald.Configuration;

/// <summary>
/// Defines the order of decorators in the logging pipeline.
///
/// The pipeline is a linear decorator chain. Each position holds one decorator.
/// PipelineStrategy lets you control the order rather than accepting the default.
///
/// Apache Core strategies:
///   PipelineStrategy.Default()      -- async with in-queue filtering (filter before render+batch)
///   PipelineStrategy.FilterEarly()  -- filter before async (drops never enter the queue)
///   PipelineStrategy.Minimal()      -- filtering + fanout only
///
/// Custom (Apache steps only):
///   PipelineStrategy.Create()
///       .Swappable()
///       .Async()
///       .Filtering()
///       .Rendering()
///       .Batching()
///       .PostFiltering()
///       .FanOut()
///
/// JSON:
///   "pipelineStrategy": ["swappable", "async", "filtering", "rendering", "batching", "postFiltering", "fanOut"]
///
/// Plugin-supplied steps (HotPath, FlightRecorder, EventProcessing,
/// CircuitBreaker, Retry, DurableBuffer, Fallback, Audit) are added by
/// extension methods on PipelineStrategy that ship in the corresponding
/// plugin assembly. Configs that name plugin steps in JSON require the
/// builder to be configured with WithPlugin&lt;TPlugin&gt;() before
/// deserialization so the steps are registered.
///
/// Rules:
/// - FanOut is always last (it's the terminal fan-out to sinks). If omitted, it's appended.
/// - Decorators not listed are skipped even if configured (e.g., omitting "async" disables async).
/// - RenderingLogger should be inside the async boundary (after Async). Validation warns if not.
/// - Each step appears at most once.
/// </summary>
public sealed class PipelineStrategy
{
    private readonly List<PipelineStep> _steps = new();

    private PipelineStrategy() { }

    // -- Factory --

    /// <summary>Start building a custom strategy.</summary>
    public static PipelineStrategy Create() => new();

    // -- Fluent step methods --

    public PipelineStrategy Swappable() { _steps.Add(PipelineStep.Swappable); return this; }
    public PipelineStrategy HotPath() { _steps.Add(PipelineStep.HotPath); return this; }
    public PipelineStrategy Async() { _steps.Add(PipelineStep.Async); return this; }
    public PipelineStrategy Rendering() { _steps.Add(PipelineStep.Rendering); return this; }
    public PipelineStrategy Batching() { _steps.Add(PipelineStep.Batching); return this; }
    public PipelineStrategy Filtering() { _steps.Add(PipelineStep.Filtering); return this; }
    public PipelineStrategy PostFiltering() { _steps.Add(PipelineStep.PostFiltering); return this; }
    public PipelineStrategy EventProcessing() { _steps.Add(PipelineStep.EventProcessing); return this; }
    public PipelineStrategy FlightRecorder() { _steps.Add(PipelineStep.FlightRecorder); return this; }
    public PipelineStrategy FanOut() { _steps.Add(PipelineStep.FanOut); return this; }

    // Pro/Enterprise fluent methods (CircuitBreaker, Retry, DurableBuffer,
    // Fallback, Audit) are supplied by plugin assemblies as extension methods
    // on PipelineStrategy. The plugin's [ModuleInitializer] calls
    // PipelineStep.Register(...) before the first call. Consumers must
    // reference the plugin assembly explicitly via the builder's
    // WithPlugin<TPlugin>() before deserializing a config that names the
    // corresponding step.

    /// <summary>
    /// Add a custom plugin step by name. The step must be registered via
    /// <see cref="PipelineStep.Register"/> and a matching
    /// <see cref="Pipeline.IConfigurablePipelineDecorator"/> must be provided
    /// to the builder via <c>WithPipelineDecorator()</c>.
    /// </summary>
    public PipelineStrategy Custom(string stepName)
    {
        var step = PipelineStep.FromName(stepName)
            ?? PipelineStep.Register(stepName);
        _steps.Add(step);
        return this;
    }

    /// <summary>The ordered list of pipeline steps.</summary>
    public IReadOnlyList<PipelineStep> Steps
    {
        get
        {
            // FanOut is always terminal. Append if not explicitly listed.
            if (_steps.Count == 0 || _steps[^1] != PipelineStep.FanOut)
            {
                var result = new List<PipelineStep>(_steps) { PipelineStep.FanOut };
                return result;
            }
            return _steps;
        }
    }

    // -- Presets --

    /// <summary>
    /// Default strategy for Apache Core. Filtering runs inside the async
    /// boundary but before rendering and batching, so rejected events pay
    /// only the async hop — no template parse, no batch buffer slot.
    /// EventProcessing sits at the end of the chain so processors see
    /// post-filtered, post-rendered events; when no processors are
    /// configured the step is dormant (no decorator wired).
    ///
    /// Order: Swappable → Async → Filtering → Rendering → Batching
    ///        → PostFiltering → EventProcessing → FanOut
    ///
    /// All steps are Community-tier after the Step 2a re-tier — Apache
    /// consumers get the full chain without any license check.
    /// </summary>
    public static PipelineStrategy Default() => Create()
        .Swappable()
        .Async()
        .Filtering()
        .Rendering()
        .Batching()
        .PostFiltering()
        .EventProcessing()
        .FanOut();

    /// <summary>
    /// Filter-early strategy. Drops events before they enter the async queue.
    /// Saves worker-thread CPU and queue capacity. A FlightRecorder plugin
    /// (if loaded) cannot buffer below-minimum events because they never
    /// reach the queue.
    ///
    /// Order: Swappable → Filtering → Async → Rendering → Batching → FanOut
    /// </summary>
    public static PipelineStrategy FilterEarly() => Create()
        .Swappable()
        .Filtering()
        .Async()
        .Rendering()
        .Batching()
        .FanOut();

    /// <summary>
    /// Minimal strategy. Synchronous, no async, no batching. Just filter and deliver.
    ///
    /// Order: Swappable → Filtering → FanOut
    /// </summary>
    public static PipelineStrategy Minimal() => Create()
        .Swappable()
        .Filtering()
        .FanOut();

    // -- Validation --

    /// <summary>
    /// Validate the strategy for known anti-patterns. Returns warnings (not errors)
    /// because all orderings are structurally valid -- some are just suboptimal.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var warnings = new List<string>();
        var steps = Steps;

        var renderingIndex = IndexOf(steps, PipelineStep.Rendering);
        var asyncIndex = IndexOf(steps, PipelineStep.Async);
        var filteringIndex = IndexOf(steps, PipelineStep.Filtering);
        var batchingIndex = IndexOf(steps, PipelineStep.Batching);

        // FlightRecorder is a plugin-supplied step; look it up by name so this
        // check works when the FlightRecorder plugin is loaded and silently
        // skips the related ordering checks when it isn't.
        var flightRecorder = PipelineStep.FromName("flightRecorder");
        var flightRecorderIndex = flightRecorder is null ? -1 : IndexOf(steps, flightRecorder);

        // Rendering before Async means rendering happens on the calling thread
        if (renderingIndex >= 0 && asyncIndex >= 0 && renderingIndex < asyncIndex)
            warnings.Add("RenderingLogger is before AsyncLogger. Template rendering will happen on the calling thread, not the worker thread.");

        // Filtering after Batching without FlightRecorder wastes batch buffer on events that get dropped
        if (filteringIndex >= 0 && batchingIndex >= 0 && filteringIndex > batchingIndex && flightRecorderIndex < 0)
            warnings.Add("FilteringLogger is after BatchingLogger with no FlightRecorder. Filtered events waste batch buffer space. Consider FilterEarly() strategy or adding FlightRecorder.");

        // FlightRecorder without Filtering after it has nothing to buffer for
        if (flightRecorderIndex >= 0 && (filteringIndex < 0 || filteringIndex < flightRecorderIndex))
            warnings.Add("FlightRecorder is after FilteringLogger or FilteringLogger is absent. The flight recorder needs to see events before they're filtered.");

        // Duplicate steps
        var seen = new HashSet<PipelineStep>();
        foreach (var step in steps)
        {
            if (!seen.Add(step))
                warnings.Add($"Duplicate pipeline step: {step}. Each decorator should appear at most once.");
        }

        return warnings;
    }

    /// <summary>
    /// Rules-driven validation. Each step's PipelineStepRules is evaluated against
    /// the actual step order. Issues identify the affected steps for dashboard highlighting.
    /// Vendor plugins just declare rules - the validator does the rest.
    /// </summary>
    public IReadOnlyList<PipelineValidationIssue> ValidateDetailed()
    {
        var issues = new List<PipelineValidationIssue>();
        RunRules(Steps, issues);
        return issues;
    }

    /// <summary>
    /// Validate a raw list of step names without auto-appending FanOut.
    /// Used by the dashboard to validate the user's local arrangement as-is.
    /// Unknown step names are skipped (FromName returns null) — same
    /// forgiveness the previous implementation had.
    /// </summary>
    public static IReadOnlyList<PipelineValidationIssue> ValidateStepNames(IReadOnlyList<string> stepNames)
    {
        var steps = new List<PipelineStep>();
        foreach (var name in stepNames)
        {
            var step = PipelineStep.FromName(name);
            if (step is not null) steps.Add(step);
        }

        var issues = new List<PipelineValidationIssue>();
        RunRules(steps, issues);
        return issues;
    }

    // Shared walker for ValidateDetailed and ValidateStepNames. Locks the
    // rule wording, severities, and AffectedSteps shape in one place so the
    // two public entry points can never drift. Both callers supply the
    // already-resolved step list and an issues sink they own.
    //
    // Cognitive Complexity: each rule type is a small linear block. Pulled
    // out as private static helpers below so the orchestrator stays readable
    // and the per-rule shape is easy to extend without re-threading the
    // whole walker.
    private static void RunRules(IReadOnlyList<PipelineStep> steps, List<PipelineValidationIssue> issues)
    {
        // Build index lookup: stepName -> position.
        var indexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < steps.Count; i++)
            indexMap[steps[i].Name] = i;

        var presentNames = new HashSet<string>(indexMap.Keys, StringComparer.OrdinalIgnoreCase);

        CheckDuplicates(steps, issues);

        foreach (var step in steps)
        {
            var rules = step.Rules;
            var myIndex = indexMap[step.Name];

            CheckOptimalPosition(step, rules, myIndex, steps.Count, indexMap, issues);
            CheckPreferAfter(step, rules, myIndex, indexMap, issues);
            CheckPreferBefore(step, rules, myIndex, indexMap, issues);
            CheckRequiresBefore(step, rules, myIndex, indexMap, presentNames, issues);
            CheckRequiresAfter(step, rules, myIndex, indexMap, presentNames, issues);
            CheckRecommendPresent(step, rules, presentNames, issues);
            CheckIncompatibleWith(step, rules, presentNames, issues);
        }
    }

    private static void CheckDuplicates(IReadOnlyList<PipelineStep> steps, List<PipelineValidationIssue> issues)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            if (!seen.Add(step.Name))
            {
                issues.Add(new PipelineValidationIssue("error",
                    $"Duplicate step: {step.DisplayName}. Each step should appear at most once.",
                    [step.Name]));
            }
        }
    }

    private static void CheckOptimalPosition(
        PipelineStep step, PipelineStepRules rules, int myIndex, int totalCount,
        IReadOnlyDictionary<string, int> indexMap, List<PipelineValidationIssue> issues)
    {
        if (rules.OptimalPosition is null) return;

        foreach (var constraint in rules.OptimalPosition)
        {
            if (string.Equals(constraint, "first", StringComparison.OrdinalIgnoreCase))
            {
                if (myIndex != 0)
                {
                    issues.Add(new PipelineValidationIssue("error",
                        WithInfo(rules, "optimalPosition",
                            $"{step.DisplayName} must be at position 1 (first in the pipeline)."),
                        [step.Name]));
                }
            }
            else if (string.Equals(constraint, "last", StringComparison.OrdinalIgnoreCase))
            {
                if (myIndex != totalCount - 1)
                {
                    issues.Add(new PipelineValidationIssue("error",
                        WithInfo(rules, "optimalPosition",
                            $"{step.DisplayName} must be the last step - steps after it will never receive events."),
                        [step.Name]));
                }
            }
            else if (constraint.StartsWith("before:", StringComparison.OrdinalIgnoreCase))
            {
                var target = constraint[7..];
                if (indexMap.TryGetValue(target, out var targetIdx) && myIndex >= targetIdx)
                {
                    issues.Add(new PipelineValidationIssue("error",
                        WithInfo(rules, "optimalPosition",
                            $"{step.DisplayName} must appear before {PipelineStep.FromName(target)?.DisplayName ?? target}."),
                        [step.Name, target]));
                }
            }
            else if (constraint.StartsWith("after:", StringComparison.OrdinalIgnoreCase))
            {
                var target = constraint[6..];
                if (indexMap.TryGetValue(target, out var targetIdx) && myIndex <= targetIdx)
                {
                    issues.Add(new PipelineValidationIssue("error",
                        WithInfo(rules, "optimalPosition",
                            $"{step.DisplayName} must appear after {PipelineStep.FromName(target)?.DisplayName ?? target}."),
                        [step.Name, target]));
                }
            }
        }
    }

    private static void CheckPreferAfter(
        PipelineStep step, PipelineStepRules rules, int myIndex,
        IReadOnlyDictionary<string, int> indexMap, List<PipelineValidationIssue> issues)
    {
        if (rules.PreferAfter is null) return;
        foreach (var after in rules.PreferAfter)
        {
            if (indexMap.TryGetValue(after, out var afterIdx) && myIndex < afterIdx)
            {
                issues.Add(new PipelineValidationIssue("warn",
                    WithInfo(rules, "preferAfter",
                        $"{step.DisplayName} works best after {PipelineStep.FromName(after)?.DisplayName ?? after}."),
                    [step.Name, after]));
            }
        }
    }

    private static void CheckPreferBefore(
        PipelineStep step, PipelineStepRules rules, int myIndex,
        IReadOnlyDictionary<string, int> indexMap, List<PipelineValidationIssue> issues)
    {
        if (rules.PreferBefore is null) return;
        foreach (var before in rules.PreferBefore)
        {
            if (indexMap.TryGetValue(before, out var beforeIdx) && myIndex > beforeIdx)
            {
                issues.Add(new PipelineValidationIssue("warn",
                    WithInfo(rules, "preferBefore",
                        $"{step.DisplayName} works best before {PipelineStep.FromName(before)?.DisplayName ?? before}."),
                    [step.Name, before]));
            }
        }
    }

    private static void CheckRequiresBefore(
        PipelineStep step, PipelineStepRules rules, int myIndex,
        IReadOnlyDictionary<string, int> indexMap, HashSet<string> presentNames,
        List<PipelineValidationIssue> issues)
    {
        if (rules.RequiresBefore is null) return;
        foreach (var req in rules.RequiresBefore)
        {
            if (indexMap.TryGetValue(req, out var reqIdx) && reqIdx < myIndex) continue;

            var reqName = PipelineStep.FromName(req)?.DisplayName ?? req;
            if (!presentNames.Contains(req))
            {
                issues.Add(new PipelineValidationIssue("warn",
                    WithInfo(rules, "requiresBefore",
                        $"{step.DisplayName} expects {reqName} to be in the pipeline before it. Consider adding {reqName}."),
                    [step.Name]));
            }
            else
            {
                issues.Add(new PipelineValidationIssue("warn",
                    WithInfo(rules, "requiresBefore",
                        $"{step.DisplayName} requires {reqName} to appear before it in the pipeline."),
                    [step.Name, req]));
            }
        }
    }

    private static void CheckRequiresAfter(
        PipelineStep step, PipelineStepRules rules, int myIndex,
        IReadOnlyDictionary<string, int> indexMap, HashSet<string> presentNames,
        List<PipelineValidationIssue> issues)
    {
        if (rules.RequiresAfter is null) return;
        foreach (var req in rules.RequiresAfter)
        {
            if (indexMap.TryGetValue(req, out var reqIdx) && reqIdx > myIndex) continue;

            var reqName = PipelineStep.FromName(req)?.DisplayName ?? req;
            if (!presentNames.Contains(req))
            {
                issues.Add(new PipelineValidationIssue("warn",
                    WithInfo(rules, "requiresAfter",
                        $"{step.DisplayName} expects {reqName} to be in the pipeline after it. Consider adding {reqName}."),
                    [step.Name]));
            }
            else
            {
                issues.Add(new PipelineValidationIssue("warn",
                    WithInfo(rules, "requiresAfter",
                        $"{step.DisplayName} requires {reqName} to appear after it in the pipeline."),
                    [step.Name, req]));
            }
        }
    }

    private static void CheckRecommendPresent(
        PipelineStep step, PipelineStepRules rules, HashSet<string> presentNames,
        List<PipelineValidationIssue> issues)
    {
        if (rules.RecommendPresent is null) return;
        foreach (var rec in rules.RecommendPresent)
        {
            if (presentNames.Contains(rec)) continue;
            issues.Add(new PipelineValidationIssue("info",
                WithInfo(rules, "recommendPresent",
                    $"{step.DisplayName} works best with {PipelineStep.FromName(rec)?.DisplayName ?? rec}. Consider adding it to the pipeline."),
                [step.Name]));
        }
    }

    private static void CheckIncompatibleWith(
        PipelineStep step, PipelineStepRules rules, HashSet<string> presentNames,
        List<PipelineValidationIssue> issues)
    {
        if (rules.IncompatibleWith is null) return;
        foreach (var incompat in rules.IncompatibleWith)
        {
            if (!presentNames.Contains(incompat)) continue;
            // Use WithInfo here too — the pre-refactor ValidateDetailed
            // skipped WithInfo for this rule type while ValidateStepNames
            // applied it; collapsing to one walker fixes the drift in
            // ValidateStepNames' favour (strictly more informative).
            issues.Add(new PipelineValidationIssue("error",
                WithInfo(rules, "incompatibleWith",
                    $"{step.DisplayName} is incompatible with {PipelineStep.FromName(incompat)?.DisplayName ?? incompat}. Remove one of them."),
                [step.Name, incompat]));
        }
    }

    private static string WithInfo(PipelineStepRules rules, string ruleType, string baseMessage)
    {
        var info = rules.GetMoreInfo(ruleType);
        return string.IsNullOrEmpty(info) ? baseMessage : $"{baseMessage} {info}";
    }

    // -- Strategy-name round-trip helpers (for JSON config) --

    /// <summary>
    /// Conventional name of this strategy by structural comparison against
    /// the built-in presets. Returns "default" / "minimal" / "filterEarly"
    /// when the ordered step list matches one of those presets exactly,
    /// otherwise "custom". The builder writes this into
    /// <c>JsonLoggingConfig.PipelineStrategyName</c> so a Reload can
    /// reconstruct the same selection rather than collapse everything to
    /// "custom from steps array."
    /// </summary>
    public string ResolveName()
    {
        if (StepsMatch(Default().Steps)) return PresetNames.Default;
        if (StepsMatch(Minimal().Steps)) return PresetNames.Minimal;
        if (StepsMatch(FilterEarly().Steps)) return PresetNames.FilterEarly;
        return PresetNames.Custom;
    }

    /// <summary>
    /// Reconstruct a strategy from a JSON-supplied <paramref name="name"/>
    /// and ordered <paramref name="stepNames"/>.
    ///
    /// Precedence: an explicit non-empty <paramref name="stepNames"/> array
    /// always wins and is round-tripped through <see cref="FromNames"/>.
    /// This preserves the persisted shape exactly and surfaces plugin-supplied
    /// step names as a loud "register the plugin via WithPlugin&lt;TPlugin&gt;()"
    /// error rather than silently collapsing into a preset whose shape no
    /// longer matches.
    ///
    /// Only when <paramref name="stepNames"/> is null or empty does
    /// <paramref name="name"/> dispatch to a built-in preset factory.
    /// A null or empty <paramref name="name"/> returns null so the caller
    /// can keep the existing "factory uses Default()" behaviour.
    /// </summary>
    public static PipelineStrategy? Resolve(string? name, IReadOnlyList<string>? stepNames)
    {
        // Explicit steps array wins — preserves persisted shape and surfaces
        // missing-plugin errors at the right place rather than silently
        // degrading a Pro config into the Apache Default.
        if (stepNames is { Count: > 0 })
            return FromNames(stepNames);

        if (string.IsNullOrWhiteSpace(name)) return null;

        return name.Trim() switch
        {
            var s when string.Equals(s, PresetNames.Default, StringComparison.OrdinalIgnoreCase) => Default(),
            var s when string.Equals(s, PresetNames.Minimal, StringComparison.OrdinalIgnoreCase) => Minimal(),
            var s when string.Equals(s, PresetNames.FilterEarly, StringComparison.OrdinalIgnoreCase) => FilterEarly(),
            _ => null,
        };
    }

    /// <summary>Canonical preset names emitted into JSON.</summary>
    public static class PresetNames
    {
        public const string Default = "default";
        public const string Minimal = "minimal";
        public const string FilterEarly = "filterEarly";
        public const string Custom = "custom";
    }

    private bool StepsMatch(IReadOnlyList<PipelineStep> other)
    {
        var mine = Steps;
        if (mine.Count != other.Count) return false;
        for (var i = 0; i < mine.Count; i++)
        {
            if (mine[i] != other[i]) return false;
        }
        return true;
    }

    // -- Parsing from string list (for JSON config) --

    /// <summary>
    /// Parse a strategy from a list of step names (case-insensitive).
    /// Used by JSON configuration deserialization.
    /// </summary>
    public static PipelineStrategy FromNames(IReadOnlyList<string> stepNames)
    {
        ArgumentNullException.ThrowIfNull(stepNames);
        var strategy = Create();

        foreach (var name in stepNames)
        {
            var step = PipelineStep.FromName(name)
                ?? throw new ArgumentException(
                    $"Unknown pipeline step: '{name}'. " +
                    $"Known steps: {string.Join(", ", PipelineStep.AllNames)}. " +
                    $"If '{name}' is a plugin-supplied step (e.g., circuitBreaker, retry, " +
                    $"durableBuffer, fallback, audit), register the plugin via the builder's " +
                    $"WithPlugin<TPlugin>() before deserializing this config so the plugin's " +
                    $"pipeline steps are known.");
            strategy._steps.Add(step);
        }

        return strategy;
    }

    private static int IndexOf(IReadOnlyList<PipelineStep> steps, PipelineStep target)
    {
        for (var i = 0; i < steps.Count; i++)
            if (steps[i] == target) return i;
        return -1;
    }
}

/// <summary>
/// A named step in the pipeline strategy. Each step corresponds to one decorator type.
/// </summary>
/// <summary>
/// Declarative placement rules for a pipeline step.
/// The validator checks these against the actual step order and reports issues.
/// Vendors creating custom steps declare rules to guide users toward optimal placement.
/// </summary>
/// <summary>
/// Declarative placement rules for a pipeline step.
/// The validator checks these against the actual step order and reports issues.
///
/// OptimalPosition: hard constraints (error-level, blocks commit).
///   Supported sub-rules: "first", "last", "before:stepName", "after:stepName"
///   Example: ["first"] means this step MUST be at position 1 or it's an error.
///   Example: ["after:async", "before:fanOut"] means it must be between those two.
///
/// All other fields are soft constraints (warn/info level, allow commit).
/// </summary>
/// <summary>
/// Declarative placement rules for a pipeline step.
///
/// OptimalPosition: hard constraints (error-level, blocks commit).
///   Sub-rules: "first", "last", "before:stepName", "after:stepName"
///
/// MoreInfo: keyed explanatory text the validator appends to issue messages.
///   Keys match rule types: "optimalPosition", "preferBefore", "preferAfter",
///   "requiresBefore", "requiresAfter", "recommendPresent", "incompatibleWith".
///   The validator appends the matching text so users understand WHY.
/// </summary>
public sealed record PipelineStepRules(
    string[]? OptimalPosition = null,
    string[]? PreferBefore = null,
    string[]? PreferAfter = null,
    string[]? RequiresBefore = null,
    string[]? RequiresAfter = null,
    string[]? RecommendPresent = null,
    string[]? IncompatibleWith = null,
    IReadOnlyDictionary<string, string>? MoreInfo = null)
{
    public static PipelineStepRules Default { get; } = new();

    /// <summary>Get explanatory text for a rule type, or empty string.</summary>
    public string GetMoreInfo(string ruleType)
    {
        if (MoreInfo is null) return "";
        return MoreInfo.TryGetValue(ruleType, out var text) ? text : "";
    }
}

public sealed class PipelineStep
{
    public string Name { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string Help { get; }
    public Pipeline.VendorInfo Vendor { get; }
    public PipelineStepRules Rules { get; }
    /// <summary>
    /// Minimum edition required to use this step. Defaults to
    /// <see cref="HeraldEdition.Community"/> so all built-in steps remain
    /// available everywhere. Paid steps registered by plugin assemblies pass
    /// a higher tier through <see cref="Register"/> so the dashboard can
    /// render an edition badge and the host can refuse to assemble a
    /// pipeline that names a step above the running tier.
    /// </summary>
    public HeraldEdition MinimumEdition { get; }

    /// <summary>
    /// Capabilities required to use this step. Sibling to
    /// <see cref="MinimumEdition"/>. Defaults to empty so existing steps
    /// remain available everywhere. Paid steps registered by plugin
    /// assemblies declare their caps via <see cref="Register"/> so the
    /// host can refuse to assemble a pipeline that names a step whose
    /// capabilities are missing from the current effective cap-set.
    ///
    /// <para>
    /// <b>Cap monotonicity (security).</b> Once a step has a non-empty
    /// capability set, re-registration's new cap-set must be a SUPERSET
    /// of the prior cap-set. New caps may be added; existing caps may
    /// not be removed. <b>Carve-out:</b> when the prior registration was
    /// edition-only (empty <c>RequiredCapabilities</c>), a re-registration
    /// may declare any cap-set — there is no prior cap-set to be a
    /// superset of. Without the carve-out, every legacy step would be
    /// permanently barred from adopting caps post-deprecation.
    /// </para>
    /// </summary>
    public IReadOnlyList<CapabilityRequirement> RequiredCapabilities { get; }

    /// <summary>
    /// Visual link type for dashboard puzzle-piece connectors.
    /// Start = peg only (bottom), End = hole only (top), Middle = both.
    /// Derived from OptimalPosition rules.
    /// </summary>
    public string LinkType =>
        Rules.OptimalPosition?.Contains("first") == true ? "start" :
        Rules.OptimalPosition?.Contains("last") == true ? "end" :
        "middle";

    /// <summary>
    /// A structural step is part of the strategy's fixed skeleton — the pipeline
    /// compiler positions it (entry point or terminal fan-out), not the operator.
    /// Derived from the same OptimalPosition rules that drive LinkType, so the two
    /// can never disagree. Structural steps are excluded from the components
    /// catalog's ADDABLE list but still compute as `linked` when present in
    /// bootstrap.loggers[] (the default chain renders them as fixed markers).
    /// </summary>
    public bool Structural =>
        Rules.OptimalPosition?.Contains("first") == true ||
        Rules.OptimalPosition?.Contains("last") == true;

    private PipelineStep(string name, string displayName = "", string description = "", string help = "", Pipeline.VendorInfo? vendor = null, PipelineStepRules? rules = null, HeraldEdition? minimumEdition = null, IReadOnlyList<CapabilityRequirement>? requiredCapabilities = null)
    {
        Name = name;
        DisplayName = string.IsNullOrEmpty(displayName) ? name : displayName;
        Description = description;
        Help = help;
        Vendor = vendor ?? Pipeline.VendorInfo.MMP;
        Rules = rules ?? PipelineStepRules.Default;
        MinimumEdition = minimumEdition ?? HeraldEdition.Community;
        RequiredCapabilities = requiredCapabilities ?? [];
    }

    // Entry point steps — rules owned by the class, referenced here
    // Swappable has no separate JSON config record. Its only knob (debounceMs)
    // lives on JsonHotReloadConfig and is serialized by SwappableStepConfigSerializer.
    public static PipelineStep Swappable { get; } = new("swappable",
        "Swap Config Live", "Change the pipeline while it runs. No restart needed",
        "The HotReload entry point wraps the entire pipeline in a swappable container. When you change the pipeline configuration at runtime (e.g. via the management API or a config file reload), the old pipeline is atomically replaced with the new one. In-flight events complete on the old pipeline; new events go to the new one. Without this step, configuration changes require a full application restart.",
        rules: Pipeline.SwappableLogger.StepRules);

    public static PipelineStep HotPath { get; } = new("hotPath",
        "Fastest Path",
        "Logs pre-formatted text with nothing extra. Built for tight game loops",
        "The HotPath entry point skips template parsing, enrichment, scoped context, caller info capture, and property resolution. Takes pre-formatted strings only. All methods [AggressiveInlining]. Use for game loop inner ticks where sub-100ns matters. Inherits the pipeline's minimum level for zero-alloc rejection.",
        rules: Addons.GamePerformance.HotPathLogger.StepRules);

    // Pipeline decorators — rules owned by each class.
    // The 5 remaining plugin-supplied steps (CircuitBreaker, Retry,
    // DurableBuffer, Fallback, Audit) are registered by their plugin
    // assemblies via PipelineStep.Register(...) so Apache Core has no
    // compile-time reference to those decorator types.
    public static PipelineStep Async { get; } = new("async",
        "Log in the Background", "Hand events to a background worker so your code doesn't wait on logging",
        "The Async Queue decouples the calling thread from log processing. Events are placed into a bounded queue and processed by a background worker.",
        rules: Pipeline.AsyncLogger.StepRules);

    public static PipelineStep Rendering { get; } = new("rendering",
        "Build the Message Text", "Fill in the {placeholders} to make the final log line",
        "Resolves {placeholder} tokens on the worker thread. Place after Async for best performance.",
        rules: Pipeline.RenderingLogger.StepRules);

    public static PipelineStep Batching { get; } = new("batching",
        "Send in Groups", "Collect events and send them together. Fewer trips, less overhead",
        "Collects events and flushes by size or time threshold. Reduces per-event overhead for network sinks.",
        rules: Pipeline.BatchingLogger.StepRules);

    public static PipelineStep Filtering { get; } = new("filtering",
        "Drop What You Don't Want", "Keep events at or above a level. Drop the rest before they go further",
        "Evaluates events against level/category filters. Position matters: before Async (FilterEarly) saves queue; after Async (Default) enables flight recorder.",
        rules: Filters.FilteringLogger.StepRules);

    public static PipelineStep PostFiltering { get; } = new("postFiltering",
        "Filter on Message Text", "A second filter that reads the finished message. Drop events by what they say",
        "Filters based on rendered message content. Place between Rendering and FanOut.",
        rules: Pipeline.PostFilteringBatchLogger.StepRules);

    public static PipelineStep EventProcessing { get; } = new("eventProcessing",
        "Reshape Each Event", "Add, redact, or change fields on every event before it's delivered",
        "Applies ILogEventProcessor chain: redaction, schema validation, metrics extraction. Runs once per event.",
        rules: Pipeline.EventProcessingLogger.StepRules);

    public static PipelineStep FlightRecorder { get; } = new("flightRecorder",
        "Keep the Last Few for Crashes", "Hold recent low-level events in memory and dump them when an error hits",
        "Ring buffer for below-minimum events. On error trigger, flushes buffer before the error. MUST be before Filtering.",
        rules: Addons.GamePerformance.FlightRecorderLogger.StepRules);

    public static PipelineStep FanOut { get; } = new("fanOut",
        "Deliver to Every Destination", "The last step. Sends each surviving event to all your destinations at once",
        "The last step in every pipeline. Takes each event that passed the filters and sends it to every destination you set up (Console, HTTP, File, Database, and so on). One destination can fail without stopping the others. They still get the event. Herald adds this step automatically if you leave it out. This is where the pipeline ends and the actual log output begins.",
        rules: Pipeline.SafeCompositeLogger.StepRules);

    public static PipelineStep WindowedMean { get; } = new("windowedMean",
        "Summarize Noisy Numbers", "Roll a flood of number events into one summary per time window",
        "Groups events by category and rule into time-based windows. When a window closes, it emits one summary event carrying the count, mean, min, and max of the number being tracked. The originals are folded in, not forwarded. Use it for high-frequency number events where the trend matters but each value doesn't, like request latency per endpoint per second. Thread-safe with per-window locks, and light enough for 60+ Hz tight loops. Each summary copies its origin from the last event in the window so downstream destinations accept it the same way.");

    public static PipelineStep FrameBudget { get; } = new("frameBudget",
        "Cap Logging per Frame", "Stop logging once it has used its time slice this frame. Protects your frame rate",
        "Tracks how much time logging has used inside each frame or tick. Once it passes the budget, the rest of that frame's events are dropped so logging never steals your frame rate. Call ResetBudget() at the start of each frame or tick to reset the counter. Dropped events can route to a separate destination for later review. Built for game loops, simulation ticks, and any real-time system where logging must stay inside a fixed slice of the frame.");

    public static PipelineStep MetricsCapturing { get; } = new("metricsCapturing",
        "Measure Delivery", "Time how long each event takes to deliver and count successes and failures",
        "Wraps a destination to measure how long delivery takes through the whole chain around it. It records timing on every event, whether delivery succeeds or fails. It stays out of the way. It re-throws on failure, so retry, circuit-breaker, and audit steps downstream still behave normally. Use it for dashboards that track destination health, p99 delivery latency, and error rates without changing the destination itself.");

    public static PipelineStep RenderedOutput { get; } = new("renderedOutput",
        "Format for Text Output", "Turn events into formatted text and write them to console, debug, or trace output",
        "Connects the pipeline to outputs that expect ready-made text. It applies a layout (timestamp format, level label, property order) and then writes through a host target like the console, debug output, or a trace listener. It implements IKernelSink, so a pipeline with a console destination can take the fast direct path and skip the longer chain for events that qualify. This is the adapter behind WithConsoleSink() and similar text-output builder methods.");

    // ── Filter templates — composable ILogFilter implementations. ──
    // Each filter lives inside a FilteringLogger's filter list. The operator
    // adds them from the left panel and removes them from the FilteringLogger's
    // nested filter view. Registered with "filter." prefix so the components
    // catalog can distinguish them from logger steps.

    public static PipelineStep FilterLevel { get; } = new("filter.level",
        "Keep at or Above a Level", "Let through events at a level or higher. The simplest gate there is",
        "Checks each event's level against a fixed minimum, using your pipeline's level order. Anything below the line is dropped. This is the simplest filter: one level, one check, no allocations. Use it when you want a fixed floor that doesn't change while the app runs. To change the level at runtime or set per-category floors, use the Adjustable Level Gate instead.");

    public static PipelineStep FilterSwitchableLevel { get; } = new("filter.switchableLevel",
        "Adjustable Level Gate", "Change the level floor while the app runs, with per-topic and per-trace overrides",
        "Reads a live level setting on every event, so you can raise or lower the floor without restarting the app. You can override per topic (say, 'health-check' at Error while everything else stays at Debug) and per trace (say, one traceId at Verbose for focused debugging). When more than one applies, the trace override wins, then the topic, then the global setting. This is the go-to filter for production pipelines you need to tune live.");

    public static PipelineStep FilterSampling { get; } = new("filter.sampling",
        "Keep 1 in N", "Pass every Nth event and drop the rest. Cut volume without losing the picture",
        "Counts events and lets every Nth one through, dropping the rest. It's lock-free and safe on game-loop hot paths. You can limit it to events that match a rule; everything else passes untouched. Use it to thin out volume evenly. To keep every event in a related trace together, use Keep Whole Traces instead.");

    public static PipelineStep FilterConsistentSampling { get; } = new("filter.consistentSampling",
        "Keep Whole Traces", "Keep or drop every event in a trace together. Never half a trace",
        "Decides keep-or-drop from a key like correlationId, traceId, or requestId, so the same trace always lands the same way. Every event sharing that key is kept together or dropped together. You never get a half trace. The decision is stable across restarts and across servers, so a distributed system samples the same traces everywhere. Use it when a complete trace matters more than cutting volume evenly.");

    public static PipelineStep FilterCompositeSampling { get; } = new("filter.compositeSampling",
        "Different Rates by Rule", "A list of rules, each with its own keep rate. The first match wins",
        "Works down an ordered list of rules. Each rule says what to match (topic, level, a property value) and how often to keep it (every Nth, or a rate over time). The first rule that matches an event decides its fate; events that match no rule pass through untouched. Use it when different topics need different rates: health-checks at 1-in-100, auth events at 1-in-10, and errors always kept.");

    public static PipelineStep FilterThrottling { get; } = new("filter.throttling",
        "Cap the Rate", "Allow at most N events per time window, then drop until the window resets",
        "Caps how many events pass in each time window. Once you hit the cap, the rest of that window is dropped. It's lock-free and thread-safe. You can limit it to events that match a rule; everything else passes through. Use it when you need a hard ceiling no matter how big the burst: at most 100 error events per minute, so an outage doesn't flood your alerts.");

    public static PipelineStep FilterConditionalBatch { get; } = new("filter.conditionalBatch",
        "Full Detail Around Errors", "Keep everything in a batch once one event trips a trigger. Otherwise stay lean",
        "Looks at a whole batch at once. It scans the batch for any event that trips a trigger (say, Error or above). If one does, it keeps the entire batch, so you get full context around the problem. If nothing trips it, it keeps only the important events. The idea comes from NLog's PostFilteringTargetWrapper. Use it when you want lean output most of the time but full detail the moment something goes wrong.");

    public static PipelineStep FilterExpression { get; } = new("filter.expression",
        "Filter by Written Rule", "Write a plain rule like 'category:auth AND level:warn' and filter on it",
        "Takes a rule written as text in Herald's query language and turns it into a filter. The rule is read once when you set it, then runs on each event with no allocation on the hot path. You can compare fields (topic, level, property values), combine them with AND, OR, and NOT, and use wildcards. Use it when you want people to write filter rules as readable text instead of code. It's a good fit for the filter editor in the viewer.");

    public static PipelineStep FilterPredicate { get; } = new("filter.predicate",
        "Reuse a Query Rule", "Drop a rule from the query engine straight into a filter slot",
        "An adapter that lets a query-engine rule (ILogPredicate) act as a pipeline filter (ILogFilter). It just calls the rule's Evaluate method. Use it when you already have a rule from the query system and want to slot it into a filter list.");

    public static PipelineStep FilterCompiledPredicate { get; } = new("filter.compiledPredicate",
        "Fast Compiled Rule", "A filter built from a rule ahead of time. No parsing per event, just speed",
        "Wraps a rule (ILogPredicate) that was compiled from a query expression at startup. It runs as plain .NET method calls, with no parsing or expression-tree walking per event. Use it when filter speed matters and the rule won't change while the app runs. A common pattern: write the rule with Filter by Written Rule during development because it's readable, then switch to this compiled form in production because it's fast.");

    public static PipelineStep FilterFunc { get; } = new("filter.func",
        "Filter with a Lambda", "Pass a small Func<LogEvent, bool> in code. The quickest custom filter",
        "Turns any Func<LogEvent, bool> into a filter. Plain delegate call: no reflection, no compilation, AOT-clean. Return true to keep the event, false to drop it. Use it for quick one-off filters in code: WithFilter(e => e.Category.Value != \"health-check\"). For rules people manage from the viewer, use Filter by Written Rule instead.");

    public override string ToString() => Name;
    public override bool Equals(object? obj) => obj is PipelineStep other && Name == other.Name;
    public override int GetHashCode() => Name.GetHashCode();
    public static bool operator ==(PipelineStep? a, PipelineStep? b) => ReferenceEquals(a, b) || (a is not null && a.Equals(b));
    public static bool operator !=(PipelineStep? a, PipelineStep? b) => !(a == b);

    // ConcurrentDictionary so plugin [ModuleInitializer] writes are safe against
    // concurrent FromName / AllNames reads. Apache Core seeds only the
    // community-tier steps; Pro/Enterprise plugins call Register(...) to add
    // their steps on assembly load.
    private static readonly ConcurrentDictionary<string, PipelineStep> _byName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["swappable"] = Swappable,
        ["hotPath"] = HotPath,
        ["async"] = Async,
        ["rendering"] = Rendering,
        ["batching"] = Batching,
        ["filtering"] = Filtering,
        ["postFiltering"] = PostFiltering,
        ["eventProcessing"] = EventProcessing,
        ["flightRecorder"] = FlightRecorder,
        ["fanOut"] = FanOut,
        ["windowedMean"] = WindowedMean,
        ["frameBudget"] = FrameBudget,
        ["metricsCapturing"] = MetricsCapturing,
        ["renderedOutput"] = RenderedOutput,
        // ── Filter templates ──
        ["filter.level"] = FilterLevel,
        ["filter.switchableLevel"] = FilterSwitchableLevel,
        ["filter.sampling"] = FilterSampling,
        ["filter.consistentSampling"] = FilterConsistentSampling,
        ["filter.compositeSampling"] = FilterCompositeSampling,
        ["filter.throttling"] = FilterThrottling,
        ["filter.conditionalBatch"] = FilterConditionalBatch,
        ["filter.expression"] = FilterExpression,
        ["filter.predicate"] = FilterPredicate,
        ["filter.compiledPredicate"] = FilterCompiledPredicate,
        ["filter.func"] = FilterFunc,
    };

    public static PipelineStep? FromName(string name) =>
        _byName.TryGetValue(name, out var step) ? step : null;

    /// <summary>
    /// Register a custom pipeline step for plugin decorators.
    /// Returns the step for fluent use. If a step with the same name exists,
    /// non-empty metadata in the call merges over the existing entry — this
    /// allows plugins to enrich step metadata when re-registered.
    ///
    /// <para>
    /// <b>Tier monotonicity (security).</b> A step's <see cref="MinimumEdition"/>
    /// can only be RAISED via re-registration, never lowered. This blocks a
    /// downgrade attack where third-party code calls
    /// <c>Register("circuitBreaker", minimumEdition: Community)</c> after a
    /// plugin has registered the same step with a Pro tier, in order to
    /// slip a paid step past the host's edition gate. A re-registration
    /// with a HIGHER tier (e.g., Community step legitimately promoted to
    /// Pro by a plugin) is allowed.
    /// </para>
    ///
    /// <para>
    /// <b>Vendor monotonicity (security).</b> Once a step's <see cref="Vendor"/>
    /// is set to anything other than <see cref="Pipeline.VendorInfo.ThirdParty"/>,
    /// later re-registrations cannot replace the vendor. Prevents third-party
    /// code from claiming ownership of a registered MMP step by re-registering
    /// with a different vendor name. The first non-ThirdParty registrant
    /// owns the step.
    /// </para>
    ///
    /// <para>
    /// <b>Cap monotonicity (security).</b> Once a step has a non-empty
    /// <see cref="RequiredCapabilities"/> set, re-registration's new
    /// cap-set must be a SUPERSET of the prior. New caps may be added;
    /// existing caps may not be removed. Closes the symmetric downgrade-
    /// bypass surface — third-party code cannot re-register a paid step
    /// with a narrower cap-set to slip past the host's capability gate.
    /// <b>Carve-out:</b> when the prior registration was edition-only
    /// (empty <see cref="RequiredCapabilities"/>), any new cap-set is
    /// accepted; without the carve-out, every legacy step would be
    /// permanently barred from adopting caps post-deprecation.
    /// </para>
    ///
    /// <para>
    /// Thread-safe: uses ConcurrentDictionary.AddOrUpdate so two plugins'
    /// module initializers racing on the same name produce a deterministic
    /// merged result rather than a torn dictionary.
    /// </para>
    /// </summary>
    public static PipelineStep Register(string name, string displayName = "", string description = "", string help = "", Pipeline.VendorInfo? vendor = null, PipelineStepRules? rules = null, HeraldEdition? minimumEdition = null, IReadOnlyList<CapabilityRequirement>? requiredCapabilities = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return _byName.AddOrUpdate(name,
            addValueFactory: key => new PipelineStep(
                key, displayName, description, help,
                vendor ?? Pipeline.VendorInfo.ThirdParty,
                rules, minimumEdition, requiredCapabilities),
            updateValueFactory: (key, existing) =>
            {
                if (string.IsNullOrEmpty(displayName) && string.IsNullOrEmpty(description) &&
                    string.IsNullOrEmpty(help) && vendor is null && rules is null && minimumEdition is null && requiredCapabilities is null)
                    return existing;

                // Tier is monotonic: only allow raising the bar, never lowering.
                // Closes the downgrade-bypass surface where a caller would
                // re-register a paid step with MinEdition=Community to skip
                // the host's edition gate.
                var mergedMinEdition = existing.MinimumEdition;
                if (minimumEdition is not null && minimumEdition.Rank > existing.MinimumEdition.Rank)
                {
                    mergedMinEdition = minimumEdition;
                }

                // Cap monotonicity: the new cap-set must be a superset of the
                // prior. Carve-out when prior was edition-only (empty caps).
                // Mirrors the edition-tier rule symmetrically — closes the
                // capability-narrowing downgrade-bypass surface.
                var mergedCaps = existing.RequiredCapabilities;
                if (requiredCapabilities is not null)
                {
                    if (existing.RequiredCapabilities.Count == 0)
                    {
                        // Carve-out: legacy edition-only prior accepts any
                        // new cap-set (the "first adopt caps" transition).
                        mergedCaps = requiredCapabilities;
                    }
                    else
                    {
                        var priorNames = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var prior in existing.RequiredCapabilities)
                            priorNames.Add(prior.Name);
                        var nextNames = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var next in requiredCapabilities)
                            nextNames.Add(next.Name);

                        if (!priorNames.IsSubsetOf(nextNames))
                        {
                            // Find the first removed cap so the message names
                            // a concrete missing element — gives the operator
                            // an actionable grep target.
                            string? droppedCap = null;
                            foreach (var prior in existing.RequiredCapabilities)
                            {
                                if (!nextNames.Contains(prior.Name))
                                {
                                    droppedCap = prior.Name;
                                    break;
                                }
                            }
                            throw new InvalidOperationException(
                                $"PipelineStep '{key}' re-registration violates capability monotonicity: " +
                                $"new RequiredCapabilities is not a superset of the prior set " +
                                $"(dropped capability: '{droppedCap}'). " +
                                $"Cap-sets may only be widened, never narrowed.");
                        }
                        mergedCaps = requiredCapabilities;
                    }
                }

                // Vendor is sticky: once a non-ThirdParty owner is recorded,
                // subsequent registrations cannot rename. Third-party stubs
                // (Custom() fallback) CAN be upgraded to an owned vendor.
                var mergedVendor = existing.Vendor;
                if (vendor is not null && existing.Vendor == Pipeline.VendorInfo.ThirdParty)
                {
                    mergedVendor = vendor;
                }

                return new PipelineStep(
                    key,
                    !string.IsNullOrEmpty(displayName) ? displayName : existing.DisplayName,
                    !string.IsNullOrEmpty(description) ? description : existing.Description,
                    !string.IsNullOrEmpty(help) ? help : existing.Help,
                    mergedVendor,
                    rules ?? existing.Rules,
                    mergedMinEdition,
                    mergedCaps);
            });
    }

    public static IEnumerable<string> AllNames => _byName.Keys;
}

/// <summary>
/// A validation issue with the pipeline strategy.
/// <para>
/// <c>Severity</c> is a validation-issue category ("info", "warn", "error") — not a Herald
/// level key. These strings are intentionally not swept by the Serilog rename; they describe
/// the importance of the validation finding, not a log event level.
/// </para>
/// </summary>
public sealed record PipelineValidationIssue(
    string Severity,
    string Message,
    IReadOnlyList<string> AffectedSteps);
