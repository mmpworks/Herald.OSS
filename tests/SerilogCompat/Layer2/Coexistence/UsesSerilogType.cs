// G-LAYER2.1: probe file that forces the duplicate-type collision.
//
// This file references Serilog.Log — a type defined in BOTH the Layer-2 mirror
// assembly (AssemblyName=Serilog) AND the real Serilog NuGet package.
// When both appear in the same compilation graph the compiler emits:
//   CS0433: The type 'Log' exists in both 'Serilog, ...' and 'Serilog, ...'
// This file is never compiled in isolation; its purpose is fulfilled when
// CoexistenceConflict.csproj fails to build (see G_Layer2_CoexistenceTests).
using Serilog;

namespace CoexistenceConflict;

/// <summary>
/// Minimal probe: the act of naming <see cref="Log"/> with both Serilog assemblies
/// in the reference graph triggers CS0433 at compile time.
/// </summary>
public static class UsesSerilogType
{
    public static ILogger? Logger { get; set; }  // CS0433 fires here (ILogger + Log are duplicated)
}
