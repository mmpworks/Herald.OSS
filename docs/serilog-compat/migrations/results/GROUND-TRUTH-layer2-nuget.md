# Ground truth — the Layer-2 / NuGet question, resolved

- **By:** Richard, 2026-06-01 (Wave 1). Empirically proven; see CANARY-verdict.md.
- **Purpose:** correct honest-claim section 5 and the migration runbook. This doc states the
  measured truth; it does NOT edit honest-claim.md (engineering-owned single source; a section-5
  claim change is Steve's call). Wave 4 / Steve folds this in.

## What honest-claim section 5 says today (and why it is wrong as written)

Section 5 ("Why the literal `using Serilog;` drop-in can't be a NuGet package") argues that a
no-source-change drop-in "would need an assembly literally named `Serilog`... NuGet cannot ship
that... So the no-source-change path is not a package; it is a build-output assembly swap."

That reasoning conflates two orthogonal things:

- the **assembly file name** (`Serilog.dll`) — this is what causes the file collision; and
- the **namespace** (`namespace Serilog`) — this is what `using Serilog;` binds to.

You can keep `namespace Serilog.*` and `RootNamespace=Serilog` while naming the DLL anything
else. `using Serilog;` still compiles; the file no longer collides; the package ships.

## What is actually true (measured)

1. **A no-source-change `using Serilog;` drop-in CAN ship as a NuGet package.** Build the
   Layer-2 mirror sources (`namespace Serilog.*`) with
   `<AssemblyName>MMP.Herald.Compat.Serilog</AssemblyName>` (DLL = `MMP.Herald.Compat.Serilog.dll`)
   and `<RootNamespace>Serilog</RootNamespace>`. Pack it. A fresh consumer referencing only that
   package compiles and runs verbatim `using Serilog;` + `Log.Information(...)` code with ZERO
   source edits, and the output dir contains NO bare `Serilog.dll`. Proven (CANARY-verdict.md).

2. **The one true constraint is unchanged.** You cannot reference real Serilog at the same time:
   two `Serilog.Log` types → CS0433. The migration removes real Serilog anyway, so this is the
   expected cutover rule, not a blocker. Proven (the canary-coexist case yields the exact CS0433).

3. **Pre-compiled community sinks (Seq, MSSqlServer, ...) still will not bind.** They demand
   strong-named identity `Serilog, PublicKeyToken=24c2f752a8e58a10`; the unsigned mirror is
   `PublicKeyToken=null`. That wall is real and unchanged. The renamed package does not move it.

## The corrected section-5 wording (proposed — Steve/engineering to ratify)

> The literal no-source-change `using Serilog;` drop-in **does** ship as a NuGet package. The
> trick is to keep the `namespace Serilog.*` surface but name the assembly file something other
> than `Serilog.dll` (we ship `MMP.Herald.Compat.Serilog.dll`). `using Serilog;` binds to the
> namespace, not the file name, so consumer code compiles unchanged; and because the DLL is not
> named `Serilog.dll`, there is no file collision in the output. The one rule that remains: you
> cannot reference real Serilog at the same time — two `Serilog.Log` types collide (CS0433). The
> migration removes real Serilog anyway, so that is the expected cutover step. Pre-compiled
> community sinks still require Serilog's strong-named identity, which the unsigned mirror cannot
> provide; that wall is unchanged.

## Vehicle guidance (what this changes for the runbook)

Two real migration vehicles, pick by app shape:

- **Renamed package (`MMP.Herald.Compat.Serilog`) — zero source change.** Best for inline-wired
  apps: custom sinks/enrichers, `WriteTo.Console()`, `Destructure.*`, predicate filters. Proven on
  Ref3 (byte-identical Program.cs). Gap today: no Layer-2 `ReadFrom.Configuration` bridge, so
  `appsettings.json`-configured apps are not yet zero-change on this vehicle.
- **Find-replace (`MMP.Herald.Serilog`) — one-namespace swap.** Covers all apps including
  config-driven ones, at the cost of `using Serilog;` → `using MMP.Herald.Serilog;` (+ an added
  `using Herald.OSS.Serilog.Settings;` for `ReadFrom.Configuration`). Proven on Ref1/Ref2.

## Open follow-ups (not done tonight; flagged for Steve)

- The renamed package is built and packed locally but NOT published. Publishing is Glenn/Max's
  lane and needs Steve's go (irreversible). It would become the headline zero-change vehicle.
- For true zero-change on config-driven apps, the renamed (Layer-2) vehicle needs a Layer-2
  `ReadFrom.Configuration` bridge (today the settings extension targets the Layer-1
  `LoggerConfiguration` type only).
