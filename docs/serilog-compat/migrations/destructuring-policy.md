---
gap-id: destructuring-policy
serilog-surface: IDestructuringPolicy (Destructure.ByTransforming / Destructure.With)
herald-status: carries-over (tree-bridge; throws loud at registration if bridge unreachable)
population-rank: high
regression-test-id: G-SEC.1
---

<!-- Heather T-H2: STANDALONE companion. SECURITY-CRITICAL — must carry the
     redaction-must-fire callout. A no-op'd redaction policy is a PII regression. -->

# Migrating a Custom Destructuring Policy

## Security contract, first

<!-- If you register a policy it fires at projection time; if the bridge can't reach the
     value-model projector it throws at REGISTRATION, never silently no-ops. -->

## What you have in Serilog

## Path 1 — ByTransforming (the worked example)

## Path 2 — raw IDestructuringPolicy (tree bridge)

## Step-by-step

## Verify the redaction actually fires

<!-- Scan the FULL serialized event for the secret value, not just the property dict
     (ties G-SEC.1). A field-name check misses leak-into-other-field. -->

## Deep dive

<!-- Link worked-examples/S5-destructuring-policy.md. -->
