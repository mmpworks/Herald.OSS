# Herald.OSS — Legal Disclaimers

**Reviewed and approved by counsel — AS-IS framing — 2026-06-02.**

This document is prepared by MMPWorks' in-house legal-documents advisor — who is **not a licensed
attorney**. It is prepared text, not legal advice in itself. A **licensed attorney has reviewed and
approved the AS-IS-restatement framing of the External Event Injection disclaimer (§2) on
2026-06-02**, and this document is now live and approved for production use. The companion lawyer
packet (`lawyer-packet-injection-disclaimer.md`) is the record of that review.

This is the single authoritative home for Herald.OSS legal items. Other surfaces (README, NOTICE,
the `AllowExternalEventInjection()` XML-doc) point here rather than restating the text, so the
canonical language lives in exactly one place.

---

## 1. The license — Apache 2.0, "AS IS", no warranty

Herald.OSS is distributed under the Apache License, Version 2.0. The full license text is in
[`LICENSE`](../../LICENSE) and is the controlling document. Two sections of that license carry the
warranty and liability position for the entire package:

- **Section 7 — Disclaimer of Warranty.** The Work is provided on an "AS IS" BASIS, WITHOUT
  WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
- **Section 8 — Limitation of Liability.** No contributor is liable for damages arising out of the
  use or inability to use the Work, to the extent permitted by applicable law.

Everything below sits **alongside** these two sections. Nothing below replaces them, narrows them,
or raises MMPWorks' liability above the "AS IS" floor they set. Where this document speaks to a
specific feature, it is restating that already-granted position for that feature and disclosing,
as a matter of fact, what that feature does.

---

## 2. External Event Injection — scope and what it bypasses

### 2.1 What this section covers

This section concerns **one specific opt-in capability in Herald.OSS**: the
`AllowExternalEventInjection()` builder switch and the external event injection path it enables. It
applies **only to Herald.OSS**. It does not apply to Herald Pro, Herald Enterprise, or Herald
Compliance, which never expose or honor this switch (see §2.5).

The technical mechanics are settled in `docs/design/external-event-injection-switch.md` (the
SETTLED ADR, Richard + Jared, 2026-06-02). Every factual statement here about what the switch does
and what it bypasses traces to that ADR.

### 2.2 Factual disclosure — the bypass (this part is a statement of fact)

This subsection states plainly what the injection path does. It is a disclosure of system
behavior, separate from the legal-allocation language in §2.3. Read it as documentation of fact.

**Default behavior.** Herald.OSS does not accept externally constructed (hand-built) log events on
its public injection port unless the application author explicitly enables that capability. By
default, an externally constructed event submitted through the public `ILogger.Log(LogEvent)` entry
point is dropped, and Herald emits a runtime notice identifying the call site and the protections
that were bypassed. A logging call never throws on this path.

**Enabling injection is a deliberate, named opt-in.** Calling `AllowExternalEventInjection()` on
the pipeline builder is an affirmative, deliberate act by the application author. It is the sole
supported way to enable external event injection in Herald.OSS. The method name states the action
in plain language at the point where it is enabled. No configuration default, transitive
dependency, or accidental code path enables this capability; it is enabled only where an author
writes that method call, or supplies the equivalent serialized configuration value the builder
produces.

**What the path bypasses.** An event injected through this path does **not** pass through Herald's
standard ingest pipeline. Specifically, an injected event bypasses:

1. **Redaction processing** — Herald's redaction/processor pass does not run on an injected event.
2. **Factory stamping** — time, scope, tenant, and other context fields the Herald event factory
   would normally stamp are not applied.
3. **Enrichment** — registered enrichers do not run on the injected event.
4. **Template rendering** — the standard message-template rendering pass does not run on the
   injected event.

(The bypassed protections are enumerated per ADR §4.1, §5.2, §8. Redaction is named first because
it carries the clearest data-safety consequence.)

**What that means for the application.** Because the redaction pass does not run, the application —
not Herald — is responsible for vetting the content of any event it injects. That responsibility
covers **any content the application is responsible for handling**. Personally identifiable
information, secrets, and credentials are **illustrative examples** of content the application must
vet on this path. This list is illustrative, not exhaustive, and is **not a warranted statement of
what Herald's redaction would otherwise process or detect**.

### 2.3 Legal allocation — the "AS IS" position, restated for this path

The text in this subsection is the legal-allocation language. It is structurally separate from the
factual disclosure in §2.2 so that the disclosure stands on its own regardless of how any
allocation language is ultimately read.

