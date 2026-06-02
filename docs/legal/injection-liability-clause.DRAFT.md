# External Event Injection — Liability Disclaimer Clause

**DRAFT — pending attorney review.**
Authored by Max (build + licensing steward). This is the upstream draft for Dave's adversarial
pass and a licensed attorney's sign-off. It is **not legal advice** and is **not authorized for
production use** until counsel reviews it. Nothing here states a settled legal fact; the
jurisdiction-dependent items are flagged for the attorney in the final section.

Source of truth for the technical mechanics: `docs/design/external-event-injection-switch.md`
(SETTLED ADR, Richard + Jared, 2026-06-02). Every factual statement in the clause about what the
switch does and what it bypasses is traceable to that ADR — see the cross-reference notes inline.

---

## 1. Scope of this clause

This clause governs **one specific opt-in capability in Herald.OSS**: the
`AllowExternalEventInjection()` builder switch and the external event injection path it enables.
It is **additive** to the Apache License, Version 2.0 under which Herald.OSS is distributed. It
does not replace, override, or narrow the Apache-2.0 warranty disclaimer or limitation of
liability (see §4). It allocates responsibility for one deliberate, author-initiated action that
the Apache-2.0 text does not address by name.

This clause applies **only to Herald.OSS**. It does not apply to Herald Pro, Herald Enterprise,
or Herald Compliance, which never expose or honor this switch (see §3).

---

## 2. The clause text (DRAFT)

> ### External Event Injection — Opt-In and Liability Allocation
>
> **Default behavior.** Herald.OSS does not accept externally constructed (hand-built) log
> events on its public injection port unless the application author explicitly enables that
> capability. By default, an externally constructed event submitted through the public
> `ILogger.Log(LogEvent)` entry point is dropped, and Herald emits a runtime notice identifying
> the call site and the protections that were bypassed. A logging call never throws on this path.
>
> **Enabling injection is a deliberate opt-in.** Calling `AllowExternalEventInjection()` on the
> pipeline builder is an affirmative, deliberate act by the application author. It is the sole
> supported way to enable external event injection in Herald.OSS. The method name states the
> action in plain language at the point where it is enabled. No configuration default, transitive
> dependency, or accidental code path enables this capability; it is enabled only where an author
> writes that method call (or supplies the equivalent serialized configuration value the builder
> produces).
>
> **What enabling injection bypasses.** An event injected through this path does **not** pass
> through Herald's standard ingest pipeline. Specifically, an injected event bypasses:
>
> 1. **Redaction processing** — Herald's redaction/processor pass does not run on an injected
>    event. The application, not Herald, is responsible for ensuring an injected event contains no
>    unredacted secret, credential, or personally identifiable information.
> 2. **Factory stamping** — time, scope, tenant, and other context fields the Herald event factory
>    would normally stamp are not applied.
> 3. **Enrichment** — registered enrichers do not run on the injected event.
> 4. **Template rendering** — the standard message-template rendering pass does not run on the
>    injected event.
>
> (The bypassed protections are enumerated per the ADR §4.1, §5.2, §8. Redaction is named first
> and explicitly because it is the protection with the clearest data-safety consequence.)
>
> **Allocation of responsibility and liability.** By enabling external event injection, the
> application author and the operator of the application accept full responsibility for every
> event submitted on that path, including its content, its accuracy, its handling of sensitive
> data, and all downstream consequences of that content reaching configured sinks. Responsibility
> for vetting injected events transfers to the party that enabled the switch. To the maximum
> extent permitted by applicable law, MMPWorks LLC disclaims all liability for externally injected
> events and for any consequence arising from them, including but not limited to disclosure of
> unredacted sensitive data, incorrect or malformed event content, and any downstream processing,
> storage, or transmission of injected content. This allocation is in addition to, and does not
> limit, the "AS IS" warranty disclaimer and the limitation of liability provided under the
> Apache License, Version 2.0.
>
> **Scope.** This allocation applies to the Herald.OSS `AllowExternalEventInjection()` capability
> only. It does not apply to Herald Pro, Herald Enterprise, or Herald Compliance, in which the
> provenance gate is always enforced and this switch is not a consideration.

---

## 3. The paid tiers are unaffected — why, in plain terms

The disclaimer is **scoped to the OSS opt-in** because the footgun it covers does not exist in
the paid tiers. Per the ADR §6:

