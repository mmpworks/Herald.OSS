---
title: Serilog.Expressions companion-port. 0.12.4 implementation plan
status: design (Richard, paired with Jared on parser/evaluator depth)
gap-id: expressions-dsl
regression-test-id: G-GAP.2
target-release: 0.12.4 (Tier 1), 0.12.x (Tier 2/3)
license: Apache-2.0 (upstream is the spec)
---

# Serilog.Expressions companion-port

## Decision

Ship a separate Apache-2.0 companion package, MMP.Herald.Serilog.Expressions, that mirrors the Serilog.Expressions public API so existing Serilog config and code recompile against it. Steve accepted another-lib-to-invoke: minor friction, big payoff. Knocking this wall down leaves ONLY pre-compiled community sink binaries as the remaining non-carryover (an identity wall we will not spoof). The DSL itself carries over.

New compile boundary, not a Core edit. Plugs into the existing ILogFilter seam (src/Filters/ILogFilter.cs) and the existing ISerilogEventView rendering surface (src/Serilog/Formatting/*). Core stays untouched and AOT-clean.

## Package shape (what the consumer adds and calls)

The consumer adds one package reference. The API surface matches Serilog.Expressions 1:1:

    .Filter.ByExcluding(  RequestPath like /health-prefix  )
    .Filter.ByIncludingOnly(  @Level = Error and StatusCode >= 500  )
    .WriteTo.Console(new ExpressionTemplate(  {@t:HH:mm:ss} [{@l:u3}] {@m} {@x}  ))

(Quotes elided in the sketch above to keep the doc tool-safe; real calls take the literal Serilog expression strings verbatim.)

Filter.ByExcluding(string) and ByIncludingOnly(string) are the Layer-1 compat overloads. They compile the string once and hand an ILogFilter to the pipeline. ExpressionTemplate implements the same formatter surface the output-template renderer already targets.

## Reuse vs build (be specific)

- Superpower (already a dependency): REUSE. Same tokenizer/combinator foundation Jared builds the new grammar on.
- SerilogOutputTemplateParser / Token / Renderer: REUSE for the ExpressionTemplate literal-and-hole scanner. The Name/Align/Format machinery, brace-escaping, and alignment are done. Net-new: the #if / #else / #end / #each directive blocks and holes whose body is an expression rather than a property name.
- ExpressionLogFilter (Query addon): REUSE the SHAPE. Parse-once-in-ctor, evaluate-per-event, ILogFilter adapter, drop attribution via DropReasons.Predicate, QueryParseException at build time. Copy this skeleton verbatim.
- QueryParser / QueryExpression / QueryEvaluator: REUSE the PATTERN, not the grammar. The Query DSL is Lucene-shaped (field colon value AND ...). Serilog is a real expression language: arithmetic, like, in, ternary, function calls, @-builtins, array/object literals. The AST-record plus switch-evaluator structure transfers cleanly; the node set and parser are net-new.
- @Properties indexing resolution: REUSE LogEvent.GetProperty(name) (O(1) indexed, case-insensitive). The expression binder calls straight into it.

Honest split: about 30% reuse (foundation plus shapes), about 70% net-new (grammar, evaluator, builtins). The reuse is what makes this a port rather than a greenfield engine.

## Runtime type model (the binding)

Serilog expressions evaluate against Serilog LogEvent. Ours bind to Herald event through one resolver, mirroring QueryEvaluator.ResolveField but over the Serilog name space:

- @l / @Level resolves to evt.Level?.Key (string). See F1: bind severity-rank-aware.
- @m / @Message resolves to evt.Message.
- @mt resolves to evt.MessageTemplate.
- @t / @Timestamp resolves to the event timestamp.
- @x / @Exception resolves to the exception text.
- @p / @Properties resolves to the property collection.
- @Properties[name], dotted a.b.c, and bare Name all resolve to evt.GetProperty(path)?.Value.

Expression values carry runtime CLR type (object) exactly like the Query evaluator actual. The numeric-vs-string coercion rules (TryBothAsDouble, quoted = force-string) are reused directly. They already match Serilog intent of comparing numerically when both sides look numeric.

## Scope tiers

- Tier 1 (string-DSL filtering): ByExcluding/ByIncludingOnly, full comparison/boolean/like/in/arithmetic/ternary/property-access grammar, and the high-frequency builtins (StartsWith, EndsWith, Contains, Length, Substring, Coalesce, ToString, IsDefined). THIS IS THE 0.12.4 CUT. Highest-value, most-used surface; closes the filtering half of G-GAP.2.
- Tier 2 (ExpressionTemplate output): #if / #else-if / #end conditionals, expression holes, format specifiers. Builds on the existing renderer. 0.12.x next.
- Tier 3 (long tail): #each iteration, the full builtin catalog (IndexOf, Round, TypeOf, Now, UtcDateTime, ElementAt), array/object literals as values. Demand-driven.

Recommended 0.12.4 cut: Tier 1 only. It removes the documented hard wall for the common case and ships a coherent, testable unit. Tiers 2 and 3 are additive and do not block the filtering win.

## Performance posture

Compile, do not interpret-from-text. The flow is: parse string to AST (once at config), compile AST to a delegate (once), invoke delegate per event. The Query evaluator already proves the parse-once/evaluate-many model (ExpressionLogFilter ctor parses; Allow walks). For 0.12.4 the AST tree-walk (20-80 ns/event, per the Query addon own measured note) is acceptable on the filter path. The AST nodes compile cleanly to a Func over LogEvent via System.Linq.Expressions as a Tier-1.1 hardening step ONLY if benchmarks demand it. Expression-trees are an AOT/trim hazard, so the companion package owns that risk, never Core, and the tree-walk stays the AOT-clean default. Regex builtins reuse the QueryEvaluator 200 ms catastrophic-backtracking timeout: non-negotiable, users supply these strings.

## The honest boundary that remains

Two genuine residuals, both small:

1. NameResolver / user-registered custom functions. Serilog lets callers inject StaticMemberNameResolver to add functions callable from the DSL. We can support the MECHANISM (a HeraldNameResolver hook), but a Serilog config that references a custom function by name only works if the consumer re-registers it against our resolver. Document as a one-line migration, not a wall.
2. Pre-compiled community sink binaries (unchanged). Out of scope by design: the identity wall we will not spoof.

Everything else in the four-part scope ports.

## Pre-mortem (the-fool, pre-mortem mode)

F1 (Semantic drift on @Level) Likelihood High, Impact High. Serilog @l is an enum (LogEventLevel); Herald is Level?.Key (string). A DSL @Level = Error works, but @Level >= Warning (ordinal severity compare) silently does the wrong thing: string compare, not severity rank. So a filter that LOOKS migrated drops the wrong events in production. Mitigation: bind @Level to a severity-rank-aware comparand, not raw string; pin with a regression test per level pair. Effort Med. This is the single most dangerous narrative. A wrong result is worse than a loud failure.

F2 (Grammar edge-case tail eats the timeline) Likelihood Med, Impact Med. like wildcard escaping, in with mixed-type lists, ci/cs collation modifiers, operator precedence corners. Just-port-the-grammar hides weeks. Mitigation: the upstream Apache-2.0 test suite IS the spec. Port Serilog own expression tests as the acceptance gate, scope 0.12.4 to the cases they cover, fail-loud on unrecognized syntax (never silent-no-op). Effort Low to adopt their tests.

F3 (Upstream-tracking maintenance burden) Likelihood Med, Impact Low. Serilog.Expressions ships new builtins/syntax; our port drifts. Mitigation: pin a stated upstream version in the package README (tracks Serilog.Expressions X.Y), treat their changelog as the diff source, accept lag as honest. A companion package that is 95% current beats none.

F4 (appsettings.json round-trip mismatch) Likelihood Med, Impact Med. Serilog config reads Filter/WriteTo blocks with expression/Name properties via the Serilog.Settings.Configuration convention. If our package is not discovered by the same Using/assembly-scan convention, a copied appsettings.json fails to bind. Mitigation: match the config-extension naming/assembly convention exactly; add a round-trip test that loads a real Serilog appsettings.json snippet unmodified. Defer full config integration to Tier 2 if it threatens the Tier-1 filtering ship date.

Inversion (what guarantees failure): (a) shipping silent-wrong results instead of loud failures (F1), guard with severity binding plus tests; (b) scoping to all-of-Serilog.Expressions instead of Tier 1, guard with the tier cut; (c) hand-writing acceptance tests instead of porting upstream. None exist yet; all three are designed out above.

## Coordination note (Jared)

Jared owns parser plus evaluator depth: the Superpower grammar for the expression language (precedence, like, in, ternary, function-call parsing) and the object-typed evaluator with Serilog coercion semantics. Richard owns the package boundary, the ILogFilter/ISerilogEventView seam wiring, the binding table, and the tier cut. The AST-record plus switch-evaluator handshake is the shared contract: same shape as QueryExpression/QueryEvaluator, new node set.
