// MMP.Herald.Serilog assembly-level attributes.
using System.Runtime.CompilerServices;

// Test assembly grant: allows Herald.OSS.Tests to access internal types
// (LogEventValueProjector, LogEvent constructor) for G-VM.* parity tests.
// Seam grant for P4 assembly: exposes DefaultValueFactory + HeraldPropertyValueFactory
// to the P4 plugin assembly (not yet named -- to be added when P4 ships).
[assembly: InternalsVisibleTo("Herald.OSS.Tests")]
