# P7 Layer-2 Mirror — the-fool Pre-Mortem

> Generated: 2026-05-30. Input: the-fool skill, pre-mortem mode.
> Framing: *"Layer 2 is a hand-mirrored copy of Serilog's entire public surface, where the only
> correct amount of logic is zero. Where does a mirror silently grow behaviour, drift from the real
> Serilog shape, or fail to be the only Serilog in the graph?"*
>
> Attack vectors enumerated by the caller: logic leaks, under-mirror, signature drift, CS0433
> coexistence, cross-plan gap faking, the concrete `Logger` type, the `Log.Logger` second-slot
> violation, and `LogEvent` mutable enricher methods.

---

## Summary

| Severity | Count | All mitigated? |
|----------|-------|----------------|
| CRITICAL | 3     | 1 of 3 mitigated; **2 have no mitigating task** |
| HIGH     | 7     | 3 of 7 mitigated; **4 have no mitigating task or corpus coverage** |
| MEDIUM   | 4     | 2 of 4 mitigated; **2 partially mitigated only** |

**Two ship-blockers with no mitigating task today:**

- **CRIT-FM-L2** — `Log.Logger` second-slot: Layer-2 introduces its own backing field; all log calls
  through the Layer-2 static facade are silently discarded. No exception. No existing test catches it.
  Required: a `ReferenceEquals` slot-identity test in Task 3 (write Layer-1 → read Layer-2).

- **CRIT-FM-G2** — `CreateLogger()` return-type mismatch forces a cast into Layer-2's mirror, making
  it the single highest-logic member in the entire assembly. Requires the Layer-1/Layer-2 type contract
  to be locked before Task 4 starts, not discovered during implementation.

**Highest-severity unmitigated risk:** CRIT-FM-L2 (silent total log loss through the Layer-2 facade).

---

## Risk Catalog

### CRIT-FM-L1 — Logic Leak: Constructor Null-Guard on Value-Model Types

**Severity: CRITICAL**

**Description.**
A mirror constructor for `ScalarValue`, `StructureValue`, `SequenceValue`, or `DictionaryValue` adds
a validation expression — e.g., `ArgumentNullException.ThrowIfNull(value)`. The author's intent is
defensive. Real Serilog's `ScalarValue(null)` is valid and represents a SQL NULL or absent field.
A user enricher that writes `new ScalarValue(null)` to represent a missing tenant ID compiles and
runs against real Serilog but throws `ArgumentNullException` against the mirror.

The DRY tripwire (Task 7) checks for `IfStatement` AST nodes. `ArgumentNullException.ThrowIfNull(v)`
is a single-expression method call — **no `IfStatement` node**, so the Roslyn source-level check
passes. The mirror ships with a behavioral divergence that only fires on null-valued properties.

**Why invisible.** The corpus (Task 6) uses well-formed data. No snippet constructs `ScalarValue(null)`
because happy-path templates do not produce nulls.

**Second-order effect.** An anonymous-user request where tenant ID is absent triggers
`ArgumentNullException` inside the consumer's enricher code. The stack trace points at their code,
not the mirror.

**Mitigating task.** Task 7 (DRY tripwire) — **conditional**: the tripwire must add these single-
expression forms to its forbidden list: `ArgumentNullException.ThrowIfNull(...)`,
`ArgumentOutOfRangeException.ThrowIfNull(...)`, `Debug.Assert(...)`, and the null-forgiving operator
`!` applied to a value that could legitimately be null. The tripwire as currently specified (catches
`IfStatement`/`ForStatement`/`WhileStatement`) is **insufficient** without this extension.

**Does the mitigation test go RED if the guard is removed?** Yes, once the tripwire explicitly names
these patterns. Without the extension it passes vacuously.

**FLAG — MITIGATION REQUIRES TASK 7 SCOPE EXTENSION.** Add to Task 7 Step 1: "Extend the forbidden-
expression list to include single-expression validation calls: `ArgumentNullException.ThrowIfNull`,
`ArgumentOutOfRangeException.ThrowIf*`, `Debug.Assert`, and the null-forgiving operator `!` applied
to a dereference of an argument."

---

### CRIT-FM-L2 — Logic Leak: `Log.Logger` Introduces a Second Mutable Slot

**Severity: CRITICAL — NO MITIGATING TASK**

**Description.**
The Layer-2 `Serilog.Log.Logger` property is authored with its own backing field:

```csharp
// Layer-2 Serilog/Log.cs — WRONG
private static ILogger _logger = SilentLogger.Instance; // a second slot
public static ILogger Logger
{
    get => _logger;
    set => _logger = value;
}
```

