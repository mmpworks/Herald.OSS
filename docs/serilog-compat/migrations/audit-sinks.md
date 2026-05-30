---
gap-id: audit-sinks
serilog-surface: AuditTo vs WriteTo (sink-failure semantics)
herald-status: carries-over (auditMode bool on the sink adapter)
population-rank: medium
regression-test-id: G-SEC.2, G-SEC.3
---

<!-- Heather T-H2: STANDALONE companion. COMPLIANCE callout — AuditTo throws,
     WriteTo swallows. Silently swallowing an audit failure is the worst break. -->

# Migrating AuditTo / WriteTo Semantics

## The one difference that matters

<!-- WriteTo swallows sink failures (+ reports via SelfLog); AuditTo re-throws.
     auditMode:true on the adapter re-throws; default false swallows. -->

## What you have in Serilog

## What changes

## Step-by-step

## Verify the oppositional pair

<!-- Inject a throwing sink: WriteTo swallows, AuditTo propagates (G-SEC.2).
     Redaction runs BEFORE audit capture — secret absent from both outputs (G-SEC.3). -->
