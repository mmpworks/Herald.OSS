// MMP.Herald.Serilog — Layer-1 Serilog-compatibility assembly.
//
// Provides a drop-in API surface compatible with Serilog, backed by the
// Herald.OSS engine. Consumers reference this assembly instead of (or
// alongside) real Serilog; the two coexist because their namespaces are
// distinct.
//
// This assembly targets net9.0 and net10.0 only — see the csproj for rationale.

namespace MMP.Herald.Serilog;
