# P3 — Serilog Output-Template Grammar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render Serilog's output-template grammar (`{Timestamp:HH:mm:ss}`, `{Level:u3}`/`{Level:w}`, `{Message:lj}`, `{Properties}`, `{NewLine}`, `{Exception}`, alignment/format specifiers) to Serilog's output shape; ship the two built-in `ITextFormatter`s (message-template text formatter + `CompactJsonFormatter`/`RenderedCompactJsonFormatter` = CLEF) mirrored over Herald's CLEF/JSON writers; and wire the **S3 seam** — `WriteTo.Console(ITextFormatter)` accepting a user formatter through a `TextWriter` bridge over Herald's string-returning `ILogFormatter`.

**Why this is v1 (Steve decision):** the output-template grammar is a common config shape and **degrades silently to wrong output if missing** — a Serilog user who configured `outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"` gets *plausible-looking but wrong* console lines on Herald, with no error. That silent-wrong-output failure is what this plan closes. The-fool #3 and Dissent D-3 (design-round-richard.md) flag it as higher-population than its earlier v1-optional status implied; Steve promoted it to v1.

**Architecture:** Herald already ships an `OutputTemplateFormatter` (`native/dotnet/Formatting/OutputTemplateFormatter.cs`) with a **Herald-native** token grammar — it does *not* understand Serilog's `:u3`/`:w` level casing, `:lj` literal-JSON message rendering, `{p}`-style alignment, or per-token alignment/format specifiers. **We do not extend that formatter in place** (its token set is part of Herald's own surface and its `{Properties}`/`{Context}` semantics differ from Serilog's). Instead P3 adds a *parallel, compat-only* `SerilogOutputTemplate` parser + renderer in the compat assembly (`MMP.Herald.Serilog`), reusing Herald's `MessageTemplateRenderer`, `JsonEscaper`, and `Utf8JsonFormatter` building blocks for the heavy lifting. The two built-in `ITextFormatter`s and the `ITextFormatter`→`ILogFormatter` bridge live alongside it. **CUPID/DRY:** the renderer is a *translator* onto Herald's existing rendering primitives, not a re-implementation of message rendering; the CLEF formatters wrap Herald's CLEF writer rather than hand-rolling JSON.

**Tech Stack:** C# / net9 + net10 (compat assembly target; Herald.OSS core multi-targets), xUnit (`tests/Herald.OSS.Tests.csproj`), `bash build.sh`. Parity oracle: a real-Serilog reference (`Serilog` + `Serilog.Formatting.Compact` NuGet, Layer-1 coexistence only — Layer 2 cannot coexist with real Serilog in the same graph).

**Plan-only:** this document specifies tasks; no product source is written here. P0 (levels) is assumed landed and P1 (value-model mirror) assumed to exist — the formatter receives the **mirrored** `Serilog.Events.LogEvent` (the "flat-fast, tree-on-demand" wrapper from design-round-richard.md, *value-model* section). See **Cross-plan types** below.

---

## Cross-plan types (FLAGGED — owned elsewhere, consumed here)

| Type | Owner plan | How P3 uses it | If absent |
|---|---|---|---|
| `Serilog.Events.LogEvent` (mirrored, flat-fast/tree-on-demand) | **P1** | The formatter's `Format(LogEvent, TextWriter)` input. `{Message:lj}` reads `MessageTemplate` + tree `Properties`; `{Properties}` walks the tree-projected property dictionary; `{Exception}` reads `Exception`. | P3 is blocked on its renderer tasks. Mitigation: Task 1 defines a **narrow read-only adapter interface** (`ISerilogEventView`) the renderer consumes, so renderer tasks proceed against a test double until P1's concrete mirror lands; the bridge to the real mirror is a one-file swap. |
| `Serilog.Formatting.ITextFormatter` (mirrored interface, `void Format(LogEvent, TextWriter)`) | **P1 / Layer-2 mirror** | The public seam type for S3 and the return shape of the two built-ins. | P3 declares the **Layer-1** `MMP.Herald.Serilog.Formatting.ITextFormatter` itself (it is a behaviour-free interface; declaring it here is not a DRY violation). The Layer-2 `Serilog.Formatting.ITextFormatter` mirror forwards to it and is owned by the Layer-2 plan. Task 0 confirms which plan landed the interface and avoids a double-declare. |
| `LogPropertyCaptureMode.Destructure/Stringify` mapping | **P2** (`{@}`/`{$}` template lowering) | The renderer does **not** re-parse `{@}`/`{$}` — those are message-template concerns resolved upstream. P3's grammar is the *output* template (`{Level:u3}` etc.), a distinct grammar. Flagged only to assert the boundary so no one conflates them. | No impact — P3 owns only the output-template grammar. |

