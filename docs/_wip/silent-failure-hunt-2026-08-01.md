# Silent-failure / load-order hunt -- Herald.OSS -- 2026-08-01

Scope: src/, native/dotnet/, skim of src/Quick/. Excludes test code and the
three files already fixed on `fix/sink-autoregistration-load-gap`
(`src/Routing/SinkAssemblyCatalog.cs`, `src/Routing/LogSinkProviderRegistry.cs`,
`native/dotnet/Routing/DefaultLogSinkFactory.cs`).

## BLOCKER

### 1. PipelineStrategy.Custom(string) + ApplyStep silently drops a named pipeline step -- no throw anywhere
- Files: src/Configuration/PipelineStrategy.cs:85-91 (Custom), native/dotnet/Pipeline/DefaultLogPipelineFactory.cs:437-466 (ApplyStep)
- Class: A (load-order / registration-dependent-on-assembly-load), compounded by B (silent skip, no diagnostic at all)
- Scenario: A consumer builds a strategy in code with PipelineStrategy.Create().Async().Custom("retry").FanOut() (or any Pro/Enterprise step name) instead of the plugin's own fluent extension method, and the Pro assembly's [ModuleInitializer] never fired (package referenced but never touched -- the same load-gap class as the sink bug). Custom() calls PipelineStep.FromName(stepName) ?? PipelineStep.Register(stepName) -- when FromName misses, it does not fail, it creates a brand-new bare PipelineStep on the spot with no rules, no vendor, no handler. Later, DefaultLogPipelineFactory.ApplyStep resolves the step against PipelineStepHandlerRegistry -- miss -- falls through to policy.CustomDecorators -- also miss (no WithPipelineDecorator call was ever needed to satisfy the compiler) -- and just returns. The step is silently omitted from the assembled pipeline. No exception, no stderr line, no failure-sink entry. A consumer who thinks they configured retry/circuit-breaker/audit protection ships with none, and nothing ever tells them.
- Contrast: the JSON path (PipelineStrategy.FromNames, PipelineStrategy.cs:549-568) throws an actionable ArgumentException naming the missing step and the WithPlugin<TPlugin>() remedy. The fluent/code path has no equivalent guard -- it is strictly worse than the JSON path for the identical mistake.
- Fix shape: Custom(string) should throw (mirroring FromNames) unless the step is already known via FromName, OR at minimum ApplyStep's final fallthrough (native/dotnet/Pipeline/DefaultLogPipelineFactory.cs:462, decorator is null -> return) should report through the failure sink / throw when a step made it into Steps but resolves to neither a handler nor a decorator -- "you asked for step X, nothing implements it" is exactly the sink bug's shape.

## HIGH

### 2. EntityKindRegistry.WarnOrphanedSections uses Debug.WriteLine -- compiled out entirely in Release builds
- File: src/Addons/ManagementApi/Entities/EntityKindRegistry.cs:137-146
- Class: B (silent degradation, no diagnostic in the environment that matters)
- Scenario: This is the exact validator the class-doc XML says exists to close the class of bug that hit PropertyStyles before E1 -- section serialized on save but no restore block, silently dropped on the next load. It fires Debug.WriteLine(...). System.Diagnostics.Debug.WriteLine carries [Conditional("DEBUG")]; call sites are elided by the compiler in any assembly built without the DEBUG symbol -- i.e. every Release/NuGet build of Herald.OSS's ManagementApi addon, which is what ships to consumers. In production the boot-time validation this method exists to provide never runs at all -- not "logs quietly," literally not present in the IL. An operator whose config carries an orphaned decorator/processor/channel section (explicitly called out in the surrounding comment as the intended trigger) gets zero signal that their data is being preserved-opaque instead of restored, in exactly the release artifact where it matters.
- Fix shape: route through the failure sink / a real logging call (or at minimum a Console.Error.WriteLine matching the pattern used in DefaultLogSinkFactory's sink-substitution WARN) instead of Debug.WriteLine.

## MEDIUM

### 3. Default ILogFailureSink silently degrades to NullLogFailureSink on every low-level pipeline-construction type
- Files: src/Bootstrap/LoggingBootstrap.cs:62, native/dotnet/Routing/DefaultLogSinkRouterFactory.cs:32, native/dotnet/Pipeline/SafeCompositeLogger.cs:56 (also native/dotnet/Routing/DefaultLogSinkFactory.cs:31, in an excluded file, same pattern)
- Class: B
- Scenario: All four types accept ILogFailureSink? failureSink = null and silently substitute NullLogFailureSink.Instance -- a sink whose ReportFailure does nothing but validate arguments. SafeCompositeLogger is the pipeline's per-event top-level composite; it catches every exception thrown by every child sink and routes it through _failureSink. The blessed path (JsonConfiguredLoggingBootstrapFactory) correctly threads a DiagnosticLogFailureSink through all four constructors, so this does not bite QuickLogBuilder consumers. It does bite any advanced/manual consumer who constructs LoggingBootstrap, DefaultLogSinkRouterFactory, or SafeCompositeLogger directly (all public types) without explicitly passing a failure sink -- the "just new it up" path is the one that goes completely silent on every runtime sink failure (render exceptions, I/O errors on file/network sinks, etc.), with no compiler or runtime nudge that a diagnostic surface was skipped.
- Fix shape: either make the parameter required (no default), or have the null-default log a one-time stderr NOTE the way the sink-substitution WARN does (no ILogFailureSink supplied -- sink failures will be discarded silently; pass DiagnosticLogFailureSink or your own ILogFailureSink).

### 4. DefaultLogSinkRouterFactory.WrapWithLoopback swallows file/URL construction failures with zero diagnostic
- File: native/dotnet/Routing/DefaultLogSinkRouterFactory.cs:160-202
- Class: B
- Scenario: When an operator sets TestLoopbackLogDir or TestLoopbackUrl, construction of the file writer / URL poster is wrapped in bare catch blocks that null out the leg, with comments noting bad path/permissions or a malformed URL make that leg just unavailable. The exception (and its message -- the actual bad path, the actual malformed URL, the actual permission error) is discarded completely; not even _failureSink.ReportFailure is called even though _failureSink is a field on this very class. An operator debugging why loopback is not capturing gets no lead at all -- the test-loopback feature just quietly does nothing.
- Fix shape: call _failureSink.ReportFailure(...) (or at minimum a stderr line) with the caught exception before nulling the leg out.

## Notes on things checked that are NOT problems (already well-guarded)
- LogSinkProviderRegistry.Resolve (fixed), SinkWrapperRegistry and its two consumers in DefaultLogSinkFactory (retry/audit) -- both throw actionable InvalidOperationExceptions naming the missing NuGet package and the WithPlugin<TPlugin>() remedy.
- PipelineDecoratorKindRegistry / JsonKindRegistry<T>.Reconstruct -- throws with an actionable unknown-kind message; used correctly by the JSON reload path.
- NamingPolicyRegistry.Resolve -- throws UnknownNamingPolicyException; TryResolve is a documented non-throwing variant used only by hot-reload's intentional degrade-to-previous-policy path.
- PipelineStrategy.FromNames (JSON path) -- throws with the full remedy message; this is the strategy the fluent Custom() path (finding #1) should mirror.
- PipelineStepHandlerKindRegistry -- Apache built-ins registered eagerly in the constructor; Pro/Enterprise handlers register via [ModuleInitializer] same as sinks, but there is no string-keyed consumer path that reaches an unregistered kind without going through ApplyStep's silent fallthrough (finding #1 covers the actual bug).