- Herald Pro / Enterprise / Compliance compose an **always-on enforced provenance gate**
  (`GenSourceGatedSink` wrapping every sink) plus a commercial factory that stamps `GenSource` on
  every event. An unstamped, hand-built event is rejected by that gate **regardless of any OSS
  switch setting**, because the gate reads provenance, not the switch.
- The paid composition **never reads the OSS consent flag.** The ADR records this as a
  by-construction guarantee with an open one-line grep verification (§7.3). If
  `AllowExternalEventInjection()` were deleted, the paid composition would be byte-for-byte
  unchanged.

So the liability shift is an **OSS-only** allocation. In the paid tiers, MMPWorks's enforced gate
stays in force, the injection switch is not exposed as a way around it, and the existing paid
license terms (MMP.Licensing `LICENSE.txt` §5 No Warranty, §6 Limitation of Liability, and the
per-tier Product License Agreements) already govern liability for those products. This clause
must **not** introduce an OSS-flavored liability shift into the paid terms — doing so could be read
to imply the paid gate is bypassable, which it is not. **Attorney note for Dave:** confirm the
paid `LICENSE.txt` needs no matching line, only a one-line non-applicability marker if any
(see §5, attach point D).

---

## 4. Interaction with Apache 2.0 — additive, not overriding

Herald.OSS is distributed under the Apache License, Version 2.0. Apache-2.0 already provides:

- **§7 Disclaimer of Warranty** — the Work is provided "AS IS", without warranties or conditions
  of any kind.
- **§8 Limitation of Liability** — no contributor is liable for damages arising from use of the
  Work, to the extent permitted by applicable law.

This injection clause sits **alongside** those provisions and is **additive**:

- It does **not** restate, narrow, or attempt to override Apache-2.0 §7–§8. Apache-2.0 forbids
  adding restrictions to the licensed Work itself; this clause does not restrict the Work — it
  allocates responsibility for a **specific runtime action the application author chooses to
  take** (enabling a named opt-in), which is a different thing from a license restriction on
  redistributing or using the code.
- Where Apache-2.0 §7–§8 disclaim warranty and liability **generally**, this clause makes the
  allocation **specific and named** for the one capability whose data-safety consequence is most
  acute (redaction bypass). The general disclaimer and the specific allocation reinforce each
  other; they do not conflict.
- Apache-2.0's "AS IS" position is the **floor**. This clause does not raise MMPWorks's liability
  above that floor; it confirms that for injected content the responsibility rests with the party
  that opted in.

**Attorney note for Dave:** Apache-2.0 §4 lets a distributor "state additional or different
license terms for your modifications" but the Work-as-licensed cannot carry added restrictions.
The defensible framing is that this clause is **a notice and a responsibility allocation, not a
license restriction on the Apache-2.0 Work.** Confirm that framing holds, and confirm the clause
lives in a NOTICE-style / disclaimers document rather than being injected into the `LICENSE` file
itself (which should remain the unmodified Apache-2.0 text).

---

## 5. Attach points — what Dave assembles (do not create the consolidated doc here)

Max's lane is the clause text and where it lands. Dave assembles the consolidated doc and the
README link. Here is the surface list:

**A. Herald.OSS consolidated legal/disclaimers doc (Dave creates).**
- Path suggestion: `docs/legal/DISCLAIMERS.md` (sibling to this draft).
- Must contain: the clause text from §2 above, the paid-tier non-applicability note from §3, and
  the Apache-2.0 interaction note from §4. Heading the doc with the same "additive to Apache-2.0"
  framing keeps it from reading as an attempt to relicense.
- README link: a single line under a "License" or "Legal" section pointing to the disclaimers
  doc. Max does **not** edit the README in this pass (per directive); Dave wires the link when the
  consolidated doc exists.

**B. Herald.OSS `NOTICE` file (license-adjacent, append-only).**
- The existing `NOTICE` carries the Apache-2.0 banner and third-party attributions. A short
  pointer paragraph is appropriate here — **not** the full clause. Suggested shape: one paragraph
  stating that Herald.OSS includes an opt-in external event injection capability whose use shifts
  event-vetting responsibility to the application, and pointing to `docs/legal/DISCLAIMERS.md`.
- Do **not** put the full liability language in `NOTICE`; keep `NOTICE` to attribution + pointer
  so the canonical clause lives in exactly one place (DRY for legal text — one authoritative copy,
  everything else points to it).
