# Releasing Herald.OSS

The release checklist for any Herald.OSS version push. Work top to
bottom; the downstream-sync section exists because a Herald.OSS
release is not done when the package lands on nuget.org — the
ecosystem repos pin the version and go stale silently.

## 1. Prepare

- [ ] `main` is green in CI (the full net8/net9/net10 suite — no
      skipped gate, no red TFM).
- [ ] `<Version>` bumped in `Herald.OSS.csproj` (only `Version`;
      `AssemblyVersion`/`FileVersion` stay pinned unless the ABI moved).
- [ ] `CHANGELOG.md` entry added under the new version with today's
      date, following Keep a Changelog. Say plainly when binaries are
      functionally unchanged.
- [ ] `README.md` updated to the release: the `## Status — vX.Y.Z`
      section (header + a short what-this-release-is paragraph) and
      the pinned-version `<PackageReference>` install example.
- [ ] Release commit pushed as `release(x.y.z): <summary>` and the CI
      run on that exact commit is green.

## 2. Tag and publish

- [ ] Annotated tag `vX.Y.Z` on the release commit; message = short
      summary + "Published to nuget.org." Push the tag.
- [ ] GitHub release created from the tag.
- [ ] `dotnet pack -c Release`, then push the `.nupkg` (+ `.snupkg`)
      to nuget.org. **This step is irreversible** — a published
      version number can never be reused, only unlisted.
- [ ] Verify: `https://api.nuget.org/v3-flatcontainer/herald.oss/index.json`
      lists the new version.

## 3. Downstream sync (do not skip)

- [ ] **Herald.Sinks** (`mmpworks/Herald.Sinks`): bump
      `<HeraldCoreVersion>` in `Directory.Build.props` to the new
      version — one edit moves the whole sink ecosystem. Build, test,
      commit, push; cut a sinks release tag if the sinks changed too.
- [ ] **Herald.DemoApp** (nuget.org dotnet tool + `mmpworks/Herald.DemoApp`):
      rebuild the demo tool against the new Herald.OSS, publish the
      updated tool package, sync the repo. The demo is the public
      first impression — it should never trail the OSS release.
- [ ] **Umbrella modules**: update any Modules/ pointers or pins that
      reference the released version.
- [ ] Website / docs that quote the latest version number.

## Notes

- CI needs the full timeout headroom: the three-TFM suite takes
  ~25–30 minutes on a 2-core hosted runner.
- Tests must stay hosted-runner-safe: no assertions that require a
  non-UTC host timezone, no timing races that need >2 cores, generous
  ceilings on async completions (a passing wait returns immediately).
