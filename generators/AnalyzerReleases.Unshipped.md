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
HRLD0050 | Herald.OSS | Warning | Herald interceptor surface exceeded the soft threshold