Richard's invariant: `Log.Logger` is one mutable slot in Layer 1, not duplicated. A consumer who
writes to `MMP.Herald.Serilog.Log.Logger` (e.g., inside P2's `LoggerConfiguration.CreateLogger()`
result-wiring) and reads via `Serilog.Log.Logger` gets `SilentLogger.Instance` back. Every call
through the Layer-2 static facade (`Serilog.Log.Information(...)`) is silently discarded. No
exception fires. The loss is total.

**Why invisible.** The corpus test (Task 6) uses `new LoggerConfiguration().WriteTo.X().CreateLogger()`
end-to-end and assigns the result. If both the assignment and the read go through Layer-2, the test
passes. The split-slot failure only surfaces when Layer-1's write path and Layer-2's read path are
exercised in the same run — which is exactly the migration scenario (a consumer partially migrated to
Layer 2, or test infrastructure referencing both namespaces).

**Correct form.** `Serilog.Log.Logger { get => MMP.Herald.Serilog.Log.Logger; set => MMP.Herald.Serilog.Log.Logger = value; }`. One line per accessor. No backing field. No state.

**Mitigating task.** NONE.

**Required addition to Task 3 (Step 2):** After mirroring the static `Log` facade, add a slot-identity
test:

```csharp
[Fact]
public void Log_Logger_Layer2_and_Layer1_are_the_same_slot()
{
    var logger = new LoggerConfiguration().WriteTo.Sink(new NullSink()).CreateLogger();
    MMP.Herald.Serilog.Log.Logger = logger;
    Assert.True(
        ReferenceEquals(MMP.Herald.Serilog.Log.Logger, Serilog.Log.Logger),
        "Layer-1 and Layer-2 Log.Logger must resolve to the same object — no second slot.");
}
```

**Does the test go RED if the second slot is introduced?** Yes — `ReferenceEquals` fails immediately
when the Layer-2 getter returns its own field instead of forwarding to Layer-1. Confirmed ship-blocker.

---

### CRIT-FM-G2 — Cross-Plan Gap: `CreateLogger()` Return-Type Mismatch Forces a Cast

**Severity: CRITICAL — NO MITIGATING TASK**

**Description.**
Layer-2 `Serilog.LoggerConfiguration.CreateLogger()` must return `Serilog.Core.Logger` (the Layer-2
type). Layer-1 `MMP.Herald.Serilog.LoggerConfiguration.CreateLogger()` returns
`MMP.Herald.Serilog.ILogger` (R-2 ratified: `SerilogLoggerAdapter` implements this interface).

The Layer-2 forward is therefore NOT one-line-pure without a type narrowing:

```csharp
// Layer-2 LoggerConfiguration.cs — WRONG: cast is logic
public Serilog.Core.Logger CreateLogger()
{
    var l1 = _inner.CreateLogger();   // returns MMP.Herald.Serilog.ILogger
    return (Serilog.Core.Logger)l1;   // cast — logic in the mirror
}
```

A cast that throws on unexpected types is a behavioral addition. The DRY tripwire catches a
`CastExpression` only if it is in the explicit forbidden list.

**The correct resolution (must be locked before Task 4).** R-2 specifies that Layer-2's
`Serilog.Core.Logger` is an alias to `SerilogLoggerAdapter`. If `SerilogLoggerAdapter` IS the
`Serilog.Core.Logger` type (same CLR type, same assembly identity — achievable only if Layer-1's
`SerilogLoggerAdapter` is defined IN the Layer-2 assembly, which contradicts the architecture), then
the cast is a no-op. The correct path: Layer-1's `CreateLogger()` must return the Layer-2 `Logger`
type directly — which means Layer-1 references Layer-2, which is a circular dependency, which is
impossible.

**Actual correct resolution.** The Layer-1 `CreateLogger()` must return `SerilogLoggerAdapter`
directly (not just `ILogger`). Layer-2 `Serilog.Core.Logger` is a `sealed class` that wraps or IS
`SerilogLoggerAdapter` via composition, not cast. Every member on Layer-2 `Logger` forwards to the
wrapped `SerilogLoggerAdapter`. This is the only way to have a zero-logic forward: the Layer-2
`Logger` constructor takes a `SerilogLoggerAdapter` and stores it; `CreateLogger()` in Layer-2 calls
`new Serilog.Core.Logger(_inner.CreateLogger())`. No cast. One object-creation expression.

**This contract must be confirmed before Task 4 Step 1 authors `LoggerConfiguration.cs`.** If it
is not, the first implementation will either introduce a cast or return the wrong type.

**Mitigating task.** NONE.