**No new cross-plan type is introduced by P3.** `ISerilogEventView` (Task 1) is a P3-internal seam, deleted or kept as the P1 adapter once the mirror lands (decided in Task 1 / Open decision OD-1).

---

## Scope boundary — S3 seam vs the grammar (do NOT conflate)

Per seam-inventory.md S3 and the PRD: the **grammar** (output-template token language) is the v1 deliverable; the **S3 seam** (`WriteTo.Console(ITextFormatter)` accepting a *user-supplied* formatter) is the cheap mechanical bridge. They share the `ITextFormatter` shape but are separate deliverables:

- **Grammar (Tasks 2–6):** parse + render Serilog's output-template string to its output shape. This is the silent-wrong-output closer.
- **Built-in formatters (Tasks 7–8):** the message-template text formatter (the grammar wrapped as an `ITextFormatter`) and the CLEF pair (`CompactJsonFormatter`/`RenderedCompactJsonFormatter`), mirrored over Herald's CLEF writer.
- **S3 bridge (Task 9):** `WriteTo.Console(ITextFormatter)` routes a user formatter into Herald's console sink via a `TextWriter`→`ILogFormatter` adapter. A user `ITextFormatter` compiles and runs; its `Format(evt, writer)` output reaches the console byte-for-byte.

---

### Task 0: the-fool pre-mortem gate (no product code)

**Files:**
- Create: `docs/serilog-compat/plans/P3-grammar-premortem.md`

- [ ] **Step 1: Run the pre-mortem.** Invoke `Skill(the-fool)` framed as: *"Herald already ships an `OutputTemplateFormatter` with a Herald-native token grammar. We are adding a parallel Serilog grammar in the compat layer. Where does a user silently get wrong output — a template that parses without error but renders differently from real Serilog? Which specifiers (`:u3`, `:w`, `:lj`, alignment `,-N`, `{Properties}` filtering of already-rendered holes) are most likely half-implemented and invisible in a happy-path test?"* Capture each silent-divergence mode.

