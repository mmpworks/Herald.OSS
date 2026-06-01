### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
HERALD001 | Herald.Strategy | Warning | Rendering before Async in pipeline strategy
HERALD002 | Herald.Strategy | Warning | Duplicate pipeline step
HERALD003 | Herald.Strategy | Warning | FlightRecorder before Filtering
HERALD004 | Herald.Configuration | Warning | No sinks configured before Build()
HERALD005 | Herald.Strategy | Info | Filtering after Batching without FlightRecorder
HERALD006 | Herald.Performance | Info | LogCategory allocated per call
HERALD007 | Herald.Levels | Warning | [HeraldLog] Level string does not match a known log level
HERALD0410 | Herald.OSS | Error | HeraldNamingPolicy MSBuild value is not a recognised policy id
HERALD0411 | Herald.OSS | Error | [HeraldLog(NamingPolicy = "...")] per-method value is not a recognised policy id
HRLD0001 | Herald.OSS | Error | HeraldInterceptorsEnabled MSBuild value is not a recognised bool
HRLD0002 | Herald.OSS | Error | HeraldStrictMode MSBuild value is not a recognised bool
HRLD0003 | Herald.Serilog | Warning | Typed Serilog overload may bind to a lower arity because [OverloadResolutionPriority] requires C# 13 — set <LangVersion>13</LangVersion> or use named arguments
HRLD0011 | Herald.OSS | Error | HeraldNamingPolicyAssertion MSBuild value is not a recognised assertion
HRLD0050 | Herald.OSS | Warning | Herald interceptor surface exceeded the soft threshold
HRLD0051 | Herald.OSS | Warning | Runtime naming-policy override called in an asserting assembly
HERALD008 | Herald.AsyncSafety | Error | LogProperty.Lazy closure captures AsyncLocal<T>
HERALD009 | Herald.AsyncSafety | Error | LogProperty.Lazy closure captures HttpContext / IHttpContextAccessor
HERALD010 | Herald.AsyncSafety | Error | LogProperty.Lazy closure captures a [ThreadStatic] field
HERALD011 | Herald.AsyncSafety | Warning | LogProperty.Lazy closure captures a mutable reference-type field
HERALD012 | Herald.AsyncSafety | Error | LogProperty.Lazy closure invokes an ILogScopeProvider method
HERALD013 | Herald.Performance | Info | LogProperty.Lazy closure is trivial — pass the value directly
HERALD014 | Herald.Pipeline | Warning | LogProperty with Format or Visibility axis passed to compact-path API