**Required addition (pre-Task 4 gate):** Add a Step 0 to Task 4: "Confirm with P1/P2 owners that
`MMP.Herald.Serilog.LoggerConfiguration.CreateLogger()` returns `SerilogLoggerAdapter` (not just
`ILogger`). If it returns `ILogger`, Layer-2's zero-logic invariant cannot hold for `CreateLogger()`;
escalate to Richard before authoring the type. Do NOT use a cast as a workaround."

---

### HIGH-FM-L3 — Logic Leak: `LogEvent.Properties` Builds a Dictionary on Each Access

**Severity: HIGH**

**Description.**
The Layer-2 `Serilog.Events.LogEvent.Properties` getter returns
`IReadOnlyDictionary<string, LogEventPropertyValue>`. A naive implementation projects the entire
Layer-1 property list on every call:

```csharp
// WRONG: ToDictionary is a LINQ allocation on every access
public IReadOnlyDictionary<string, LogEventPropertyValue> Properties =>
    _inner.Properties.ToDictionary(p => p.Name, p => LogEventValueProjector.Project(p));
```

`ToDictionary` is a method call with no `IfStatement`/`ForEachStatement` node in Roslyn's
`SyntaxKind` taxonomy — the DRY tripwire may not catch it unless `InvocationExpression` targeting
known allocating methods is in the forbidden list.

**Second-order effect.** Every custom sink that reads `event.Properties["RequestId"]` allocates a
full property dictionary on every `Emit` call. The Guard-2 "zero allocation on the native path"
claim holds; the custom-sink path pays unbounded allocation proportional to property count. A sink
author benchmarking against real Serilog sees better numbers on real Serilog and files a bug.