- [ ] **Step 2: Confirm the two-formatter trap.** Specifically have the-fool check: a user who points `outputTemplate` at the *wrong* formatter (Herald's native vs the Serilog one) — does the wiring pick the Serilog grammar for Serilog-configured sinks? Capture the dispatch risk (ties Task 9 + Open decision OD-2).

- [ ] **Step 3: Write the risk list** to `P3-grammar-premortem.md` — each risk + which Task below mitigates it. Any risk without a mitigating task means a task is missing. Note especially: `{Level:u3}` casing table, `:lj` JSON-vs-literal branch, alignment sign/width edge cases, `{Properties}` must exclude properties already named in the template (Serilog behaviour), `{Exception}` trailing-newline rule.

- [ ] **Step 4: Commit**

```bash
git add docs/serilog-compat/plans/P3-grammar-premortem.md
git commit -m "docs(serilog-compat): the-fool pre-mortem on the output-template grammar"
```

---

### Task 1: Parity oracle harness + `ISerilogEventView` seam (fixtures first)

Echo's "build the cross-cutting fixtures first" rule. The **real-Serilog parity oracle** is the load-bearing fixture for every grammar test — without it, "matches Serilog" is an assertion against our own guess.

**Files:**
- Read first: `docs/serilog-compat/test-inventory.md` (oracle harness description), `native/dotnet/Formatting/OutputTemplateFormatter.cs` (the native grammar we are *not* extending), design-round-richard.md value-model section (mirror shape).
- Create: `tests/TestSupport/Serilog/SerilogParityOracle.cs` — drives the **real** `Serilog` package (Layer-1 coexistence): given an output template + a canonical event spec, returns real Serilog's rendered string.
- Create: `tests/TestSupport/Serilog/CanonicalEventSpec.cs` — a test-only event description (timestamp, level, message template, property name/value/capture-mode tuples, optional exception) that both real Serilog *and* the Herald renderer can be fed, so the same input drives both.
- Create: `src/Addons/Serilog/Formatting/ISerilogEventView.cs` (compat assembly) — narrow read-only view the renderer consumes (`Timestamp`, `Level`, `MessageTemplate`, rendered `Message`, ordered `Properties` as name→value, `Exception?`). **This is the P1 decoupling seam** (see Cross-plan table).

- [ ] **Step 1: Stand up the oracle.** Add the real `Serilog` + `Serilog.Formatting.Compact` package refs to the **test project only** (never the product compat assembly — the product mirrors types, it does not reference real Serilog). Implement `SerilogParityOracle.Render(template, CanonicalEventSpec)` using `Serilog.Formatting.Display.MessageTemplateTextFormatter`.

- [ ] **Step 2: Define `ISerilogEventView`** and a test double `FakeSerilogEventView` (in `tests/TestSupport/Serilog/`) so renderer tasks (2–6) run before P1's concrete mirror exists.

- [ ] **Step 3: Smoke test the oracle itself** — one trivial template (`"{Message}"`) through real Serilog returns the rendered message. This proves the oracle wiring before it is trusted as truth.

```bash
cd E:/dev/Herald.OSS && dotnet test tests/Herald.OSS.Tests.csproj --filter "FullyQualifiedName~SerilogParityOracle" -v minimal
```

- [ ] **Step 4: Commit.**

```bash
git add tests/TestSupport/Serilog src/Addons/Serilog/Formatting/ISerilogEventView.cs
git commit -m "test(serilog-compat): real-Serilog parity oracle + ISerilogEventView seam (P1 decoupler)"
```

---

### Task 2: Output-template parser — tokens + specifiers (G-GAP.6 escaping/positional)

Parse the Serilog output-template string into a token list: literal text, escaped `{{`/`}}`, and **property tokens** carrying name, optional alignment (`,N` / `,-N`), and optional format specifier (`:fmt`). This is a *different* parser from Herald's native `OutputTemplateFormatter.Parse` — Serilog allows alignment *and* format on the same token (`{Level,-5:u3}`), which the native parser does not model.

**Files:**
- Create: `src/Addons/Serilog/Formatting/SerilogOutputTemplateToken.cs` — record hierarchy: `Text(string)`, `Hole(string Name, int Alignment, string? Format)` (Alignment 0 = none; positive = right-pad, negative = left-pad, matching Serilog).
- Create: `src/Addons/Serilog/Formatting/SerilogOutputTemplateParser.cs`.
- Test: `tests/Output/Serilog/SerilogOutputTemplateParserTests.cs`

- [ ] **Step 1: Write the failing parse table** (G-GAP.6 — `{{`/`}}` escaping + positional/named holes, plus alignment/format split):

```csharp
[Theory]
// template, expected token shape (encoded as a readable assertion in the test body)
[InlineData("{Message}",                "Hole(Message,0,null)")]
[InlineData("[{Level:u3}]",             "Text([) Hole(Level,0,u3) Text(])")]
[InlineData("{Level,-5:w}",             "Hole(Level,-5,w)")]
[InlineData("{Timestamp:HH:mm:ss}",     "Hole(Timestamp,0,HH:mm:ss)")]   // colon INSIDE the format must NOT split
[InlineData("{{literal}}",              "Text({literal})")]
[InlineData("{Properties:j}",           "Hole(Properties,0,j)")]
public void Parses_holes_alignment_and_format(string template, string expected) { /* ... */ }
```

The `{Timestamp:HH:mm:ss}` case is the trap: the colon inside the time format must not be treated as the name/format delimiter. Serilog splits on the **first** `:` only, and alignment (`,`) binds before format (`:`).

- [ ] **Step 2: Run — expect FAIL** (parser undefined).

- [ ] **Step 3: Implement the parser.** Split each hole on the first `,` (alignment) then first `:` (format); honour `{{`/`}}` escaping exactly as Serilog does. Keep it a pure function (string → `IReadOnlyList<SerilogOutputTemplateToken>`), no allocation concerns in the parse path (parsed once per formatter construction, like the native one).

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```bash
git add src/Addons/Serilog/Formatting/SerilogOutputTemplateToken.cs src/Addons/Serilog/Formatting/SerilogOutputTemplateParser.cs tests/Output/Serilog/SerilogOutputTemplateParserTests.cs
git commit -m "feat(serilog-compat): output-template parser (holes, alignment, format)"
```

---

### Task 3: `{Level}` specifiers — `:u3`/`:w`/`:t` casing + width (parity table)

Serilog's level token has its own specifier family: `u`=upper, `w`=lower, `t`=titlecase (default), with an optional moniker width (`u3` = uppercase 3-char abbreviation: `INF`, `WRN`, `ERR`, `FTL`, `VRB`, `DBG`). This is the single most common output-template specifier and the most likely to silently mis-render. Maps over Herald's **renamed** (P0) level display names.

**Files:**
- Create: `src/Addons/Serilog/Formatting/SerilogLevelMoniker.cs` — the casing + abbreviation table, keyed on Herald's post-P0 level (Verbose/Debug/Information/Warning/Error/Fatal) + the four Herald extras (Notice/Success/Security/Metric — Serilog has no abbreviation for these; define a deterministic 3-char fallback and pin it).
- Test: `tests/Output/Serilog/SerilogLevelMonikerTests.cs`

- [ ] **Step 1: Write the failing parity table** against the oracle:

```csharp
[Theory]
[InlineData("information", "u3", "INF")]
[InlineData("warning",     "u3", "WRN")]
[InlineData("error",       "u3", "ERR")]
[InlineData("fatal",       "u3", "FTL")]
[InlineData("verbose",     "u3", "VRB")]
[InlineData("debug",       "u3", "DBG")]
[InlineData("information", "w",  "information")]  // lowercase, full
[InlineData("information", "u",  "INFORMATION")]  // uppercase, full
[InlineData("information", null, "Information")]  // default titlecase
public void Level_moniker_matches_serilog(string levelKey, string? spec, string expected) { /* assert against SerilogLevelMoniker AND SerilogParityOracle */ }
```

Cross-check **each row against the oracle** so the abbreviation table is Serilog's, not ours.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement `SerilogLevelMoniker.Render(levelKey, spec)`.** For the four Herald-extra levels (no Serilog equivalent), pin a deterministic fallback and document it as a *named gap* (Serilog never emits these, so there is no oracle row — this is a Herald superset, pin it so it can't drift).

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```bash
git add src/Addons/Serilog/Formatting/SerilogLevelMoniker.cs tests/Output/Serilog/SerilogLevelMonikerTests.cs
git commit -m "feat(serilog-compat): {Level:u3}/:w/:t moniker rendering (oracle-pinned)"
```

