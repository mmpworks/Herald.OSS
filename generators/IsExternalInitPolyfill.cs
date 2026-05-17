#nullable enable

// netstandard2.0 polyfill for the type the C# compiler needs to
// synthesize `init` accessors on records. The generator assembly
// targets netstandard2.0 (Roslyn analyzer requirement) and the BCL
// shipped at that level predates init-only properties, so the C#
// compiler errors out on records unless this marker type is in
// scope. The shim is a no-op — the compiler only needs the type to
// exist by name.
//
// Internal so it never surfaces from the generator assembly into
// any consuming compile.

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
