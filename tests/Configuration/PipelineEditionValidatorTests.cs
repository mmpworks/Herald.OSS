#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using MMP.Herald;
using MMP.Herald.Configuration;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pipeline;
using Xunit;

// Namespace uses "ConfigurationAudit" so it does not shadow
// MMP.Herald.Configuration for other tests in the assembly that use
// unqualified type references like `Configuration.PipelineStrategy`.
namespace MMP.Herald.Tests.ConfigurationAudit;

/// <summary>
/// Covers <see cref="PipelineEditionValidator"/>. Uses the internal
/// <c>ValidateAgainst</c> overload to drive the validator against any
/// edition without recompiling; that keeps the test independent of the
/// assembly's compile-time <see cref="HeraldEdition"/>.
///
/// <para>
/// The headline contract is composed failure — a single exception that
/// names every incompatibility together, so operators fix the whole
/// chain in one pass instead of one-at-a-time on each build.
/// </para>
/// </summary>
public sealed class PipelineEditionValidatorTests
{
    private static LogPipelinePolicy MakePolicy(
        PipelineStrategy? strategy = null,
        IReadOnlyList<ILogEventProcessor>? processors = null,
        IReadOnlyList<IConfigurablePipelineDecorator>? customDecorators = null) =>
        new(
            MinimumLevel: KnownLogLevels.Info,
            AsyncPolicy: AsyncLogPolicy.Disabled,
            BatchingPolicy: BatchingPolicy.Disabled,
            EventProcessors: processors,
            Strategy: strategy,
            CustomDecorators: customDecorators);

    [Fact]
    public void Default_strategy_passes_on_community()
    {
        var policy = MakePolicy(PipelineStrategy.Default());
        var act = () => PipelineEditionValidator.ValidateAgainst(policy, HeraldEdition.Community);
        act.Should().NotThrow("the default strategy is Community-compatible");
    }