---

### Task 4: `{Message:lj}` + `{Timestamp:fmt}` + `{NewLine}` + `{Exception}` token rendering

The remaining built-in tokens. `:l` = literal (string values rendered without quotes), `:j` = JSON (values rendered as JSON), `lj` = literal message + JSON values — the canonical Serilog message-rendering specifier. Reuse Herald's `MessageTemplateRenderer` for the message body; reuse `JsonEscaper`/`Utf8JsonFormatter` value-rendering for the `:j` branch.

**Files:**
- Create: `src/Addons/Serilog/Formatting/SerilogTokenRenderers.cs` — one renderer per built-in token (Message, Timestamp, NewLine, Exception, Properties is Task 5).
- Test: `tests/Output/Serilog/SerilogTokenRenderTests.cs`

- [ ] **Step 1: Write the failing parity tests** (each row vs oracle):

```csharp
[Theory]
[InlineData("{Message:lj}",          /* event with string + int props */)]  // literal text, JSON-shaped values
[InlineData("{Message}",             /* default render */)]
[InlineData("{Timestamp:HH:mm:ss}",  /* time-only */)]
[InlineData("{Timestamp:o}",         /* round-trip */)]
[InlineData("{NewLine}",             /* Environment.NewLine */)]
[InlineData("{Exception}",           /* exception present: type+message+stack, trailing newline rule */)]
[InlineData("{Exception}",           /* exception ABSENT: renders nothing, not "null" */)]
public void Token_render_matches_serilog(string template, /* CanonicalEventSpec */) { /* ... */ }
```

