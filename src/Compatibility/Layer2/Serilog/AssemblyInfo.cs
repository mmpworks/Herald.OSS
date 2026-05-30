// MMP.Herald.Compat.Serilog (Layer-2) assembly-level attributes.
using System.Runtime.CompilerServices;

// P7 corpus test grant: allows the dedicated P7 compatibility test project
// to access Serilog.Core.Logger.Inner for the CRIT-FM-L2 slot-identity test.
// The test project cannot coexist with the real Serilog NuGet in the same
// project (assembly name collision), so it lives in its own project.
// See tests/Serilog/StaticLogFacadeTests.cs for the tracking comment.
[assembly: InternalsVisibleTo("Herald.OSS.Serilog.Compat.Tests")]
