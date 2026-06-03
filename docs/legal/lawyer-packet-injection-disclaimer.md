# Lawyer Packet — Herald.OSS External Event Injection Disclaimer

**OUTCOME — counsel approved the AS-IS framing on 2026-06-02. This packet is now a RECORD of that
approved review, not a pending ask.**

> **What was approved.** A licensed attorney reviewed the disclaimer and approved the
> **AS-IS-restatement framing** — the structural choice to frame the injection clause as a
> restatement of the already-granted Apache-2.0 "AS IS" position plus a factual disclosure, rather
> than as a new responsibility-transfer term. That resolves **question A** below. `DISCLAIMERS.md`
> is now live and approved for production use.
>
> **Still to confirm (B–G).** The in-house advisor (not a licensed attorney) has explicit word only
> that the **framing (A)** was approved. The jurisdiction-specific questions **B through G** below
> were the open items in this packet. It has **not** been separately confirmed in-house that
> counsel's sign-off individually addressed each of B–G. **Steve: please confirm with counsel
> whether the sign-off covered B–G as well, or whether any of them remain open.** Do not treat B–G
> as resolved on in-house authority — only the framing (A) is confirmed approved.

This packet was prepared by MMPWorks' in-house legal-documents advisor, who is **not a licensed
attorney**. It is prepared text, not legal advice in itself. It was built to make the attorney visit
short: the document was already drafted, the known failure modes were already closed, and the
questions that genuinely needed a lawyer were isolated below so the hour was spent on those, not on
drafting.

This packet has three parts:

1. The clean document, ready to review (the content of `DISCLAIMERS.md`).
2. A one-page plain-language summary of what it does and why.
3. The attorney-only questions, labeled A through G.

---

## Part 1 — The clean document

The reviewable text is the consolidated [`DISCLAIMERS.md`](DISCLAIMERS.md) in this same folder. It
is a single authoritative copy; README and NOTICE point to it rather than restating it. The two
load-bearing structural moves in the draft, which the attorney should evaluate first:

- **It is framed as a restatement, not a new contract term.** The injection clause does not read as
  a fresh responsibility-transfer agreement. It restates the Apache-2.0 "AS IS" / no-warranty /
  limitation-of-liability position for the injection path specifically, and discloses, as a matter
  of fact, what that path bypasses. This rides the assent the user already gave by using the
  package under Apache-2.0, rather than trying to add a new term the license would not carry.

- **The factual disclosure and the legal allocation are structurally separated** (§2.2 vs §2.3 in
  the document). The factual bypass disclosure is just true and stands on its own. The legal
  allocation is set apart, carries a "cannot-be-excluded-under-law" carve-out, and carries a
  severability sentence, so that if any allocation language is struck, the disclosure and the
  Apache-2.0 floor survive intact.

---

## Part 2 — Plain-language summary

Here is the whole thing in plain terms, so the attorney can read it in two minutes and Steve knows
exactly what he is signing.

Herald.OSS is adding a switch. By default it is off. When it is off and someone tries to push a
hand-built log event into Herald, Herald drops the event and prints a notice saying what it
skipped. Turning the switch on takes one deliberate line of code, `AllowExternalEventInjection()`,
written by the application's own developer. Nothing turns it on by accident.

Once the switch is on, hand-built events skip Herald's normal safety passes. The big one is
redaction. Herald normally scrubs sensitive content. On this path it does not. So the application
that flipped the switch is the one responsible for checking what goes in.

That is the whole situation. The legal text does two jobs, and we kept them apart on purpose.

The first job is to state the facts. The switch exists, here is what it skips, here is what that
means for the application. That part is not really arguable. It is a description of how the software
behaves, and a court has nothing to strike because it is just true.

The second job is the liability piece, and we wrote it carefully. We did not write a brand-new
contract saying "you now accept all responsibility." That kind of bolt-on term is exactly what a
plaintiff's lawyer attacks, and it can also clash with the open-source license the user already
agreed to. Instead we restated the position the Apache-2.0 license already grants. The software is
provided as-is. On this path the safety passes do not run. The party who turned the path on controls
what enters it, and bears the result of that content as a plain consequence of the passes it skipped.

We added two protections so a single bad ruling cannot take the rest down. One line says nothing
here removes any liability that the law says cannot be removed. So if a court decides one piece is
void, that void piece does not poison everything around it. A second line, the severability line,
says if any part is struck, the rest stays, and the Apache-2.0 as-is and liability terms keep
applying in full.

We were deliberate about the sensitive-data wording too. The draft names PII, secrets, and
credentials, but only as examples of the broader thing the application has to check. It says in so
many words that the list is not complete and is not a promise about what Herald's redaction would
otherwise catch. That keeps a clever opponent from arguing we implied a guarantee we never made.

And the user gets warned in four places, not one. A runtime notice when an event is dropped. A
build-time analyzer warning, `HRLD0060`. The method's own documentation. And this disclaimers
document. So "nobody told me" is not an honest position for a developer who turned the switch on.

