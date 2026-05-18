# HRLDxxxx diagnostic codes

Herald.OSS's interceptor generator (P3) reserves the `HRLD0001..HRLD0099` range
for MSBuild-property validation. Each ID corresponds to one input the generator
reads at build time; the diagnostic message names the property, the invalid
value, and the suggested fix.

This range is separate from the older `HERALD0xx`/`HERALD04xx` family, which
covers analyzer rules (`HERALD001..HERALD006`, `HERALD007`, `HERALD0410`,
`HERALD0411`).

## V1 codes

| ID | Severity | Trigger | Suggested fix |
|----|----------|---------|---------------|
| `HRLD0001` | Error | `<HeraldInterceptorsEnabled>` MSBuild value is not `true` or `false`. | Use `true` (default — bake interceptors) or `false` (disable, runtime resolver for every call site). |
| `HRLD0002` | Error | `<HeraldStrictMode>` MSBuild value is not `true` or `false`. | Use `true` (escalate Herald analyzer warnings to errors) or `false` (default — leave them as warnings). |
| `HRLD0050` | Warning | Assembly's interceptor surface exceeds the soft threshold (5,000 call sites). | Group callers into hot-path facades or move to `[HeraldLog]` partial methods. The warning is operator-hint only — the build still succeeds. |

## V1.1 codes

| ID | Severity | Trigger | Suggested fix |
|----|----------|---------|---------------|
| `HRLD0010` | Error | `<HeraldPipelineComposition>` MSBuild value is not a recognised composition tag. | Use `Dynamic` (the default in v0.3.0). V2 will add `SingleKernelSink` for the elided-dispatch path. |
| `HRLD0011` | Error | `<HeraldNamingPolicyAssertion>` MSBuild value is not a recognised assertion. | Use `Default` (consumer asserts no runtime naming-policy override; interceptors emit a single Pascal lane per call site). Leave the property unset to keep the multi-policy emit shape. |
| `HRLD0051` | Warning | Asserting assembly (`<HeraldNamingPolicyAssertion>Default</>`) calls `WithNamingPolicy(...)` or `InstallNamingPolicy(...)`. | Either remove the assertion (revert to multi-policy emit) or remove the call (preserve the build-time assumption). Strict mode escalates to error. |

`HRLD0051` is local-assembly only — a library that asserts but is called by a non-asserting consumer is fine, and vice versa. Cross-assembly transitivity is V2 territory.

## Property reference

| MSBuild property | Default | Allowed values | What it controls |
|------------------|---------|----------------|------------------|
| `HeraldInterceptorsEnabled` | `true` | `true`, `false` | Master switch for the interceptor generator. When `false`, no `HeraldInterceptors.g.cs` or `HeraldBuildAssertion.g.cs` is emitted; every literal-template call site stays on the runtime resolver path. |
| `HeraldStrictMode` | `false` | `true`, `false` | Promotes Herald analyzer warnings (`HERALD0xx`, `HRLD0xxx`) to errors. Informational on the build-assertion attribute; the actual escalation happens via `<WarningsAsErrors>` on the consumer's csproj when this is `true`. |
| `HeraldPipelineComposition` | `Dynamic` | `Dynamic` | Names the pipeline composition shape the assembly was built against. V1.1 ships `Dynamic` only; V2 reserves `SingleKernelSink` for the elided-dispatch path. The targets file emits `[assembly: HeraldPipelineCompositionAttribute(...)]` carrying the value. |
| `HeraldNamingPolicyAssertion` | _(unset)_ | `Default`, _(unset)_ | When `Default`, the interceptor generator emits a Pascal-only single-lane interceptor per literal-template call site; the dispatcher's BuiltinPolicy switch + `CurrentPolicyKind` read + custom-policy fallback are elided. HRLD0051 fires if the asserting assembly also calls `WithNamingPolicy(...)` or `InstallNamingPolicy(...)`. Leave unset for the V1 multi-policy emit. |

## Where the properties live

The Herald.OSS NuGet ships `buildTransitive/Herald.OSS.props` which auto-applies
the `InterceptorsNamespaces` opt-in and marks both properties as
`CompilerVisibleProperty`. Consumers who reach the generator through a
`ProjectReference` (in-source-tree builds) need to mirror the
`CompilerVisibleProperty` items by hand — see
`tests/Interceptor.SmokeTests/Interceptor.SmokeTests.csproj` for the pattern.

## Reading the build assertion at runtime

```csharp
using System.Reflection;
using MMP.Herald.Build;

var marker = typeof(Program).Assembly
    .GetCustomAttribute<HeraldBuildAssertionAttribute>();

if (marker is null)
{
    // Either HeraldInterceptorsEnabled=false on build, or the Herald.OSS
    // NuGet didn't auto-apply its buildTransitive/Herald.OSS.props.
}
else
{
    Console.WriteLine(marker.InterceptorsEnabled);     // true / false
    Console.WriteLine(marker.BakedPolicies);           // "Pascal,Snake,Camel" (V1) or "Pascal" (asserted)
    Console.WriteLine(marker.InterceptedCallSites);    // int
    Console.WriteLine(marker.StrictMode);              // true / false
    Console.WriteLine(marker.NamingPolicyAssertion);   // "Default" or "" (unasserted)
}
```

The lookup is trim-safe and AOT-safe — `Assembly.GetCustomAttribute<T>()` over a
plain attribute with init-only properties does not require dynamic-code
generation.
