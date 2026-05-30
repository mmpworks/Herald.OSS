---
gap-id: config-by-name
serilog-surface: sink/enricher by name in appsettings.json ("Using" / "Name")
herald-status: carries-over (LoggerSinkRegistry.RegisterSink — one call, no parser fork)
population-rank: high
regression-test-id: G-SINK-WALL.1
---

<!-- Heather T-H2: STANDALONE companion. The S-NEW-1 day-one-friction gap — lands on
     the highest-value customer (in-house sink wired by name). The one call that avoids
     forking the parser. -->

# Migrating Sink/Enricher-by-Name in appsettings.json

## What you have in Serilog

<!-- "Using": ["MyCompany.Logging"], "WriteTo": [{ "Name": "MyCompanySink", "Args": {...} }] -->

## What changes

<!-- Register your in-house sink by name once via LoggerSinkRegistry.RegisterSink(...)
     (and LoggerEnricherRegistry for enrichers) before the parser runs. -->

## Step-by-step

<!-- FILL AFTER P5: exact RegisterSink signature from the shipped settings parser. -->

## Unresolved names fail loud

<!-- An unregistered name throws a named, audited error — never a silent no-op.
     Same loud-fail family as the Seq wall (G-SINK-WALL.1). -->

## Verify

<!-- Note (open decision #3 in P8 plan): if P0 removed the alias map and old on-disk
     configs need a one-time migration shim, add a "migrating old-key configs" note here.
     Depends on P0 Task 9 resolution with Richard. -->