- The `LICENSE` file itself stays **unmodified Apache-2.0**. Nothing attaches there.

**C. The `AllowExternalEventInjection()` method XML-doc (code surface — Max's lane, separate task).**
- Per ADR §5.2 and §7.5, the method's XML-doc must point to this clause in plain language so the
  consent and the disclaimer are read together at the call site. This is a **code change tracked
  separately** from the legal-doc assembly (it lands when the switch is implemented — implementation
  is a separate Steve go per the ADR). Listed here so Dave knows the code-side pointer is accounted
  for and not a gap.

**D. `MMP.Licensing` terms — matching line assessment.**
- The paid `LICENSE.txt` already disclaims warranty (§5) and limits liability (§6) for the paid
  package. Because the injection switch is **OSS-only and not honored in paid**, the paid terms do
  **not** need an OSS-style liability-shift clause.
- The only candidate addition is a **single clarifying line** (optional, attorney's call) stating
  that the external event injection opt-in is a Herald.OSS capability and that the enforced
  provenance gate in the Paid Herald Products is not bypassable by it. This would be a
  **scope-clarification**, not a liability shift. **Flagged to Dave + attorney as a yes/no
  decision, not a Max decision** — it touches paid customer-facing terms (Steve/legal lane), not
  build or engine internals.

---

## 6. Jurisdiction and enforceability flags — Dave / attorney questions, not settled facts

Max is not a lawyer. The following turn on jurisdiction and on the legal character of a liability
shift, and are explicitly **open questions for Dave's adversarial pass and the attorney's
sign-off**, not statements of law:

1. **TX LLC, ships worldwide.** MMPWorks is a Texas LLC; Herald.OSS is distributed globally via
   NuGet and GitHub. The paid `LICENSE.txt` fixes Texas law + Williamson County venue (§8.1). The
   OSS disclaimers doc has **no such governing-law anchor** because Apache-2.0 sets none. **Question
   for attorney:** does the OSS disclaimer need a governing-law/venue line, and if so, does adding
   one to an Apache-2.0-licensed distribution create any tension with Apache-2.0's no-added-terms
   posture on the Work?

2. **Consumer vs. business enforceability of a liability shift.** A clause that shifts liability to
   the party enabling a feature may be enforced differently against a business user than against a
   consumer, and differently across jurisdictions (some consumer-protection regimes limit
   liability waivers). **Question for attorney:** is the "responsibility transfers to the enabling
   party" language enforceable across the markets Herald.OSS reaches, and does it need
   jurisdiction-qualifying language ("to the maximum extent permitted by applicable law" is
   already in the draft as a hedge — confirm it is sufficient)?

3. **"Additive to Apache-2.0" framing.** §4 argues this is a responsibility allocation/notice, not
   an added restriction on the Apache-2.0 Work. **Question for attorney:** does that framing hold,
   or does any part of the clause read as an added term on the Work that Apache-2.0 §4 would
   disallow? If the latter, the clause may need to be reframed purely as a notice of behavior +
   an "AS IS for this path" restatement rather than a responsibility-transfer.

4. **Defensibility / "the user was not warned."** Per the security-due-diligence standard the ADR
   cites (§5.2), the goal is that "the user was not warned" is not a defensible position against a
   malfeasance claim. The runtime notice + analyzer + XML-doc + this clause together form the
   warning chain. **Question for attorney:** is the documented warning chain sufficient to defeat a
   failure-to-warn theory, or is additional conspicuousness (e.g., the clause surfaced at first
   run, not only in docs) advisable?

5. **Naming "PII" and "secrets" in the clause.** The draft names redaction-of-PII/secrets as the
   acute risk. **Question for attorney:** is naming specific data categories (PII, credentials) a
   help (specific warning) or a risk (implying a completeness of categories that is not intended)?
   Confirm the "including but not limited to" hedge covers this.

---

## 7. Handoff summary for Dave

- The clause text to red-team is §2.
- The attach points to assemble are §5 (A–D).
- The Apache-2.0 interaction argument to stress-test is §4.
- The jurisdiction/enforceability questions to route to the attorney are §6.
- Everything here is **DRAFT — pending attorney review.** Nothing is pushed; no README edit was
  made; the consolidated `DISCLAIMERS.md` is yours to create.
