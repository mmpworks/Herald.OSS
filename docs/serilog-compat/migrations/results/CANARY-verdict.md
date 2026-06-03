# Canary verdict — the renamed-assembly NuGet technique WORKS

- **Date:** 2026-06-01 (overnight Wave 1, Richard)
- **Branch:** `feat/four-project-migration`
- **Status:** PROVEN empirically. Steve's instinct is correct.

## The claim under test

The existing honest-claim §5 says the literal no-source-change `using Serilog;` drop-in
"can't be a NuGet package" — it can only be a build-output assembly swap (`Serilog.dll`
dropped in place). Steve's instinct: that reasoning conflates two orthogonal things —
the **assembly file name** (which causes the collision) and the **namespace** (which
`using Serilog;` binds to). Name the DLL something else, keep `namespace Serilog.*`, and
it ships on NuGet with zero source change.

## What was built

A sibling packable project `src/Compatibility/Layer2/Serilog.Nuget/MMP.Herald.Compat.Serilog.Nuget.csproj`.
It compiles the SAME Layer-2 sources (`src/Compatibility/Layer2/Serilog/**/*.cs`,
`namespace Serilog.*`) but sets:
- `<AssemblyName>MMP.Herald.Compat.Serilog</AssemblyName>`  (was `Serilog`)
- `<RootNamespace>Serilog</RootNamespace>`                   (unchanged)
- `<PackageId>MMP.Herald.Compat.Serilog</PackageId>` + `<IsPackable>true</IsPackable>`

The bin-swap project (`..\Serilog\`) is left untouched — fully reversible.

Packed to `nupkgs/MMP.Herald.Compat.Serilog.0.12.5.nupkg` (74 KB; ships
`lib/net9.0/MMP.Herald.Compat.Serilog.dll` + `lib/net10.0/...`, NO `Serilog.dll`).
Depends transitively on `MMP.Herald.Serilog` 0.12.5 → `Herald.OSS` 0.12.5.

## The consumer test (fresh external project, off-repo)

`C:\Users\smuch\canary-consumer\` — references ONLY `MMP.Herald.Compat.Serilog` 0.12.5.
Source, verbatim and UNCHANGED from a real Serilog app:

```csharp
using Serilog;
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
Log.Information("hello {N}", 42);
```

- **Build:** succeeded, 0 warnings, net10.0.
- **Run:** printed `INF:2 ... hello 42` and `WRN:3 ... warn X12`, then `CANARY-CONSUMER-OK`.
- **Output dir:** contains `MMP.Herald.Compat.Serilog.dll` + `MMP.Herald.Serilog.dll` + Herald.OSS.
  **NO bare `Serilog.dll`.** No file collision. Publishable.

## The one true constraint (also proven)

`C:\Users\smuch\canary-coexist\` — same app + `Serilog` 4.3.1 added back. Result: **CS0433**:

> The type 'Log' exists in both 'MMP.Herald.Compat.Serilog, ...PublicKeyToken=null' and
> 'Serilog, Version=4.3.0.0, ...PublicKeyToken=24c2f752a8e58a10'

This is the unchanged rule: you cannot reference real Serilog at the same time (two
`Serilog.Log` types). The migration removes real Serilog anyway, so it is the expected
cutover constraint, not a blocker. Pre-compiled community sinks (Seq) still won't bind —
they demand strong-named identity `Serilog, PublicKeyToken=24c2f752a8e58a10`, which
`PublicKeyToken=null` cannot satisfy. That wall is unchanged.

## Herald-specific gotchas watched (all clear)

1. **Source-gen gate.** `SerilogArityGenerator` Gate 2 keys on
   `c.GetTypeByMetadataName("Serilog.Core.Logger")` and
   `marker.ContainingAssembly == c.Assembly` — by metadata NAME, not assembly file name.
   Renaming `AssemblyName` leaves `Serilog.Core.Logger` declared in the same compilation,
   so the gate still fires. Verified: typed `Log`/`Core.Logger` overloads generate correctly.
2. **obj/bin glob hygiene.** A recursive `..\Serilog\**\*.cs` include initially swept the
   bin-swap project's own `obj/.../*.Generated.cs`, producing CS0111 duplicate `Log.Fatal`.
   Fixed by `Exclude="..\Serilog\obj\**\*.cs;..\Serilog\bin\**\*.cs"`. This is a build-file
   detail of compiling shared sources, NOT a flaw in the technique.
3. **Herald.OSS does NOT export `namespace Serilog.*`.** It excludes `src\Compatibility\Layer2\**`
   and only ships `namespace MMP.Herald.Serilog.*` (Layer-1) + `namespace MMP.Herald.*`.
   So the renamed package's `Serilog.*` types do not collide with anything in the Herald.OSS
   transitive graph. Three distinct namespace roots.

## Verdict

The renamed-assembly NuGet technique works. It is **strictly better** than the bin-swap for
the can't-touch-source migration story: identical zero-source-change property, but it ships
as a normal NuGet package. honest-claim §5 needs correction (see ground-truth section in the
Wave 1 report).