The `{Exception}`-absent row is a known silent-divergence trap (the native formatter renders nothing too, but Serilog's exact spacing/newline differs — pin it).

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement the renderers.** `{Message}` delegates to `MessageTemplateRenderer.Render` (DRY — do not re-render); the `:j`/`:l` value formatting reuses Herald's JSON value escaper. `{Timestamp:fmt}` uses `CultureInfo.InvariantCulture` (Serilog default) — confirm against the oracle that Herald's `TimeUtc` and Serilog's `Timestamp` agree on UTC-vs-local (Serilog defaults to local `DateTimeOffset`; **pin the chosen convention** and surface it as Open decision OD-3).

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```bash
git add src/Addons/Serilog/Formatting/SerilogTokenRenderers.cs tests/Output/Serilog/SerilogTokenRenderTests.cs
git commit -m "feat(serilog-compat): {Message:lj}/{Timestamp}/{NewLine}/{Exception} render (oracle-pinned)"
```

---

### Task 5: `{Properties}` — residual-property rendering (the subtle one)

Serilog's `{Properties}` token renders **only the properties NOT already named in the output template AND not in the message template** (the residual set), as a `{ key: value }` blob with optional `:j` JSON shaping. A naive implementation that dumps *all* properties double-renders the ones already shown — a common, invisible-on-happy-path divergence the-fool flags in Task 0.

**Files:**
- Modify: `src/Addons/Serilog/Formatting/SerilogTokenRenderers.cs` (add `{Properties}`).
- Create: `src/Addons/Serilog/Formatting/ResidualPropertySelector.cs` — computes the residual set given the output-template hole names + the message-template hole names.
- Test: `tests/Output/Serilog/SerilogPropertiesTokenTests.cs`

- [ ] **Step 1: Write the failing residual test** (the load-bearing case — vs oracle):

```csharp
[Fact]
public void Properties_excludes_holes_already_in_template_and_message()
{
    // message "User {UserId} did {Action}", template "[{Level}] {Message} {Properties}",
    // event also carries RequestId + Elapsed (not in message, not in template).
    // EXPECTED: {Properties} renders ONLY { RequestId, Elapsed } — NOT UserId/Action.
    // Assert against SerilogParityOracle.
}

[Fact]
public void Properties_j_renders_json_object() { /* {Properties:j} vs oracle */ }
```

- [ ] **Step 2: Run — expect FAIL** (naive dump shows all props).

- [ ] **Step 3: Implement `ResidualPropertySelector`** — set-difference of event property names minus (output-template hole names ∪ message-template hole names). Render the residual as Serilog's structure-value blob; `:j` switches to JSON object shape (reuse the value escaper).

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```bash
git add src/Addons/Serilog/Formatting/ResidualPropertySelector.cs src/Addons/Serilog/Formatting/SerilogTokenRenderers.cs tests/Output/Serilog/SerilogPropertiesTokenTests.cs
git commit -m "feat(serilog-compat): {Properties} residual-set rendering (oracle-pinned)"
```

---

### Task 6: The renderer — assemble tokens + alignment, end-to-end template parity (G-GAP.1)

Tie the parser + per-token renderers together with **alignment** (pad/truncate to the token's width, sign = direction) into the full `SerilogOutputTemplateRenderer`, and pin the canonical real-world templates against the oracle. This is **G-GAP.1** — the named-gap regression that closes the silent-wrong-output hole.

**Files:**
- Create: `src/Addons/Serilog/Formatting/SerilogOutputTemplateRenderer.cs` — `void Render(ISerilogEventView, TextWriter)`; parses once at construction (like the native formatter), renders per event.
- Create: `src/Addons/Serilog/Formatting/SerilogAlignment.cs` — apply `,N`/`,-N` padding to a rendered token segment (Serilog pads; it does **not** truncate — confirm vs oracle and pin).
- Test: `tests/Output/Serilog/SerilogOutputTemplateParityTests.cs` (G-GAP.1)

- [ ] **Step 1: Write the failing G-GAP.1 parity suite** — the representative real templates:

```csharp
[Theory]
[InlineData("[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")]  // the canonical console template
[InlineData("{Timestamp:o} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}")]
[InlineData("{Level,-11}|{Message}")]  // left-pad alignment
[InlineData("{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")]
public void Full_template_matches_serilog(string template)
{
    foreach (var spec in CanonicalEventSpec.RepresentativeCorpus())   // info+warn+error, with/without exception, with residual props
    {
        var ours    = RenderWithHerald(template, spec);
        var serilog = SerilogParityOracle.Render(template, spec);
        Assert.Equal(serilog, ours);   // EXACT string equality — this is the silent-divergence guard
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (renderer not assembled).

- [ ] **Step 3: Implement the renderer + alignment.** Iterate tokens; for a `Hole`, render the segment via the right per-token renderer, then apply alignment. Use a pooled `StringBuilder`/`TextWriter` consistent with Herald's existing formatters (`StringBuilderPool`). Keep cognitive complexity low — dispatch on token name through a small dictionary, mirroring the native formatter's dispatch-table pattern.

- [ ] **Step 4: Run — expect PASS.** Any residual diff is a real divergence — fix the renderer, not the assertion (never relax to substring/contains; exact equality is the contract).

- [ ] **Step 5: Commit.**

```bash
git add src/Addons/Serilog/Formatting/SerilogOutputTemplateRenderer.cs src/Addons/Serilog/Formatting/SerilogAlignment.cs tests/Output/Serilog/SerilogOutputTemplateParityTests.cs
git commit -m "feat(serilog-compat): output-template renderer + alignment; G-GAP.1 oracle parity"
```

---

### Task 7: Built-in text `ITextFormatter` (the grammar wrapped) + the bridge

Expose the renderer as Serilog's built-in `MessageTemplateTextFormatter`-shaped `ITextFormatter` (`void Format(LogEvent, TextWriter)`), and define the `ITextFormatter`→Herald `ILogFormatter` bridge so a formatter can drive Herald's string-returning sink path.

**Files:**
- Create: `src/Addons/Serilog/Formatting/ITextFormatter.cs` (Layer-1 interface — confirm not already declared by P1 per Task 0).
- Create: `src/Addons/Serilog/Formatting/MessageTemplateTextFormatter.cs` — ctor takes the output-template string, wraps `SerilogOutputTemplateRenderer`.
- Create: `src/Addons/Serilog/Formatting/TextFormatterLogFormatterBridge.cs` — adapts `ITextFormatter` to Herald's `MMP.Herald.Formatting.ILogFormatter` by rendering into a pooled `StringWriter` and returning the string (`Format(LogEvent) => { var sw = ...; _formatter.Format(Map(evt), sw); return sw.ToString(); }`). `Map(evt)` projects Herald's native `LogEvent`/`LogEventBuffer` to the mirrored `Serilog.Events.LogEvent` (P1) — on the **legacy/heap path only**, never the kernel hot path (Guard 1).
- Test: `tests/Output/Serilog/MessageTemplateTextFormatterTests.cs`

- [ ] **Step 1: Write the failing test** — `new MessageTemplateTextFormatter(template).Format(evt, writer)` produces the same string as Task 6's renderer (and the oracle), and the bridge returns that string from `ILogFormatter.Format`.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement** the formatter + bridge. The bridge uses Herald's `StringBuilderPool`/pooled writer to avoid a per-event allocation beyond the returned string.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```bash
git add src/Addons/Serilog/Formatting/ITextFormatter.cs src/Addons/Serilog/Formatting/MessageTemplateTextFormatter.cs src/Addons/Serilog/Formatting/TextFormatterLogFormatterBridge.cs tests/Output/Serilog/MessageTemplateTextFormatterTests.cs
git commit -m "feat(serilog-compat): MessageTemplateTextFormatter + ITextFormatter->ILogFormatter bridge"
```

---

### Task 8: CLEF formatters — `CompactJsonFormatter` + `RenderedCompactJsonFormatter` (G-GAP.5 byte-parity)

Serilog's `Serilog.Formatting.Compact` emits CLEF (Compact Log Event Format): `{"@t":...,"@mt":...,"@l":...,"@x":...,"@i":..., <props>}`. `CompactJsonFormatter` emits `@mt` (template); `RenderedCompactJsonFormatter` emits `@m` (rendered message). Mirror over Herald's CLEF writer.

**Files:**
- Read first: `native/dotnet/Formatting/Utf8JsonFormatter.cs`, `native/dotnet/Formatting/JsonFormatter.cs`, `src/Services/JsonEscaper.cs` — the CLEF write primitives to reuse. **Confirm whether a Herald CLEF writer already exists**; if not, the CLEF field-emitter is built here over `Utf8JsonFormatter`'s primitives (do NOT hand-roll a second JSON escaper — DRY).
- Create: `src/Addons/Serilog/Formatting/CompactJsonFormatter.cs`, `src/Addons/Serilog/Formatting/RenderedCompactJsonFormatter.cs`.
- Test: `tests/Output/Serilog/CompactJsonFormatterParityTests.cs` (G-GAP.5)

- [ ] **Step 1: Write the failing byte-parity suite** vs the real `Serilog.Formatting.Compact` oracle:

```csharp
[Theory]
[MemberData(nameof(CanonicalEventSpec.RepresentativeCorpus))]
public void Clef_bytes_match_serilog_compact(CanonicalEventSpec spec)
{
    var ours    = FormatWith<CompactJsonFormatter>(spec);          // @mt path
    var serilog = SerilogCompactOracle.Render(spec, rendered:false);
    AssertJsonEqual(serilog, ours);   // semantic JSON equality (key order-tolerant per CLEF spec) — pin the @-field set + @t format
}

[Theory]
[MemberData(nameof(CanonicalEventSpec.RepresentativeCorpus))]
public void RenderedClef_bytes_match_serilog(CanonicalEventSpec spec) { /* @m path */ }
```

Use **JSON-semantic** equality keyed on the CLEF `@`-field contract (`@t`,`@mt`/`@m`,`@l`,`@x`,`@i` + properties), because raw byte order across two writers is not guaranteed identical — the *CLEF contract* is the parity target, not literal bytes. (The PRD says "CLEF byte-parity"; resolve to **CLEF-spec field-and-value parity** — Open decision OD-4, flagged to Richard.)

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement both formatters** over Herald's JSON write primitives. `@l` is omitted for `Information` (Serilog convention — Information is the default level and is not emitted); pin that. Reuse the P1 mirror's tree projection for `{@}`-destructured properties so structured values land as nested JSON, not `ToString()`.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```bash
git add src/Addons/Serilog/Formatting/CompactJsonFormatter.cs src/Addons/Serilog/Formatting/RenderedCompactJsonFormatter.cs tests/Output/Serilog/CompactJsonFormatterParityTests.cs
git commit -m "feat(serilog-compat): CompactJsonFormatter/RenderedCompactJsonFormatter (CLEF); G-GAP.5 parity"
```

---

### Task 9: S3 seam — `WriteTo.Console(ITextFormatter)` end-to-end

Wire the seam: the `WriteTo.Console(ITextFormatter)` compat verb routes a **user-supplied** `ITextFormatter` into Herald's console sink via the Task 7 bridge. This is the S3 deliverable — a custom formatter compiles and its output reaches the console.

**Files:**
- Read first: `native/dotnet/Routing/Providers/ConsoleSinkProvider.cs`, the `WriteTo.Console` compat verb (created by the Layer-1 builder plan — confirm its location; if not yet present, this task adds the `ITextFormatter` overload to the verb surface and FLAGS the dependency).
- Modify/Create: the `WriteTo.Console` verb overload accepting `ITextFormatter` (+ the default `outputTemplate` overload that constructs a `MessageTemplateTextFormatter`).
- Test: `tests/Output/Serilog/WriteToConsoleFormatterTests.cs` (ties G-CORPUS.4 custom-formatter-compile-and-run)

- [ ] **Step 1: Write the failing test** — a user `ITextFormatter` (`class MyFormatter : ITextFormatter { void Format(LogEvent e, TextWriter w) => w.Write("CUSTOM:" + e.RenderMessage()); }`) wired via `WriteTo.Console(new MyFormatter())` produces `CUSTOM:...` on the captured console output. Plus: `WriteTo.Console(outputTemplate: "[{Level:u3}] {Message:lj}")` produces the grammar-rendered line.

```csharp
[Fact]
public void Custom_text_formatter_reaches_console()
{
    var captured = new StringWriter();
    var logger = BuildLoggerWithConsole(new MyFormatter(), captured);   // routes through the bridge + ConsoleSinkProvider
    logger.Information("hello {X}", 1);
    Assert.Contains("CUSTOM:hello 1", captured.ToString());
}
```

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement the verb overload** — construct the bridge from the user formatter, hand it to the console sink as an `ILogFormatter`. The default-template overload constructs a `MessageTemplateTextFormatter`. Dispatch must pick the **Serilog** grammar for Serilog-configured sinks (the-fool Task 0 Step 2 / OD-2).

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```bash
git add <verb file> tests/Output/Serilog/WriteToConsoleFormatterTests.cs
git commit -m "feat(serilog-compat): WriteTo.Console(ITextFormatter) S3 seam over the bridge"
```

---

### Task 10: Hot-path guard — grammar/CLEF on the native path stays 0-alloc (G-HOT.1 tie-in)

The grammar + bridge + mirror are **opt-in** (only when a user configures a Serilog `outputTemplate` or a custom `ITextFormatter`). Prove they cost nothing on the Herald-native sink path (Guard 1 + Guard 2 from the PRD, scoped to P3's surface).

**Files:**
- Test: `tests/AOT/Serilog/SerilogFormattingArchitectureTests.cs` (Guard 1, structural)
- Bench: `benchmarking/library/net10/` — add a row (or reference P1's mirror row) proving the native console path's alloc is unchanged when the Serilog formatting assembly is referenced-and-loaded.

- [ ] **Step 1: Architecture test (Guard 1).** Assert no kernel/native-sink assembly references the Serilog formatting types — the grammar lives only in the compat assembly. (Reuses the P1/P0 architecture-test pattern; here it pins the *formatting* boundary.)

- [ ] **Step 2: Alloc row (Guard 2).** Add/extend a net10 BenchmarkDotNet row: native `WriteTo.Console` (Herald grammar) with the Serilog formatting assembly loaded == same bytes/op as without it. The Serilog grammar path is exercised separately and its (non-zero, documented) cost is attributed to the opt-in consumer, never the hot path.

```bash
cd E:/dev/Herald.OSS && bash build.sh --release --bench-filter "*SerilogFormatting*" 2>&1 | tail -20
```

- [ ] **Step 3: AOT-clean check (G-GAP.7 tie-in).** The compat formatting assembly publishes with no new trim/AOT warnings.

```bash
dotnet test tests/AOT/Herald.OSS.Aot.Tests.csproj -v minimal 2>&1 | tail -10
```

- [ ] **Step 4: Commit.**

```bash
git add tests/AOT/Serilog benchmarking/library/net10
git commit -m "test(serilog-compat): hot-path 0-alloc + AOT guards for the grammar/CLEF surface"
```

---

### Task 11: Named-gap pins + full build close

Pin the silent-divergence boundaries as regression tests (every-gap-becomes-a-test), then close the wave.

**Files:**
- Test: `tests/Output/Serilog/SerilogGrammarGapPinTests.cs`

- [ ] **Step 1: Pin the documented boundaries** — Herald-extra levels' moniker fallback (no Serilog oracle row), the UTC-vs-local timestamp convention (OD-3), the CLEF `@l`-omitted-for-Information rule, and an *unknown specifier* (e.g. `{Level:zzz}`) rendering Serilog-identically (or, if it can't, a pinned-known-divergence with a doc link). Each pin is a `[Fact]` with a comment naming the gap.

- [ ] **Step 2: Full solution build + test.**

```bash
cd E:/dev/Herald.OSS && bash build.sh --all --test 2>&1 | tail -20
```
Expected: green.

- [ ] **Step 3: Grep guard** — the native `OutputTemplateFormatter` was **not** modified (P3 is additive; the native grammar is untouched).

```bash
git diff --name-only main...HEAD -- native/dotnet/Formatting/OutputTemplateFormatter.cs && echo "MODIFIED — investigate" || echo "untouched (expected)"
```

- [ ] **Step 4: Final commit + note P3 done.**

```bash
git add -A docs/serilog-compat tests/Output/Serilog
git commit -m "chore(serilog-compat): P3 output-template grammar complete — G-GAP.1 + G-GAP.5 closed"
```

---

## Self-review notes

- **Spec coverage:** P3 closes G-GAP.1 (output-template grammar parity), G-GAP.5 (CLEF parity), G-GAP.6 (escaping/positional, folded into Task 2), and ties G-GAP.7 (AOT) + G-HOT.1 (hot-path) for the formatting surface. It delivers the grammar (Tasks 2–6), both built-in `ITextFormatter`s (Tasks 7–8), and the S3 seam (Task 9), per scope-prd v1 + seam-inventory S3.
- **Additive, not in-place:** the existing native `OutputTemplateFormatter` is **not** extended — its Herald token grammar differs from Serilog's. P3 adds a parallel compat grammar that reuses Herald's `MessageTemplateRenderer`/`JsonEscaper`/`Utf8JsonFormatter` primitives (DRY: translate onto existing rendering, don't re-implement).
- **Parity oracle is the truth source** (Task 1, built first) — every grammar/CLEF assertion is exact-equality vs real Serilog (Layer-1 coexistence), never our own guess. The silent-wrong-output failure mode is closed only because the oracle is the gate.
- **Cross-plan dependency on P1** (the mirrored `Serilog.Events.LogEvent`) is decoupled via `ISerilogEventView` (Task 1) so renderer tasks proceed before the mirror lands.

## Open decisions (flag to Richard/Steve before execution)

- **OD-1:** Keep `ISerilogEventView` as the permanent P1-mirror adapter, or delete it once P1 lands and consume the mirror directly? (Affects whether the renderer couples to the concrete mirror.)
- **OD-2:** Sink-formatter dispatch — how does the wiring pick the **Serilog** grammar vs Herald's native `OutputTemplateFormatter` for a given sink? (the-fool Task 0 Step 2.)
- **OD-3:** `{Timestamp}` convention — Serilog defaults to **local** `DateTimeOffset`; Herald stores `TimeUtc`. Pin UTC, or honour local to match Serilog byte-for-byte? (Affects G-GAP.1 exactness.)
- **OD-4:** "CLEF byte-parity" (PRD) resolved as **CLEF-spec field/value parity** (key-order-tolerant), since two independent JSON writers won't guarantee identical byte order. Confirm acceptable.
