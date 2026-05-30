---
gap-id: third-party-sinks
serilog-surface: pre-compiled community sinks (Seq / MSSql / Datadog / long tail)
herald-status: hard-wall (strong-name identity; no drop-in path)
population-rank: high
regression-test-id: G-SINK-WALL.1
---

<!-- Heather T-H2: STANDALONE companion. HARD WALL. Must state plainly there is no
     drop-in path and link the parity-audit wall. Must NOT imply a workaround that
     does not exist. -->

# Migrating Off Pre-Compiled Community Sinks

## There is no drop-in path

<!-- State it first and plainly. Strong-name identity wall
     (PublicKeyToken=24c2f752a8e58a10 vs unsigned shim). Link parity-audit.md
     "Third-party sinks — the identity wall" (Jared's verbatim block). -->

## Why (the identity wall, in one paragraph)

## The honest alternatives

<!-- 1. Re-host the sink behind a Herald equivalent (Console/File/HTTP/OTLP/Elasticsearch).
     2. Route Herald events through an HTTP/OTLP sink to a compatible backend.
     3. Keep that one path on real Serilog in a separate process / logging path. -->

## Popular-target mapping

<!-- Link the migration-runbook.md "Community sink gaps — Herald equivalents" table
     rather than duplicating it. -->
