# Request for feedback: async-handoff design space

Herald.OSS 0.10.2 ships a new default for the async sink. We landed
it after exploring twelve other shapes. The choice is open to
challenge, and this page is how we invite the challenge.

If you read this and see a better path, an angle we missed, or a
verdict we got wrong, we want the conversation. File an issue, open
a PR, or post the workload data. Details at the bottom.

## What we landed

The default is called **Lever A**. The producer thread fills a
value-typed envelope on its own stack, copies it into a channel, and
returns. The drain reads the envelope out and routes it to the inner
sink. No heap allocation on the producer side. When the inner sink is
kernel-eligible, the whole pipeline runs zero-allocation in steady
state.

The numbers, two regimes:

| Regime | Throughput | Bytes/event | Max pause |
|---|---|---|---|
| Oversubscribed (96 conn / 24 cores, flat out) | 78.7 M/s | 0.3 | 5.8 ms |
| Paced (24 conn × 100 KHz, mean of 3 reps) | pacing-locked | 51 | 9.59 ms |

The corresponding heap-path baseline today is 39.3 M/s at 296 B/event
at the oversubscribed regime, and 2.4 M/s at 343 B/event with a
21.81 ms mean max pause at the paced regime. Lever A wins on every
axis at oversubscription; at the paced regime, throughput is
identical, allocation is 6.7× lower, and the mean max-pause is 12.22
ms lower.