    [Fact]
    public void Null_policy_throws_ArgumentNullException()
    {
        var act = () => PipelineEditionValidator.ValidateAgainst(null!, HeraldEdition.Community);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Pro_step_in_strategy_on_community_reports_the_mismatch()
    {
        // Register a Pro-tier test step inline so the test doesn't depend
        // on plugin assembly load ordering. Validator must catch any
        // step whose MinimumEdition exceeds the runtime edition.
        PipelineStep.Register("testProStep_singleMismatch", minimumEdition: HeraldEdition.Pro);

        var strategy = PipelineStrategy.Create()
            .Custom("testProStep_singleMismatch")
            .FanOut();
        var policy = MakePolicy(strategy);

        var act = () => PipelineEditionValidator.ValidateAgainst(policy, HeraldEdition.Community);
        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("testProStep_singleMismatch")
            .And.Contain("Pro")
            .And.Contain("Community");
    }

    [Fact]
    public void Multiple_mismatches_surface_in_one_composed_message()
    {
        // A Pro-tier step + an active Enterprise event processor on
        // Community — both must show up in the error. The value of the
        // validator is exactly this: one error, not a sequence of
        // per-component throws.
        PipelineStep.Register("testProStep_multiMismatch", minimumEdition: HeraldEdition.Pro);
        PipelineStep.Register("testEnterpriseStep_multiMismatch", minimumEdition: HeraldEdition.Enterprise);

        var strategy = PipelineStrategy.Create()
            .Custom("testProStep_multiMismatch")
            .Custom("testEnterpriseStep_multiMismatch")
            .FanOut();
        var processor = new FakeEnterpriseProcessor();
        var policy = MakePolicy(strategy, processors: new[] { (ILogEventProcessor)processor });

        var act = () => PipelineEditionValidator.ValidateAgainst(policy, HeraldEdition.Community);
        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("testProStep_multiMismatch");
        ex.Message.Should().Contain("testEnterpriseStep_multiMismatch");
        ex.Message.Should().Contain("fake-enterprise-processor");
    }

    [Fact]
    public void Dormant_flight_recorder_does_not_gate_community_build()
    {
        // FlightRecorder in the strategy is a placeholder — no decorator
        // fires unless FlightRecorderLogger is wired manually outside the
        // strategy. Listing the step in the strategy must not gate a
        // Community build by itself.
        var strategy = PipelineStrategy.Create()
            .FlightRecorder()
            .FanOut();
        var policy = MakePolicy(strategy);

        var act = () => PipelineEditionValidator.ValidateAgainst(policy, HeraldEdition.Community);
        act.Should().NotThrow();
    }

    [Fact]
    public void Dormant_event_processing_step_without_processors_passes()
    {
        // eventProcessing is Pro-gated but a no-op when there are no
        // processors. Listing it in the strategy without wiring a
        // processor must not gate a Community build.
        var strategy = PipelineStrategy.Create()
            .EventProcessing()
            .FanOut();
        var policy = MakePolicy(strategy);

        var act = () => PipelineEditionValidator.ValidateAgainst(policy, HeraldEdition.Community);
        act.Should().NotThrow();
    }

    [Fact]
    public void Default_strategy_with_no_processors_passes_on_community()
    {
        // PipelineStrategy.Default() lists flightRecorder and
        // eventProcessing. Both are dormant without the corresponding
        // policy fields populated, so the default strategy runs on
        // Community without the operator opting into anything higher.
        var policy = MakePolicy(PipelineStrategy.Default());
        var act = () => PipelineEditionValidator.ValidateAgainst(policy, HeraldEdition.Community);
        act.Should().NotThrow();
    }

    [Fact]
    public void Enterprise_build_allows_every_step()
    {
        // Running on Enterprise: everything passes regardless of the
        // step's minimum.
        var strategy = PipelineStrategy.Create()
            .HotPath()
            .FlightRecorder()
            .FanOut();
        var policy = MakePolicy(strategy);

        var act = () => PipelineEditionValidator.ValidateAgainst(policy, HeraldEdition.Enterprise);
        act.Should().NotThrow();
    }

    [Fact]
    public void Event_processor_with_IComponentMetadata_is_walked()
    {
        // A custom processor that self-reports an Enterprise minimum on
        // a Community build: the validator names it.
        var processor = new FakeEnterpriseProcessor();
        var policy = MakePolicy(processors: new[] { (ILogEventProcessor)processor });

        var act = () => PipelineEditionValidator.ValidateAgainst(policy, HeraldEdition.Community);
        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("fake-enterprise-processor");
    }

    [Fact]
    public void Event_processor_without_metadata_is_ignored()
    {
        // A processor that does not implement IComponentMetadata has no
        // declared edition — it is treated as Community-compatible.
        var processor = new PlainProcessor();
        var policy = MakePolicy(processors: new[] { (ILogEventProcessor)processor });

        var act = () => PipelineEditionValidator.ValidateAgainst(policy, HeraldEdition.Community);
        act.Should().NotThrow();
    }

    [Fact]
    public void Custom_decorator_with_IComponentMetadata_is_walked()
    {
        var decorator = new FakeEnterpriseDecorator();
        var policy = MakePolicy(customDecorators: new[] { (IConfigurablePipelineDecorator)decorator });

        var act = () => PipelineEditionValidator.ValidateAgainst(policy, HeraldEdition.Community);
        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("fake-enterprise-decorator");
    }

    [Fact]
    public void Empty_policy_passes()
    {
        // Nothing to walk (no strategy, no processors, no decorators)
        // means nothing to reject.
        var policy = MakePolicy();
        var act = () => PipelineEditionValidator.ValidateAgainst(policy, HeraldEdition.Community);
        act.Should().NotThrow();
    }

    // -- Test fixtures --

    private sealed class FakeEnterpriseProcessor : ILogEventProcessor, IComponentMetadata
    {
        public LogEvent? Process(LogEvent logEvent) => logEvent;
        public string ComponentName => "fake-enterprise-processor";
        public string DisplayName => "Fake Enterprise Processor";
        public string Description => "test fixture";
        public string Help => "";
        public VendorInfo Vendor => VendorInfo.MMP;
        public PipelineStepRules Rules => PipelineStepRules.Default;
        public HeraldEdition MinimumEdition => HeraldEdition.Enterprise;
        public System.Collections.Generic.IReadOnlyList<MMP.Herald.Routing.SinkConfigField> ConfigurationSchema => Array.Empty<MMP.Herald.Routing.SinkConfigField>();
    }

    private sealed class PlainProcessor : ILogEventProcessor
    {
        public LogEvent? Process(LogEvent logEvent) => logEvent;
    }

    private sealed class FakeEnterpriseDecorator : IConfigurablePipelineDecorator
    {
        public string StepName => "fake-enterprise-decorator";
        public ILogger CreateDecorator(ILogger inner, PipelineAccessor? accessor) => inner;

        // IComponentMetadata (the interface IConfigurablePipelineDecorator
        // inherits) requires more members than a test fixture usually
        // needs; the validator only reads MinimumEdition + ComponentName.
        public string DisplayName => "Fake Enterprise Decorator";
        public string Description => "test fixture";
        public PipelineStepRules Rules => PipelineStepRules.Default;
        public HeraldEdition MinimumEdition => HeraldEdition.Enterprise;
        public System.Collections.Generic.IReadOnlyList<MMP.Herald.Routing.SinkConfigField> ConfigurationSchema =>
            Array.Empty<MMP.Herald.Routing.SinkConfigField>();

        public IReadOnlyDictionary<string, object?> GetConfiguration() =>
            new Dictionary<string, object?>();
        public (bool Success, string? Error) ApplyConfiguration(IReadOnlyDictionary<string, object?> values) =>
            (true, null);
    }
}
