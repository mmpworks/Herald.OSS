# Unboxing arbitrary value types in LogPropertyCompact

Status: design / pre-prototype (targets 0.12.5)
Authors: Richard (architecture) + Jared (low-level store + InlineArray sizing)
Date: 2026-06-01

## The problem

LogPropertyCompact (src/Pipeline/Kernel/LogPropertyCompact.cs) is 32 bytes:

    string Name      8
    LogPropertyKind  1   (CaptureMode rides one of the 6 free padding bytes)
    CaptureMode      1
                     6
    long ScalarBits  8
    object? RefValue 8
    total           32   (8-byte aligned)

The six hot primitives (int, long, double, bool, DateTime, string) ride
ScalarBits / RefValue with no box. Any other value type -- Guid, decimal,
DateTimeOffset, TimeSpan, a consumer Money / Vector3 -- falls through From<T>
to the legacy (string, object) constructor and boxes once (~24 B GC-tracked)
at the call boundary.

Size matters because it multiplies: LogPropertyBuffer16 = 16 x 32 = 512 B on
the stack. Any growth in LogPropertyCompact is paid by all 16 slots, including
the int/long/string slots that never needed the room.

## Where the box actually costs, and where it does not

This is the fact the rest of the design turns on.

- The only consumer that reads the unboxed slot is the compact fast path in
  Utf8JsonFormatter (native/dotnet/Formatting/Utf8JsonFormatter.cs, ~line 157),
  which switches on Kind and writes typed JSON tokens (WriteNumber,
  WriteBoolean) directly. Its default arm renders any non-specialized kind via
  RefValue.ToString() -- that is where a boxed decimal/Guid lands today.
- Every other sink (MessagePackLogFormatter.WriteValue(object), the JSON
  formatter full-record path, OTLP, anything reading LogProperty.Value) consumes
  the inflated LogProperty, whose Value is object. Those paths box regardless of
  what we do here -- ToLogProperty() already materialises the box for them.

So the win is narrow and real: eliminate the box on the typed fast path that
ends at Utf8JsonFormatter. We are not eliminating boxing universally; we are
extending the no-box fast path to cover more value types before it hands off.

## The honest constraint (red-teamed via the-fool)

You cannot have all three at once. Pick two:

- typed fidelity + no struct growth -> must keep boxing arbitrary types (status quo)
- no struct growth + arbitrary types -> write-through to TEXT, loses typed value at the sink
- typed fidelity + arbitrary types -> tagged inline payload, GROWS the struct, capped at payload width, boxes above the cap

Arbitrary + unboxed + free + typed is an overclaim. The defensible answer is
tiered, not one mechanism.

## Approaches evaluated

### A. Widen the specialized set (decimal / Guid / DateTimeOffset / TimeSpan)

Add Unsafe.As arms for the four known 8-16-byte structs.

- TimeSpan (8 B) fits ScalarBits today -- free, do this regardless.
- Guid / decimal / DateTimeOffset are 16 B. They need a second 8-byte slot ->
  struct 32->40 B -> Buffer16 512->640 B (+25% stack).
- AOT: clean. Pure JIT specialization of From<T>, same shape as the existing
  arms. Formatter gets four new case arms with typed writers
  (WriteNumber(decimal), WriteStringValue(Guid)).
- Fidelity: perfect -- decimal renders as a JSON number, Guid as its canonical
  string, both with the right type tag for OTLP/MessagePack once those paths
  learn the kinds.
- Limit: covers known BCL types only, not consumer structs.

### B. Tagged inline union (general, capped)

Replace long ScalarBits with an [InlineArray(16)] byte payload + the existing
Kind tag. Value types up to 16 B bit-cast into the payload; bigger ones and
reference types fall to RefValue.

- Struct 32->48 B (16 payload + tag + Name + RefValue, aligned). Buffer16 -> 768 B.
- This is approach A generalised -- it IS the storage A needs for its 16-byte
  types, plus a path for small consumer structs.
- Caps at 16 B. Vector3 (12) fits; Matrix4x4 (64) boxes. So arbitrary is really
  <=16-byte registered structs; above the cap it silently falls back.
- Rendering dispatch for consumer structs is the hard part -- see C.

### C. Registration + source-gen specialization (Steve register an odd object)

[HeraldValue] on a consumer struct (or RegisterValueType<TStruct>()) drives the
generator (generators/TypedArgsOverloadGenerator.cs family) to emit:

1. a From<Money> arm that bit-casts Money into the inline payload (needs B
   storage), tagged with a generated Kind value, and
2. a render delegate / generated case the formatter dispatches to.

- Unboxed end-to-end ONLY if sizeof(TStruct) <= payload (B cap). This is the
  subtle break: the InlineArray buffer holds one concrete LogPropertyCompact; a
  registered struct rides unboxed only through B inline bytes. Above the cap it
  boxes -- registration does not change that.
- AOT: the codegen path is clean (compile-time, no reflection). A runtime
  RegisterValueType with a render delegate is also AOT-fine (delegates are
  AOT-clean); a runtime registry keyed on Type with reflection-driven rendering
  is NOT -- keep it delegate-based.
