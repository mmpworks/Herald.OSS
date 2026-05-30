---
gap-id: expressions-dsl
serilog-surface: Serilog.Expressions string DSL (Filter.ByIncluding("..."), expression templates)
herald-status: hard-wall (predicate form maps; string DSL does not)
population-rank: medium
regression-test-id: G-GAP.2
---

<!-- Heather T-H2: STANDALONE companion. HARD WALL. The predicate form maps; the
     string-DSL form does not. Named as an open RFC to the OSS community
     (open-source-dilemma rule). -->

# Migrating Off Serilog.Expressions

## What maps and what does not

<!-- Predicate Filter.ByExcluding(e => ...) maps to Herald's processor pipeline.
     The string-DSL form (Filter.ByIncluding("Level = 'Error' and ...")) does not —
     it's a separate parse engine Herald does not implement. -->

## What you have in Serilog

## If your filter is a predicate

<!-- Rewrite the string DSL as the predicate form where the logic allows; that carries over. -->

## If your filter is genuinely string-DSL only

<!-- No drop-in path. The string DSL is an open RFC to the OSS community — if you
     implement a compatible parser on Herald's engine, the extension seam is available.
     Link parity-audit.md "Serilog.Expressions DSL — the second wall". -->

## Verify

<!-- Config using the unsupported DSL fails loud + named (G-GAP.2), never silent. -->