**Correct form.** `Properties => _mirror.Properties` — forwarding to the Layer-1 mirror's lazy-
projected `_projected` dictionary (Rosanne's Task-8 Seam C), built once and cached on first access.
The projection work lives entirely in Layer 1.

**Mitigating task.** Task 7 (DRY tripwire) — **conditional**: the tripwire must add well-known
LINQ-allocating method names (`ToDictionary`, `ToList`, `ToArray`, `Select`, `Where`) to its
forbidden-invocation list. Without this extension, a `ToDictionary` call in a property getter
passes the current tripwire check.

**Required addition to Task 7 Step 1:** Extend the forbidden-expression list to include LINQ
terminal operators (`ToDictionary`, `ToList`, `ToArray`) and new-collection expressions
(`new Dictionary<...>`, `new List<...>`) in member bodies.

**Does the test go RED if the mitigation is removed?** Yes, once the tripwire names these patterns.
The allocation probe in the G-CORPUS.1 test would also catch it IF a custom-sink corpus snippet
exercises `event.Properties`.

---

### HIGH-FM-U1 — Under-Mirror: Concrete `Logger` Type Not in Corpus

**Severity: HIGH**

**Description.**
Real Serilog code written before 2020 universally stores the logger as the concrete type:

```csharp
Logger log = new LoggerConfiguration().WriteTo.Console().CreateLogger();
log.Dispose();
```

P7's surface table includes `Serilog.Core.Logger` and R-2 (cross-plan reconciliation) resolves it
as a Layer-2 alias for `SerilogLoggerAdapter`. If P7 exposes `Logger` as an interface rather than a
`sealed class`, the `Logger log = ...` declaration fails to compile.

**Why invisible.** The corpus test (Task 6) uses `ILogger log = ...` (post-2015 idiomatic Serilog).
If every corpus snippet uses the interface form, the concrete-type under-mirror ships undetected.

**Mitigating task.** Task 6 (G-CORPUS.1) — **conditional**: the corpus must include at minimum one
snippet with `Logger log = new LoggerConfiguration().CreateLogger()` (concrete-type field storage)
AND a `log.Dispose()` call (verifying `IDisposable` is present on the Layer-2 type).

**Required addition to Task 6 Step 1:** "Include a concrete-type corpus snippet:
`Serilog.Core.Logger log = new LoggerConfiguration()...CreateLogger(); log.Dispose();`. This snippet
must compile and run. Its absence means the most common pre-2015 Serilog field declaration is
untested."

**Does the test go RED if the under-mirror ships?** Yes, once the concrete-type snippet is in the
corpus — `CS0246` (type not found) or `CS0266` (cannot implicitly convert) fires at compile.

---

### HIGH-FM-U2 — Under-Mirror: `Serilog.Configuration.*` Class Names Not Confirmed Against Layer-1

**Severity: HIGH**

**Description.**
P7's surface table lists `Serilog.Configuration.*` as a group ("the config-object set") without
naming each class individually. Real Serilog exposes six concrete classes:
`LoggerSinkConfiguration`, `LoggerEnrichmentConfiguration`, `LoggerMinimumLevelConfiguration`,
`LoggerDestructuringConfiguration`, `LoggerFilterConfiguration`, `LoggerAuditSinkConfiguration`.

Consumer code stores these to chain calls:

```csharp
LoggerSinkConfiguration sinks = logCfg.WriteTo;
sinks.Console();
sinks.File("log.txt");
```

If P2's Layer-1 twins have different class names (e.g., `SerilogSinkConfiguration`), and P7 mirrors
them under the wrong name (or maps `WriteTo` to return a P2 type with a different name), the stored-
variable form fails to compile. The fluent-chained form (`logCfg.WriteTo.Console()`) works; the
stored-variable form doesn't.

**Mitigating task.** P7 Task 4 Step 2 (pre-mirror diff) — **must be made explicit**. The step says
"read first: the Layer-1 twins from P2." It does not say "diff the Layer-1 class names against the
six real Serilog `Serilog.Configuration.*` class names before mirroring."

**Required addition to Task 4 Step 2:** "Before authoring any `Serilog.Configuration.*` type, list
the six real Serilog config-object class names. Confirm each Layer-1 twin (from P2's actual output)
has the same name or is explicitly aliased. Any name mismatch is a FLAG; do not paper over with a
using alias."

**Required corpus addition (Task 6 Step 1):** "Include a stored-variable snippet:
`LoggerSinkConfiguration sinks = new LoggerConfiguration().WriteTo; sinks.Console();`".

---

### HIGH-FM-U3 — Under-Mirror: `MessageTemplate` Token Subtypes Not Enumerated

**Severity: HIGH**

**Description.**
Real Serilog's `MessageTemplate` exposes `Tokens` typed as `IEnumerable<MessageTemplateToken>`.
`MessageTemplateToken` has two concrete subtypes: `TextToken` (with `.Text` string) and
`PropertyToken` (with `.PropertyName`, `.Format`, `.Alignment`, `.Destructuring` properties).

Custom formatters iterate `event.MessageTemplate.Tokens` to reconstruct or reformat the message.
If Layer-2 exposes `Tokens` as `IEnumerable<object>` or omits `TextToken`/`PropertyToken` as
named public types, any custom formatter fails to compile.

**Why likely to be missed.** The P7 surface table lists `MessageTemplate` as a single row. The plan
says "forward to Layer-1 template twin." P1's `MessageTemplate` support is specified as covering
the `{@}`/`{$}` inline parsing, but whether `MessageTemplateToken`, `TextToken`, and `PropertyToken`
are distinct public types in Layer-1 is not confirmed in any plan.

**Mitigating task.** Task 2 Step 2 — **conditional on the corpus**: the corpus must include a
formatter-style snippet that iterates `event.MessageTemplate.Tokens` and type-tests for
`PropertyToken`. Without this snippet, omitted token subtypes ship silently.

**Required addition to Task 2 Step 2:** "Confirm that the Layer-1 `MessageTemplate` exposes
`Tokens` as `IEnumerable<MessageTemplateToken>` and that `TextToken` and `PropertyToken` are public
Layer-1 types before mirroring. If they are not, FLAG — do not invent the types here."

**Required corpus addition (Task 6 Step 1):** One formatter snippet that does
`foreach (var token in evt.MessageTemplate.Tokens) { if (token is PropertyToken pt) ... }`.

---

### HIGH-FM-S1 — Signature Drift: Default Parameter Values Missing from Mirrored Overloads

**Severity: HIGH**

**Description.**
Real Serilog's `ILogger.ForContext(string propertyName, object value, bool destructureObjects = false)`
has a default parameter. If the Layer-2 mirror declares the overload without the default:

```csharp
// WRONG: missing default
ILogger ForContext(string propertyName, object value, bool destructureObjects);
```

code that calls `log.ForContext("RequestId", requestId)` (two-argument shorthand, compiled against
real Serilog) will not compile against the mirror. C# default parameters are resolved at the call
site; the callee must declare them. A corpus that always passes all three arguments explicitly never
exercises the two-argument form.

**Mitigating task.** Task 3 (mirror `ILogger`) — mitigated IF the corpus includes a two-argument
`ForContext` call. Not currently specified.

**Required corpus addition (Task 6 Step 1):** For every `ILogger` method with a default parameter,
include a corpus snippet that exercises the default-omitted form.

**Broader mitigation.** Add a step to Task 2 (value types), Task 3 (call surface), Task 4
(configuration + seams), and Task 5 (AspNetCore): "After authoring each type, diff every method
signature against the real Serilog 4.3.1 source for default parameter values. A missing default is
a signature-drift failure."

---

### HIGH-FM-C1 — CS0433 via Transitive Dependency (Not Direct Reference)

**Severity: HIGH**

**Description.**
A consumer migrates to Layer 2, removes their direct real-Serilog package reference, and rebuilds
cleanly. Then they add a NuGet package — an OpenTelemetry adapter, a structured-logging helper, or
any instrumentation library — that has a transitive dependency on the real `Serilog` package. The
consumer's project now has both `Serilog` (mirror) and `Serilog` (real) in the graph. `CS0433` fires
with a message naming two assemblies both called `Serilog`. The consumer doesn't know which package
introduced the transitive reference.

G-LAYER2.1 (Task 8) tests the direct-reference case. It does not test or document the transitive
case. The migration runbook (P8 Task 4) says "remove all real-Serilog references" — the consumer
did; a dependency did not.

**Second-order effect.** This is the most likely production failure mode post-migration. Not a
conscious coexistence error, but an invisible one introduced by a routine `dotnet add package`.

**Mitigating task.** Task 8 (G-LAYER2.1) — **conditional**: the meta-test must also verify that
the `CS0433` error message is legible (names the conflicting package, not just the type). Currently
the test only asserts `Assert.Contains("CS0433", result.Output)`.

**Required addition to the migration runbook (P8 Task 4):** "After every `dotnet add package` that
touches the logging ecosystem, run `dotnet list package --include-transitive | grep Serilog`. Any
transitive `Serilog` reference from a non-Herald package means that package cannot coexist with
Layer 2. This check is NOT covered by G-LAYER2.1, which only tests direct references."

**Required addition to Task 8 Step 2:** Verify the `CS0433` output names the conflicting assembly
source: `Assert.Contains("Serilog.dll", result.Output)` or similar — so the consumer can identify
the transitive source.

---

### MED-FM-S2 — Signature Drift: `WriteTo.Logger(Action<LoggerConfiguration>)` Sub-Logger Overload

**Severity: MEDIUM**

**Description.**
Real Serilog's `LoggerSinkConfiguration.Logger(...)` has two overloads:
1. `Logger(ILogger logger, LogEventLevel restrictedToMinimumLevel)` — pre-built logger
2. `Logger(Action<LoggerConfiguration> configureLogger, ...)` — lambda form (sub-logger pattern)

Sub-loggers (S6 in Rosanne's inventory) are optional Layer-1 seam ("land when the corpus shows real
usage"). If P7 mirrors only overload 1, code that uses `WriteTo.Logger(lc => lc.WriteTo.Console())`
fails to compile.

**Cross-plan gap faking risk.** If the Layer-1 twin for overload 2 doesn't exist, a P7 implementer
might author a `throw new NotSupportedException(...)` stub. A stub on the allowlist (Task 7 Step 1
permits `throw new NotSupportedException` with a named reason) is acceptable — but only if:
(a) it is explicitly on the allowlist with a reason, and (b) the corpus includes a test that
verifies the stub throws with a diagnostic message, not a silent swallow.

**Mitigating task.** Task 7 (allowlist review) — conditional on the implementer knowing to add it.

**Required addition to Task 4 Step 2:** "For every overload in the `Serilog.Configuration.*` set,
confirm whether the Layer-1 twin exists. For overloads with no Layer-1 twin: add to the Task 7
allowlist with a named reason and a test that asserts the throw message is actionable."

---

### MED-FM-S3 — Signature Drift: `LoggingLevelSwitch.MinimumLevel` Property Type Namespace

**Severity: MEDIUM**

**Description.**
Layer-2 `Serilog.Core.LoggingLevelSwitch.MinimumLevel` must be typed as
`Serilog.Events.LogEventLevel` (Layer-2 enum). If the getter/setter forwards to the Layer-1 wrapper
and returns `MMP.Herald.Serilog.Events.LogEventLevel` (Layer-1 enum namespace), an assignment:

```csharp
sw.MinimumLevel = Serilog.Events.LogEventLevel.Warning;
```

produces a type mismatch. The property compiles within Layer-2 itself but a consumer reading the
property value back into a `Serilog.Events.LogEventLevel` variable gets a type error.

More critically: if the Layer-2 getter converts the Layer-1 enum value to the Layer-2 enum value
(`(Serilog.Events.LogEventLevel)(int)_inner.MinimumLevel`), that cast expression is logic — the
DRY tripwire must flag it.

**Mitigating task.** Task 4 (Step 3, `LoggingLevelSwitch` mirror) — conditionally mitigated IF the
`LogEventLevel` enum values are numerically identical (Verbose=0…Fatal=5) AND the two enum types
share the same underlying CLR type space, making the cast safe. This must be confirmed rather than
assumed.

**Required addition to Task 4 Step 3:** "Confirm that Layer-2 `LoggingLevelSwitch.MinimumLevel`
returns `Serilog.Events.LogEventLevel` (not `MMP.Herald.Serilog.Events.LogEventLevel`). If the
underlying enum values match, the forward is a direct return of the Layer-1 value cast to Layer-2's
enum type — confirm this cast is the ONLY permitted exception to the zero-logic rule (documented on
the Task 7 allowlist)."

---

### MED-FM-E1 — Mutable Enricher Methods: No Owning Task in P7

**Severity: MEDIUM**

**Description.**
Rosanne's Task-8 Seam C specifies three mutation methods on the Layer-1 `LogEvent` mirror:
`AddOrUpdateProperty`, `AddPropertyIfAbsent`, `RemovePropertyIfPresent`. The Layer-2 mirror of
`Serilog.Events.LogEvent` must expose these same methods for custom enrichers (S2 seam) to call.

The P7 plan does not enumerate these methods in Task 2 Step 2 (which lists the value types to
mirror), Task 3 (call surface), or Task 4 (seam interfaces). If the implementer mirrors `LogEvent`
without the mutation methods, any custom enricher that calls `event.AddOrUpdateProperty(...)` fails
to compile against Layer 2.

**Mitigating task.** Task 2 (Step 2) — **conditional**: the step says "each property/method is a
one-line forward." It does not name the mutation methods. The implementer must know to look for them
in real Serilog's `LogEvent` API.

**Required addition to Task 2 Step 2:** "Include the three enricher mutation methods in the
`Serilog.Events.LogEvent` mirror: `AddOrUpdateProperty(LogEventProperty)`,
`AddPropertyIfAbsent(LogEventProperty)`, `RemovePropertyIfPresent(string)`. Each forwards to the
Layer-1 mirror's overlay mutators (Seam C). If Seam C is not yet implemented in Layer 1, FLAG — do
not implement the mutation logic here."

**Required corpus addition (Task 6 Step 1):** "Include an enricher snippet that calls
`event.AddOrUpdateProperty(new LogEventProperty(\"TraceId\", new ScalarValue(traceId)))`. Without
this, the missing mutation methods ship undetected."

---

### MED-FM-G1 — Cross-Plan Gap Faking: `SelfLog` With Own Output Buffer

**Severity: MEDIUM**

**Description.**
`Serilog.Debugging.SelfLog.Enable(Action<string>)` and `SelfLog.WriteLine(...)` must forward to P4's
`SelfLog` facade over `ISinkHealthReporter`. If P4's facade doesn't exist when P7 is implemented, a
gap-faking implementation introduces a static backing field and a `string.Format` call — both logic.
The static field alone (`private static Action<string>? _output`) is a second mutable slot, which is
a logic addition the DRY tripwire must catch.

**Mitigating task.** Task 4 (Step 4, `SelfLog` mirror) — the task correctly says "forward to P4's
`SelfLog` facade; if P4 doesn't exist, FLAG." The risk is that the static field form is easy to write
and `string.Format` is a single invocation — the DRY tripwire catches format calls only if
`string.Format` and interpolated strings are in the forbidden list.

**Required addition to Task 7 Step 1:** Extend the forbidden-expression list to include
`string.Format(...)`, `string.Concat(...)`, and `$"..."` interpolated strings (these are format
operations that belong in Layer 1, never in the mirror).

**Required pre-Task 4 Step 4 gate:** "Confirm P4's `MMP.Herald.Serilog.Debugging.SelfLog` is
publicly reachable. If it is not, STOP — do not author `SelfLog` behavior here. Enter the gap as a
P4 blocker for P7."

---

## Risks with NO Mitigating Task

| Risk | Severity | Required Addition |
|------|----------|-------------------|
| CRIT-FM-L2: `Log.Logger` second mutable slot | CRITICAL | Add `ReferenceEquals` slot-identity test to Task 3 Step 2 |
| CRIT-FM-G2: `CreateLogger()` return-type mismatch forces cast | CRITICAL | Add pre-Task 4 Step 0 gate: confirm Layer-1 `CreateLogger()` return type with P1/P2 owners before authoring |
| HIGH-FM-C1: CS0433 via transitive dependency (not direct) | HIGH | Add transitive-reference check to migration runbook (P8 Task 4) + legibility assertion to Task 8 Step 2 |

---

## Conditional Mitigations (Risk Survives if Condition Not Met)

| Risk | Condition for Full Mitigation |
|------|------------------------------|
| CRIT-FM-L1: Constructor null-guard | Task 7 tripwire must add `ArgumentNullException.ThrowIfNull`, `Debug.Assert`, and `!` null-forgiving to forbidden list |
| HIGH-FM-L3: `LogEvent.Properties` LINQ allocation | Task 7 tripwire must add `ToDictionary`, `ToList`, `ToArray`, `new Dictionary<>` to forbidden list |
| HIGH-FM-U1: Concrete `Logger` type | Task 6 corpus must include `Logger log = ... CreateLogger(); log.Dispose();` |
| HIGH-FM-U2: Config-object class names | Task 4 Step 2 must diff Layer-1 class names against real Serilog's six `Serilog.Configuration.*` names before mirroring |
| HIGH-FM-U3: MessageTemplate token subtypes | Task 2 Step 2 must confirm `TextToken`/`PropertyToken` are public Layer-1 types; Task 6 corpus must include a token-iteration snippet |
| HIGH-FM-S1: Default parameter values | Task 6 corpus must include a default-omitted call for every `ILogger` method with a default |
| MED-FM-E1: Mutable enricher methods | Task 2 Step 2 must name the three mutation methods; Task 6 corpus must include an enricher that calls one |
| MED-FM-S3: `LoggingLevelSwitch.MinimumLevel` enum type | Task 4 Step 3 must confirm the forward is the Layer-2 enum type; if a cast is required, add it to the Task 7 allowlist with a reason |

---

## DRY Tripwire Extension Requirements (Task 7 Step 1 additions)

The current tripwire (Task 7 Step 1 as written) catches: `IfStatement`, `ForStatement`,
`ForEachStatement`, `WhileStatement`, `SwitchStatement`, `TryStatement`, `string.Format`,
interpolation-with-logic.

The following must be added to catch the silent logic-leak classes identified above:

| Pattern | FM it catches |
|---------|--------------|
| `ArgumentNullException.ThrowIfNull(...)` | CRIT-FM-L1 |
| `ArgumentOutOfRangeException.ThrowIf*(...)` | CRIT-FM-L1 |
| `Debug.Assert(...)` | CRIT-FM-L1 |
| Null-forgiving `!` on an argument | CRIT-FM-L1 |
| Static field declarations in Layer-2 types | CRIT-FM-L2, MED-FM-G1 |
| `ToDictionary(...)`, `ToList(...)`, `ToArray(...)` | HIGH-FM-L3 |
| `new Dictionary<...>`, `new List<...>` | HIGH-FM-L3 |
| `string.Format(...)`, `string.Concat(...)` | MED-FM-G1 |
| `CastExpression` (explicit cast) | CRIT-FM-G2, MED-FM-S3 |

Allowlist entries (the only permitted exceptions):
- `throw new NotSupportedException(...)` — for deliberately unmirrored members (named reason required)
- Null-propagating forward `x?.Twin` — only where the forward itself is the null-propagation
- One explicit enum cast on `LoggingLevelSwitch.MinimumLevel` — IF confirmed numerically safe and
  added to the allowlist with a comment

---

## Corpus Coverage Requirements (Task 6 Step 1 additions)

| Snippet | FM it catches |
|---------|--------------|
| `Logger log = new LoggerConfiguration().CreateLogger(); log.Dispose();` | HIGH-FM-U1 |
| `LoggerSinkConfiguration sinks = logCfg.WriteTo; sinks.Console();` | HIGH-FM-U2 |
| `foreach (var token in evt.MessageTemplate.Tokens) { if (token is PropertyToken pt)...}` | HIGH-FM-U3 |
| `log.ForContext("Key", value)` (two-arg, omitting the `bool` default) | HIGH-FM-S1 |
| Enricher calling `event.AddOrUpdateProperty(new LogEventProperty("TraceId", new ScalarValue(id)))` | MED-FM-E1 |

---

## Tasks to Add (Risks Currently Without Mitigating Tasks)

### New Step: Task 3 Step 2a — `Log.Logger` Slot-Identity Test

Add immediately after Task 3 Step 2 (mirror the static `Log` facade):

```csharp
[Fact]
public void Log_Logger_Layer2_and_Layer1_are_the_same_slot()
{
    var logger = new LoggerConfiguration().WriteTo.Sink(new NullSink()).CreateLogger();
    MMP.Herald.Serilog.Log.Logger = logger;
    Assert.True(
        ReferenceEquals(MMP.Herald.Serilog.Log.Logger, Serilog.Log.Logger),
        "Layer-1 and Layer-2 Log.Logger must return the same object. " +
        "A second backing field in Layer-2 is the DRY tripwire for the static facade.");
}
```

This test goes RED the moment a second `_logger` field appears in the Layer-2 `Log.cs`. It is
**not** covered by the DRY tripwire alone (a `private static ILogger _logger` without an `if`
statement passes the source-level check). It requires a behavioral slot-identity assertion.

### New Step: Task 4 Step 0 — `CreateLogger()` Return-Type Contract Gate

Add before Task 4 Step 1:

**Step 0: Confirm the `CreateLogger()` return-type contract.** Read the Layer-1 `MMP.Herald.Serilog.LoggerConfiguration.CreateLogger()` signature from P1/P2's actual output. Confirm it returns `SerilogLoggerAdapter` (not just `ILogger`). If it returns `ILogger`, Layer-2's zero-logic invariant cannot be satisfied for this method — escalate to Richard and resolve before authoring `LoggerConfiguration.cs`. Do NOT author a cast as a workaround.

The correct zero-logic Layer-2 form is:
```csharp
public Serilog.Core.Logger CreateLogger()
    => new Serilog.Core.Logger(_inner.CreateLogger()); // wraps the adapter — one new-expression
```
This is permissible only if `Serilog.Core.Logger` is a wrapper (constructor takes `SerilogLoggerAdapter`), not a cast. The DRY tripwire allowlist must include constructor-wrapping of the sole concrete return type.

---

## Open Decisions Requiring Resolution Before Named Tasks Merge

| OD | Must Resolve Before | Risk if Deferred |
|----|--------------------|--------------------|
| OD-P7-1: Does Layer-1 `CreateLogger()` return `SerilogLoggerAdapter` (not just `ILogger`)? | Task 4 Step 1 | Layer-2 `CreateLogger()` is the one member that cannot be zero-logic without a cast — which is forbidden |
| OD-P7-2: Are `TextToken`/`PropertyToken` public Layer-1 types? | Task 2 Step 2 | `MessageTemplate.Tokens` is under-mirrored; custom formatters fail to compile |
| OD-P7-3: Do the six `Serilog.Configuration.*` classes have the same names in Layer-1 (P2)? | Task 4 Step 2 | Stored-variable form of configuration fails to compile against the mirror |
| OD-P7-4: Are `AddOrUpdateProperty`/`AddPropertyIfAbsent`/`RemovePropertyIfPresent` publicly exposed on the Layer-1 `LogEvent` mirror? | Task 2 Step 2 | Mutation methods missing from Layer-2 `LogEvent`; custom enrichers fail to compile |

---

## Relationship to Existing Plan Tasks

| FM | Existing mitigating task | Mitigation status |
|----|------------------------|-------------------|
| CRIT-FM-L1: Constructor null-guard | Task 7 (DRY tripwire) | Conditional — tripwire must be extended |
| CRIT-FM-L2: Second `Log.Logger` slot | None | No mitigating task — add slot-identity test to Task 3 |
| CRIT-FM-G2: `CreateLogger()` cast | None | No mitigating task — add pre-Task 4 gate |
| HIGH-FM-L3: `Properties` LINQ allocation | Task 7 (DRY tripwire) | Conditional — tripwire must be extended |
| HIGH-FM-U1: Concrete `Logger` corpus | Task 6 (G-CORPUS.1) | Conditional — corpus must add the concrete-type snippet |
| HIGH-FM-U2: Config-object class names | Task 4 (pre-mirror diff) | Conditional — diff step must be made explicit |
| HIGH-FM-U3: `MessageTemplate` token subtypes | Task 2 + Task 6 | Conditional — cross-plan confirm + corpus snippet |
| HIGH-FM-S1: Default parameter corpus | Task 6 (G-CORPUS.1) | Conditional — corpus must add default-omitted calls |
| HIGH-FM-C1: Transitive CS0433 | Task 8 (G-LAYER2.1) | Partial — direct only; transitive needs runbook addition |
| MED-FM-S2: Sub-logger lambda overload | Task 7 (allowlist) | Conditional — implementer must know to add it |
| MED-FM-S3: `LoggingLevelSwitch` enum namespace | Task 4 Step 3 | Conditional — confirm + allowlist if cast required |
| MED-FM-E1: Mutation methods no owning task | Task 2 Step 2 | Conditional — step must name the three methods |
| MED-FM-G1: `SelfLog` own buffer | Task 4 Step 4 + Task 7 | Conditional — tripwire extension for static fields |