The full design-decision write-up, including the contract, the
honest residuals, and the M-5 contingency path, is at
[Herald.Documentation: Lever A is the default async handoff](https://github.com/mmpworks/Herald.Documentation/blob/main/prose/herald-oss/explanation/design-decisions/lever-a-async-default.md).

## Why we're asking

> 💡 **Quick picture.** An architect's sketchbook holds the buildings
> that did not get built. The one you walk into is the one that got
> selected, but the sketches record what was considered. They are
> proof the building was chosen against alternatives, not by default.
> This request-for-feedback is the cover page on the sketchbook. The
> default is Lever A. The other twelve sketches are catalogued so a
> reader can see what the choice cost and what the choice ruled out,
> and so the reader can find the sketch we missed.

Herald.OSS is open source. The team's reasoning today is not the
team's verdict for all time. We catalogued thirteen approaches with
honest evidence grades. Twelve of them are designed-only, projected,
or first-principles. Lever A is the only measured entry. The catalog
is the design-space map; the recommendation is one read of that map.

The community will know shapes we did not enumerate. It will have
production-workload data we do not have. It will spot a verdict we
overstated or an evidence grade we misclassified. Each of those is a
real contribution. The catalog moves with the evidence.

## What we considered: the twelve other shapes

Each entry has an evidence grade and a one-line verdict. The full
rationale lives in the [engineering catalog](https://github.com/mmpworks/Herald.Documentation/blob/main/prose/herald-oss/explanation/design-decisions/lever-a-async-default.md#read-next).

| # | Shape | Evidence | One-line verdict |
|---|---|---|---|
| 1 | SPSC Disruptor-proper ring per connection | Projected from analogous | 1.5–3× over `Channel<T>` in .NET, not the 10× JVM literature suggests |
| 2 | Off-heap unmanaged byte ring | Designed-only | Pays per-event serialisation to save channel allocation. Wrong trade in-process |
| 3 | Per-thread batched handoff | Designed-only | Reintroduces cross-tenant fairness inversion the per-connection-drain topology exists to prevent |
| 4 | Pre-render to bytes on producer | Designed-only | Requires re-architecting the sink interface for a tail-case win |
| 5 / M-2 | Kernel-inner-direct-to-sink bypass | Designed-only | Removes the latency firewall; operator footgun |
| 6 | Producer-side fixed-arena allocator | Designed-only | Cross-thread free coordination costs more than the allocation it saves |
| 7 | Native AOT-specific path | First-principles | No measurable AOT-specific win on this shape |
| 8 / O-1 | Ref-struct channel hybrids | First-principles | Possible only by redefining "channel" to break async decoupling |
| M-1 | Vyukov MPSC linked-list queue | Projected from analogous | Orthogonal to Lever A; could combine in a future revision |
| M-3-A | Pooled mutable `LogEvent` | Designed-only | Breaks public-API value-equality contract; gen-2 tenuring risk |
| M-3-B | Pooled mutable carrier, public `LogEvent` at sink boundary | Designed-only | Credible alternative; higher long-term pool-discipline cost than Lever A |
| M-4 | SPSC fan-in via single drainer | Designed-only | One-core ceiling; inverts load distribution |

The companion `IAsyncHandoff` seam (M-5) is the contingency, not an
alternative. It is the operator-pluggable interface the team commits
to landing if a workload surfaces that the default does not fit.

The line we held: we did not pre-measure every designed-only entry as
a precondition for shipping. That path leads to forever-pending
research. The catalog states each evidence grade honestly so a reader
can judge which entries deserve follow-up measurement.

## What we are honest about not having proven

Three residuals. Each one is documented in the design-decision page;
we restate them here because they are exactly where outside evidence
would help.

**The synthetic-workload disclaimer.** The harness uses a 4-property
template. Production workloads carry 8–15 properties with mixed
scalar, nested-object, and string-of-arbitrary-length content. The
296 B/event baseline understates production; the 0.3 B/event inline
result is approximate because overflow to heap arrays at arity > 8
is not exercised. The directional shape of the Lever-A-vs-heap win
holds across both, but the absolute numbers will move.

**The 100 KHz/connection design ceiling is a target, not a customer
rate.** Herald.OSS optimises for the headroom required by
scientific/manufacturing producers. We do not have a current
customer producing that rate today. If your deployment is at or
above 100 KHz/connection, we want the data, both for catalog
validation and for the M-5 contingency-trigger discussion.

**Three reps at 15 seconds do not prove a tail confidence interval.**
The paced-regime numbers characterise the directional shape of the
distribution. A 30-rep run and a 24-hour soak are queued; neither is
required to land the default change because the oversubscribed
regime is decisive on its own, but both are the right surfaces for
the tail claim with the rigor a production SRE will want.

## What we want feedback on

Four specific kinds. Anything else that is on-topic for the design
space is welcome too.

1. **Alternative approaches we missed.** The catalog has thirteen
   entries. If you see a shape the team did not enumerate, submit it
   with the same form (evidence grade, one-line verdict, tradeoff
   section) and we land it in the catalog. New entries credit the
   contributor by name.

2. **Measurements of the designed-only entries.** Anyone who can
   stand up a credible .NET SPSC Disruptor (entry #1), a Vyukov MPSC
   queue (entry M-1), or a pooled-carrier prototype (entry M-3-B)
   against the corrected harness and produce numbers, we will
   incorporate the result. The catalog entry moves up the
   evidence-grade column and credits the contributor.

3. **Pushback on the four hard constraints.** We held four
   constraints fixed: multi-connection mandatory, 100 KHz/connection
   design ceiling, per-connection drain topology, public-API
   discipline. If you have a reason to relax any of them, whether a
   workload we did not anticipate, a deployment model where
   multi-connection is wrong, or an operator profile we missed, open
   an issue, name the constraint, name the workload.

4. **Production-workload data that confirms or contradicts the
   cloud-native common-regime framing.** The catalog claims 4–10×
   core oversubscription is the default deployment shape for
   multi-tenant SaaS, not a tail case. If your production telemetry
   says otherwise, we want to see it.

## How to engage

- **GitHub issue with the `request-for-feedback` label** at the
  [Herald.OSS issue tracker](https://github.com/mmpworks/Herald.OSS/issues/new?labels=request-for-feedback),
  for discussion, workload data, or constraint pushback.
- **Pull request against the catalog** at the
  [Herald.Documentation repository](https://github.com/mmpworks/Herald.Documentation).
  The catalog lives in
  `prose/herald-oss/explanation/design-decisions/lever-a-async-default.md`
  alongside the structured record at
  `data/herald-oss/design-decisions/lever-a-async-default.json`.
  Both are PR-friendly. A PR proposing a new entry, contradicting an
  existing verdict, or moving an entry up the evidence-grade column
  with measurements is welcome.
- **Workload data.** If your data is non-public, contact us at
  [oss@mmpworks.com](mailto:oss@mmpworks.com) and we will work out
  the right shape for the contribution.

## Our standing posture

The safe baseline plus an in-product customization path is always
preferable to a fork. The M-5 seam is the customization path on this
specific decision. Operators whose service-level objective cannot
tolerate the paced-regime cost opt out through that seam when it
ships, not by forking the kernel.

If you find a structurally better answer, we want it in Herald.OSS,
not in your fork. The catalog is a living record of the design
space, not a frozen claim of completeness.

## Read next

- The design decision: [Lever A is the default async handoff](https://github.com/mmpworks/Herald.Documentation/blob/main/prose/herald-oss/explanation/design-decisions/lever-a-async-default.md).
  The prose version, with the contract, the residuals, and the
  community door open.
- The engineering catalog: same page, Read Next section. Jared's
  source-of-truth analysis with the per-entry rationale, the
  evidence-grade table, and the recommendation section.
- The structured record: [`lever-a-async-default.json`](https://github.com/mmpworks/Herald.Documentation/blob/main/data/herald-oss/design-decisions/lever-a-async-default.json).
  The schema-validated record, PR-friendly for proposing new
  entries or contradicting existing verdicts.
- The 0.10.2 changelog entry: [`CHANGELOG.md`](CHANGELOG.md). What
  shipped, what changed, what stayed.