> **External Event Injection — "AS IS" for this path.**
>
> The external event injection path is part of the Work and is provided on the same "AS IS" basis,
> without warranties or conditions of any kind, as the rest of Herald.OSS under Apache License,
> Version 2.0, §7. When an application enables this path by calling
> `AllowExternalEventInjection()`, the standard ingest protections described in §2.2 — including
> redaction — do not run on events injected through it. The party that enables the path is the
> party that controls what enters it.
>
> To the maximum extent permitted by applicable law, MMPWorks LLC provides no warranty for events
> injected through this path and, consistent with Apache-2.0 §8, is not liable for any consequence
> arising from injected content, including the disclosure of unredacted sensitive data, incorrect
> or malformed event content, or any downstream processing, storage, or transmission of injected
> content. The application that enables the path bears the consequence of the content it injects,
> as a factual result of the protections that path bypasses.
>
> **Nothing in this section excludes or limits any liability that cannot be excluded or limited
> under applicable law.**
>
> **Severability.** If any provision of this section is held unenforceable, the remainder stays in
> effect, and the Apache-2.0 §7 "AS IS" disclaimer and §8 limitation of liability continue to apply
> in full to this path and to the rest of the Work.
>
> This position applies to the Herald.OSS `AllowExternalEventInjection()` capability only. It does
> not apply to Herald Pro, Herald Enterprise, or Herald Compliance.

### 2.4 The documented warning chain

A user who enables this path is warned through four independent surfaces, so the path is never
silent:

1. **Runtime notice** — when an externally constructed event is dropped on the default (un-opted-in)
   path, Herald emits a notice naming the call site and the protections that were bypassed.
2. **Analyzer diagnostic `HRLD0060`** — the analyzer flags use of the switch at build time.
3. **XML-doc on `AllowExternalEventInjection()`** — the method documentation points to this
   disclaimer in plain language, so the consent and the disclosure are read together at the call
   site.
4. **This document** — the factual disclosure (§2.2) and the "AS IS" restatement (§2.3).

**Trust boundary, stated.** Herald.OSS trusts the application author who explicitly enables the
switch to vet the content they inject. Herald enforces the default-off behavior in code; the
content-vetting responsibility on the opted-in path rests with the application, because Herald's
redaction pass does not run there by design.

### 2.5 The paid tiers are unaffected

This disclosure is scoped to the OSS opt-in because the behavior it describes does not exist in the
paid tiers. Per ADR §6:

- Herald Pro / Enterprise / Compliance compose an always-on enforced provenance gate
  (`GenSourceGatedSink` wrapping every sink) plus a commercial factory that stamps `GenSource` on
  every event. An unstamped, hand-built event is rejected by that gate regardless of any OSS switch
  setting, because the gate reads provenance, not the switch.
- The paid composition never reads the OSS consent flag. The ADR records this as a by-construction
  guarantee with a grep verification (§7.3). If `AllowExternalEventInjection()` were deleted, the
  paid composition would be byte-for-byte unchanged.

In the paid tiers, the enforced gate stays in force, the injection switch is not a way around it,
and the existing paid license terms (MMP.Licensing `LICENSE.txt` §5 No Warranty, §6 Limitation of
Liability, and the per-tier Product License Agreements) govern liability for those products.

---

## 3. Where this attaches

- **`LICENSE`** stays unmodified Apache-2.0. Nothing attaches there.
- **`NOTICE`** carries a short pointer paragraph to this document — not the full text.
- **`README.md`** carries a single link to this document under its License section.
- **`AllowExternalEventInjection()` XML-doc** points here in plain language (code change, tracked
  with the switch implementation).

The canonical text lives only in this file. Every other surface points here.

---

## 4. Attorney review — record

A licensed attorney reviewed this disclaimer and **approved the AS-IS-restatement framing** (the
core question A) on 2026-06-02. The companion lawyer packet
[`lawyer-packet-injection-disclaimer.md`](lawyer-packet-injection-disclaimer.md) is now the record
of that review rather than a pending ask.

The jurisdiction-dependent questions B through G in that packet (governing-law anchor, consumer-vs-
business enforceability, point-of-use conspicuousness, data-category naming, non-assenting-operator
privity, the optional MMP.Licensing scope line) were the open items routed to counsel. The framing
approval is confirmed; whether the sign-off individually covered B–G has not been separately
confirmed in-house. See the packet header for that open note. This document still does not state any
of those questions as settled law.