- Ergonomics: one attribute, consumer Money/Vector3 logs unboxed and renders
  correctly. Best API story, most moving parts.

### D. ISpanFormattable write-through (general, lossy)

For any ISpanFormattable value type, format to a pooled/stack buffer on the
producer thread (allocation-before-pipeline is allowed) and store the resulting
string in RefValue, tagged String.

- No struct growth, works for ANY ISpanFormattable with zero registration.
- Loses the typed value. decimal -> 3.14 as a quoted string, not numeric. That
  breaks numeric aggregation in Splunk/Loki/ES and discards OTLP typed AnyValue.
  Acceptable for Guid (JSON-string anyway); wrong for decimal.
- Cost: a string allocation (a reference, not a value box) + format work.
- Verdict: a fine opt-in fallback for types we do not specialize and that have
  no numeric meaning. Never the default for numeric structs.

### E. IUtf8SpanFormattable write-through (0-string variant)

Same as D but format UTF-8 bytes into an inline buffer -- 0 string alloc. Same
fidelity loss as D. Strictly better than D on allocation, same on fidelity. Only
worth it if D proves to be the chosen fallback.

## Ranking

payoff x feasibility x AOT-cleanliness x ergonomics:

1. A (widen specialized set) + the free TimeSpan arm -- highest payoff-to-risk.
   Covers the 4 BCL types people actually log, perfect fidelity, trivial AOT
   story. Costs the 32->40 B growth, the price of typed fidelity for 16-byte
   types. Prototype this.
2. C (registration via source-gen), built on B storage -- the answer to Steve
   literal ask. Ship it after A proves the inline-payload widening, because C
   depends on B storage existing. Prototype second, gated on A landing.
3. D/E (write-through) -- keep as the explicit, opt-in, documented-lossy fallback
   for un-registered non-numeric structs. Not a default.
4. B alone -- not a product on its own; it is the storage substrate A and C
   share. Do not ship it as a user-facing thing.

## Recommendation

Two-phase, and reframe the thesis honestly.

Phase 1 (0.12.5 prototype) -- A. Widen the inline payload once (long ScalarBits
-> a 16-byte inline region; Jared owns the exact layout and the Buffer16
stack-budget call) and add specialized arms + formatter cases for TimeSpan,
Guid, decimal, DateTimeOffset. Measure the Buffer16 stack delta and the
typed-path ns against the claims-pack. If the +25% stack costs hot-path ns we do
not like, fall back to an 8-byte-only widening (TimeSpan free, the three 16-byte
types stay boxed) and ship just TimeSpan.

Phase 2 (later, gated on Phase 1) -- C. [HeraldValue] source-gen for <=16-byte
consumer structs, reusing Phase 1 inline region. Document the size cap and the
above-cap box plainly. Delegate-based render registration, never reflection.

Do not ship D/E as a default. Offer it as [HeraldValue(WriteThrough = true)] for
consumers who knowingly want the text form of an oversized or non-numeric struct.

## Decisions for Steve

1. Is the +25% Buffer16 stack (512->640/768 B) acceptable to win typed fidelity
   for decimal/Guid/DateTimeOffset? Or ship TimeSpan-only (free) and leave the
   16-byte types boxed?
2. For consumer structs above 16 B (Matrix4x4), is silent box-fallback
   acceptable, or do we want a build warning (a new HRLD analyzer) telling the
   consumer their registered struct is too big for the unboxed path?
3. Is write-through-as-opt-in (lossy text) a fallback we want to offer at all, or
   do we keep the surface clean and just box un-registered structs?

## Trade-off summary (the one-liner)

We can move the box off the GC heap for <=16-byte value types by paying stack
size (A/C) -- typed fidelity preserved. For arbitrary-sized or text-only structs
we can avoid the box entirely by storing rendered text (D/E) -- at the cost of
the live typed value at structured sinks. There is no option that is
simultaneously zero-growth, arbitrary-size, and typed-faithful; the design ships
A now and C next, and is honest about the cap.

## Paid-tier friction removed

This work started as an OSS perf item. The fair question is what it buys the paid
tiers -- Pro (durable buffer / WAL), Enterprise (audit chains), TesseraSeal
(provenance / sealing) -- since those tiers deal heavily in the exact value types
this targets (Guid, decimal, DateTimeOffset). The honest answer is narrower than
"infrastructure the paid tiers stand on," and the narrowing matters because the
paid teams will plan against this doc.

The claim was red-teamed (the-fool, falsification mode). Three of its four legs
overclaimed. What survives is below, stated at the strength the evidence supports.

### The equivocation to avoid: schema-density is not hot-path boxing

Audit, seal, and durable records ARE dense in Guid/decimal/DateTimeOffset as data.
That is true and uninteresting on its own. It does not follow that the paid hot
path BOXES on those fields today.

The counter-example that settles it is in our own tree: CorrelationIdEnricher
(src/Enrichers/CorrelationIdEnricher.cs) emits the correlation ID as a STRING --
`Guid.NewGuid().ToString("N")` -- not a Guid value type. Strings do not box. So the
single most-cited "every event carries a correlation Guid" example is untouched by
this work, because at the property layer it was never a Guid.

