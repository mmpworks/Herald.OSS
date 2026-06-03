# Ref3.CustomExt — Custom Sink + Custom Enricher (ZERO source change)

**What it does.** The extension-author case: a source-compiled custom `ILogEventSink`
(counts and prints events) and a custom `ILogEventEnricher` (stamps a `Tenant` property),
wired inline through the fluent API. No `appsettings.json` — everything is in code.

**Vehicle.** Renamed package (`MMP.Herald.Compat.Serilog` 0.12.5) — the flagship
**zero-source-change** result.

**What migration touched.** `Program.cs` is **byte-identical** to the real-Serilog baseline.
`diff before/Program.cs after/Program.cs` is empty. The custom sink keeps
`Serilog.Core.ILogEventSink`; the enricher keeps `Serilog.Core.ILogEventEnricher`. The only
change is one line in the csproj: `PackageReference Serilog` → `MMP.Herald.Compat.Serilog`.

**Before/after worth showing.** Put the two `Program.cs` files side by side and show they are
identical — then show the one-line csproj diff. The migrated run produces the same five
`[SINK nn]` lines and the same event count (4). This is the strongest story on the site: a
real custom-extension Serilog app moves to Herald with no source edits at all.

**Gotcha for the page.** The renamed package proves the technique but is built this run, not
yet published. Today's shipped `MMP.Herald.Serilog` would require the one-namespace find-replace
instead. The cosmetic difference (Herald `RenderMessage()` doesn't quote strings) is not a
behavior change.
