// Shared arity-to-buffer-size mapping used by TypedArgsOverloadGenerator
// and SerilogArityGenerator. Both generators emit overloads that slot into
// LogPropertyBuffer{N} InlineArray structs; the mapping must be identical
// across both emitters or the generated code diverges from the struct sizes
// that actually exist in the runtime library.
//
// Extracted here so a future MaxArity bump or a new buffer size only needs
// one edit. NetStandard2.0-safe (no range patterns unavailable there).

#nullable enable

namespace MMP.Herald.Generators;

internal static class GeneratorArityHelpers
{
    /// <summary>
    /// Returns the InlineArray buffer size that accommodates <paramref name="arity"/>
    /// compact-property slots. Buffer sizes are 1, 2, 4, 8, 16; an arity that
    /// does not match a size exactly uses the next-larger buffer and slices.
    /// </summary>
    internal static int BufferSizeFor(int arity) => arity switch
    {
        1                 => 1,
        2                 => 2,
        >= 3 and <= 4     => 4,
        >= 5 and <= 8     => 8,
        >= 9 and <= 16    => 16,
        _                 => 16,
    };
}
