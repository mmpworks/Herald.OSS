# Herald.OSS.Native layering plan

**Date:** 2026-05-31
**Status:** DRAFT — needs Richard (architecture-designer) ratification before Glenn/Max execute
**Context:** Herald.OSS 0.12.0 just merged the Serilog API surface into the
`Herald.OSS` assembly. This plan re-introduces a kernel/surface split as a
**two-package layering** so the sink ecosystem can depend on the lean kernel
without dragging the Serilog surface — while the advertised `Herald.OSS`
package keeps the full one-add Serilog experience.

## Goal (Steve, 2026-05-31)

- Create a `Herald.OSS.Native` package = the **kernel only** (no Serilog surface).
- Precedence: **`Herald.OSS.Native` → `Herald.OSS`**. A change to Native bubbles
  up to Herald.OSS automatically.
- `Herald.Sinks` points at **`Herald.OSS.Native`**, not `Herald.OSS`.
- Do it now while nothing is in use — the migration is cheap today and expensive later.

## Target layering

```
Herald.OSS.Native   (kernel: pipeline, sinks contract, enrichers, generator)
        │  ProjectReference (source bubble-up)
        ▼
Herald.OSS          (= Native  +  src/Serilog/**  — the advertised package)
        ▲
        │ what app developers add → full Serilog experience in one package
        │
Herald.Sinks.*  ──► Herald.OSS.Native   (kernel only, no Serilog drag)
```

End-state comparison:

| Package | 0.11.0 (separate) | 0.12.0 (merged, shipped) | Target (Native) |
|---|---|---|---|
| Advertised add | Herald.OSS + MMP.Herald.Serilog | **Herald.OSS** (all-in-one) | **Herald.OSS** (all-in-one) |
| Kernel-only pkg | Herald.OSS | — (kernel+Serilog fused) | **Herald.OSS.Native** |
| Sinks depend on | Herald.OSS (kernel) | Herald.OSS (drags Serilog) | **Herald.OSS.Native** |

Target keeps 0.12.0's one-add win **and** restores a lean kernel for sinks.

## Mechanics — recommended: Option A (source split in the Herald.OSS repo)

1. **New `Herald.OSS.Native.csproj`** compiles the kernel sources — everything
   that Herald.OSS compiles today **except `src/Serilog/**`**. AssemblyName
   `Herald.OSS.Native`, PackageId `Herald.OSS.Native`. Owns the
   `MMP.Herald.OSS.Generators` analyzer ship (sinks get it through Native).
2. **`Herald.OSS.csproj` slims to the surface layer:** compiles **only
   `src/Serilog/**`** (net9+; empty on net8) and takes a
   `ProjectReference` to `Herald.OSS.Native`. ProjectReference → package
   dependency on pack, so a Native edit bubbles into Herald.OSS automatically.
3. **Namespaces unchanged.** Kernel stays `MMP.Herald.*`. Types move *assembly*
   (→ `Herald.OSS.Native.dll`) but not *namespace*, so `using MMP.Herald;`
   source-compat holds for anyone referencing `Herald.OSS` (transitively gets
   Native). Nothing is in use yet → **source-compat only; no `[TypeForwardedTo]`
   needed** (revisit only if we ever ship a binary-compat promise).
4. **`Herald.Sinks/Directory.Build.props` repivots** from `Herald.OSS` to
   `Herald.OSS.Native`: sibling ProjectReference target becomes
   `../../../Herald.OSS/Herald.OSS.Native.csproj`; standalone PackageReference
   id becomes `Herald.OSS.Native`; `HeraldCoreVersion` pin tracks Native's
   version. The forced-package-ref escape (Max, 2026-05-30) and the generator
   ItemGroup both switch to the Native artifact.

## Gotchas to design through (for Richard)

- **InternalsVisibleTo flips direction.** Today the Serilog shim/adapter reaches
  kernel internals (`PipelineBuildResult.Logger` via `InternalsVisibleTo
  MMP.Herald.Serilog`). After the split, `Herald.OSS.Native` must grant
  `InternalsVisibleTo Herald.OSS` (the surface assembly now needs the kernel's
  internals). Audit every current IVT grant on Herald.OSS and re-home it to
  Native (Tests, Sci, Pro, Enterprise, ManagementApi, benchmarks all consume
  kernel internals → grant from Native).
- **Generator ownership.** `MMP.Herald.OSS.Generators` must ship in the Native
  package (sinks depend on Native and need the `[ModuleInitializer]`
  auto-registration generator). Herald.OSS should NOT double-ship it (CS0101/
  CS0111 double-emit risk — same class Max already guards in the sinks props).
- **buildTransitive / .targets props** (`Herald.OSS.props`, `Herald.OSS.targets`,
  interceptor namespace opt-in) — decide whether they live on Native (kernel
  interceptors) or Herald.OSS. Likely Native, since interceptors are a kernel
  fast-path concern.
- **MMP.Herald.Serilog shim** stays a forwarding shim, now pointing at
  `Herald.OSS` (unchanged — it wants the full surface).
- **DemoApp** bundles the kernel; it can keep referencing `Herald.OSS` (gets
  Native transitively) or switch to Native — decide based on whether the demo
  exercises any Serilog-surface API (today it uses native QuickLogBuilder, so
  Native would suffice, but Herald.OSS is harmless).

## Migration steps (once ratified)

1. Richard ratifies the assembly-split + IVT re-homing.
2. Glenn: create `Herald.OSS.Native.csproj`, move kernel compile + generator
   ship + IVT grants + buildTransitive props; slim `Herald.OSS.csproj` to the
   Serilog surface + ProjectReference Native. Version both at the next minor
   (proposed **0.13.0** — the layering is a visible packaging change).
3. Max: repivot `Herald.Sinks/Directory.Build.props` to Native; update the
   forced-package-ref + generator ItemGroups; bump `HeraldCoreVersion`.
4. Build matrix green (net8/net9/net10) + full test suite + a sink build that
   proves it compiles against Native without the Serilog surface present.
5. Publish `Herald.OSS.Native` + `Herald.OSS` + companions at 0.13.0;
   `HERALD_NUGET_FULL` covers `Herald.OSS*` so Native publishes on the same key.

## Open questions

- Version: bump to **0.13.0** for the layering (recommended — it's a real
  packaging shape change), or keep 0.12.x?
- Do we want Native publicly discoverable on nuget.org, or listed-but-quiet
  (advertised add stays `Herald.OSS`)? Recommend published + documented as
  "kernel for sink authors," not the headline.
- net8: Native is the only TFM that matters for sinks today (they target
  net8/9/10). Serilog surface stays net9+. No conflict — Native carries all
  three TFMs, surface layer is empty on net8.
