# S5: Destructuring Policy Bridge

> ⚠️ **KNOWN GAP — HIGH/SECURITY (2026-06-01, Wave 1).** The guarantee below holds only on the
> `WriteTo.Sink(customSink)` mirror-projection path. On **native sinks** (`WriteTo.Console()`,
> `WriteTo.File(...)`) a registered destructuring policy is currently **bypassed** — a redaction
> policy that strips a secret is silently ignored and the secret reaches sink output. This violates
> the contract below. Reproduced + filed: `migrations/results/FINDING-destructure-native-sink-leak.md`;
> regression suite `REG-SERILOG-DESTRUCTURE-NATIVE-SINK`. Fix is queued for the release lane (not an
> overnight edit). Until it lands, treat native-sink redaction via a destructuring policy as unsafe.

## Security contract

A no-op destructuring policy is a PII leak. Herald never silently drops your
redaction work. Two guarantees hold:

1. If you register a policy, it fires at mirror-projection time. Secrets stripped
   by the policy never appear in , the rendered message, or
   any location SecretScanner can reach.

2. If the bridge cannot reach the P1 value-model projector, it throws at
   **registration** time, not at log-event time. You find out immediately.
   The exception message tells you what to use instead.

## The two paths

### Path 1:  (recommended for most redaction)

Maps a matching type through a projection lambda. The lambda returns an anonymous
type or DTO that carries only the approved fields. Everything else is excluded.



This path works regardless of the P1 projector seam. It is the recommended form
when the redaction requirement is straightforward field exclusion.

### Path 2:  (for multi-type or stateful policies)

Use this when the built-in projection form is not expressive enough:
a single policy that matches several related types, or one that inspects runtime
state the lambda form cannot see.



The policy receives a . Use it to construct child value nodes
so nested objects also go through the policy chain.

## Registration order

Policies are evaluated in registration order. First match wins.



## Cycle safety

A policy over a self-referential object does not cause a stack overflow.
The P1 projector (used by the value factory) detects cycles and emits a
 scalar in place of the recursive node. Your policy fires
before the walk starts, so if it matches, the walk does not happen at all.

## AsScalar

Render a type through  instead of destructuring it:



## OD-5 design note

The P1 seam audit confirmed that  is
accessible from the Serilog compat assembly (same assembly boundary, internal
visibility). The bridge takes the tree-bridge path: the user policy receives the
real factory, builds its tree, and the result is stored directly in the mirror
. The loud-throw guard in the bridge constructor is a
belt-and-braces safety net for future refactors.