The paid Herald products are untouched. They have an always-on gate that rejects hand-built events
no matter what, and they never even read this OSS switch. The disclosure is scoped to the OSS
product where the switch actually lives.

What we could not finish, and what needs a real lawyer, is the handful of calls that turn on
jurisdiction and current law. Those are in Part 3. We did not guess at them, and we did not invent a
statute or a case to make them sound settled. They are the reason the visit exists.

---

## Part 3 — Attorney-only questions (A–G)

These are isolated on purpose. Each turns on jurisdiction, current case law, or a licensed judgment
call. None is stated as settled in the document. Please answer these; the rest of the document is
prepared work waiting for your stamp.

**A. Does the "AS IS restatement" framing survive Apache-2.0 §4? — APPROVED by counsel, 2026-06-02.**
Counsel approved the AS-IS-restatement framing. The remaining text of this question is retained as
the record of what was asked and answered.
The draft is built so the injection clause reads as a restatement of the already-granted
Apache-2.0 position plus a factual disclosure, not as a new added term on the Work. Apache-2.0 §4
permits stating additional or different terms for a distributor's own modifications but does not let
the Work-as-licensed carry added restrictions. Does our restatement framing hold? Or must the clause
shed the legal-allocation language in §2.3 entirely and stand only on the factual disclosure in
§2.2?

**B. Governing-law / venue anchor on an OSS document.**
The paid `LICENSE.txt` fixes Texas law and Williamson County venue. The OSS disclaimers document has
no governing-law anchor, because Apache-2.0 sets none. Does the OSS document need a governing-law /
venue line? If we add one, does that itself create tension with Apache-2.0's no-added-terms posture
on the Work? (This is the flip side of question A.)

**C. Consumer-vs-business waiver enforceability across MMPWorks' actual markets.**
MMPWorks is a Texas LLC and Herald.OSS ships worldwide via NuGet and GitHub. A clause allocating
risk to the party who enables a feature may be enforced differently against a business user than a
consumer, and differently across jurisdictions (some consumer-protection regimes restrict liability
waivers). Is the allocation in §2.3 enforceable across the markets Herald.OSS actually reaches? Is
the "to the maximum extent permitted by applicable law" hedge plus the cannot-be-excluded carve-out
sufficient, or is more jurisdiction-qualifying language needed?

**D. Is point-of-use / first-run conspicuousness advisable beyond the docs?**
The warning chain is runtime notice plus `HRLD0060` analyzer plus XML-doc plus this document. Is
that documented chain enough to defeat a failure-to-warn theory? Or is additional conspicuousness
advisable, for example surfacing the disclaimer at first run rather than only in documentation and
at the call site?

**E. Name specific data categories, or use pure "all content"?**
The draft names PII, secrets, and credentials as illustrative examples of a broader "any content the
application is responsible for vetting," and explicitly disclaims that the list is exhaustive or a
warranted statement of what redaction would catch. Is naming categories a net help (a specific,
concrete warning) or a net risk (an implied completeness)? Confirm the illustrative-not-exhaustive
framing is the right call, or advise pure "all content" language.

**F. Privity against a NON-ASSENTING downstream operator.**
This one was surfaced in the adversarial pass and is worth flagging directly. The developer who
writes `AllowExternalEventInjection()` has assented by using the package. But the operator who later
runs that application may never have agreed to anything from MMPWorks. The draft was deliberately
narrowed to bind and describe the party who acts (the developer who enables the switch) and to treat
the operator's exposure as a factual consequence rather than a term they "accepted." Does the
restatement framing reach a non-assenting downstream operator at all, and is the factual-consequence
treatment the correct and defensible posture against that party?

**G. The optional MMP.Licensing one-line scope clarification — yes or no?**
The paid `LICENSE.txt` already disclaims warranty and limits liability. Because the injection switch
is OSS-only and not honored in the paid tiers, the paid terms do not need an OSS-style
liability-shift clause. The only candidate addition is a single optional clarifying line stating that
the injection opt-in is a Herald.OSS capability and that the enforced provenance gate in the paid
products is not bypassable by it. This would be a scope clarification, not a liability shift. Should
that line be added to the paid terms, or left out? This touches paid customer-facing terms, so it is
your call, not an engineering call.

---

## What was decided in preparing this packet

- The single biggest change from the upstream draft: the clause was reframed from a
  responsibility-transfer term into an AS-IS-restatement plus factual disclosure, and the factual
  and legal pieces were structurally separated. This closes the strongest line of attack at once,
  but whether the restatement framing fully survives Apache-2.0 §4 is question A and remains the
  attorney's to confirm.
- No statute, case, or legal standard is asserted as settled anywhere in this packet or in
  `DISCLAIMERS.md`. The jurisdiction-dependent items are open questions only.
- Question F (the non-assenting downstream operator) was added beyond the upstream draft's list.
  The upstream draft bound "author and operator" together as parties who "accept" responsibility;
  that was softened to bind the acting party and describe the operator's exposure as a consequence,
  with the privity question routed to the attorney.