The unbox fires only when the APPLICATION (or a paid enricher) logs a *typed*
Guid / decimal / DateTimeOffset arg through the typed-args fast path. So the
correct framing is conditional: *when a paid customer logs typed value-type args,
the box is removed*. Whether their deployment does that is an empirical question we
have not measured -- not an assumed property of the tier.

### Pro (durable buffer / WAL): real but I/O-dominated

Every event hits the WAL write-before-ack. If an event carries a typed
Guid/decimal/DateTimeOffset field, eliminating that box removes a ~24 B gen0
allocation per such field, per event, at the durable buffer's sustained rate.

Be honest about the magnitude. The WAL's defining cost is fsync + serialization +
replay bookkeeping. A gen0 box is noise against a disk write -- plausibly sub-1% of
the durable per-event cost. The typed16 OSS soak (250 kHz, 0.01 MB/min allocated,
zero gen0/1/2 collections over the run) measures the in-memory null-sink path. It
does NOT measure a durable path -- the durable path's bottleneck is exactly the
fsync the soak omits. Do not transfer "zero GC at 250 kHz" onto the WAL.

Friction removed: on the typed-arg fast path, the durable buffer stops adding
gen0 pressure on the identifier/amount/timestamp fields it is built around. Over an
8h+ run that is a flatter GC profile and fewer gen0 cycles competing with the WAL
writer for CPU. It is a compounding tax cut on the highest-duty-cycle path, not a
structural dependency. Pro works fine without it; it allocates a little more.

### Enterprise (audit chains): same shape, same caveat

Audit records are dense in event/actor/correlation IDs + timestamps + (in financial
audit) decimal amounts. *When those arrive as typed args*, the audit hot path goes
0-alloc on them through the JSON fast path -- subject to fact (b): the box is only
eliminated on the typed path ending at Utf8JsonFormatter. An audit deployment that
materializes the full LogProperty record, or serializes through MessagePack/OTLP,
re-boxes at ToLogProperty() regardless. So the win is bounded to
JSON-fast-path-only audit events, and only for fields the application typed.

### TesseraSeal (provenance / sealing): the win is consistency, not hash speed

The instinct "sealing is the most ID-dense workload, so unboxing helps most there"
fails on the mechanism. A hash eats BYTES. SHA-256 of a Guid's 16 bytes and SHA-256
of its canonical UTF-8 string are both perfectly valid tamper-evident seals. The
seal needs CANONICAL, reproducible bytes -- not a typed value. So "typed fidelity
feeds the hash" is false as stated, and unboxing buys the seal no hashing speed.

There IS a real seal-specific point, but it is a fidelity argument, not a GC one:
approach A keeps the SEALED representation equal to the STRUCTURED-SINK
representation. A typed decimal seals as a JSON number and queries downstream as a
number -- one canonical form. The lossy write-through path (D/E) would seal decimal
as a quoted string while a numeric-aware sink renders it as a number -- now the
sealed bytes and the queryable bytes diverge, and a verifier re-deriving canonical
form faces two candidate representations. That is a real tamper-evidence hazard.

So the seal recommendation is precise: **approach A is the correct DEFAULT for
sealed fields, and the lossy D/E fallback must be opt-OUT there**, never silently
applied to a field that gets sealed. This protects representational consistency
between seal input and sink output. It is an argument against lossy-stringifying
sealed numeric fields -- not an argument that the heap box matters to the hash.

### The one genuine enabler: [HeraldValue]

Everything above is a tax cut. The part that actually carries weight is
the registration seam (approach C). If a paid customer's domain type -- a Money
struct in a financial audit, a SealRef / actor-ID struct in provenance -- needs to
ride unboxed end-to-end and render typed, [HeraldValue] is the ONLY path to it.
That capability does not exist otherwise, and it is the piece the paid tiers can
build domain types on. It is gated behind Phase 2, depends on Phase 1 storage, and
caps at 16 bytes (above the cap, it boxes -- registration does not change that).

### Net

Not "infrastructure the paid tiers stand on because they are built on boxing types."
The defensible version:

1. A compounding per-event allocation cut that pays its largest dividend on the
   paid tiers' high-sustained-volume duty cycle -- conditional on the application
   logging typed value-type args, and bounded to the JSON fast path.
2. One genuine new capability -- [HeraldValue] -- that the paid tiers can build
   domain types (Money, SealRef) on, which otherwise does not exist.
3. For TesseraSeal: approach A is the right default for sealed fields because it
   keeps sealed bytes consistent with sink bytes; the lossy D/E path must be
   opt-out for any sealed field. This is canonicalization fidelity, not GC.

Stop claiming: that the paid hot paths box on these fields today (unverified -- the
one visible built-in emits a string); that typed fidelity feeds the seal's hash
(false -- hashes eat canonical bytes); that the zero-GC soak characterizes the
durable/audit/seal path (it is the null-sink OSS path, which omits the fsync and
serialization that dominate those tiers).
