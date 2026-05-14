#nullable enable

namespace MMP.Herald.Output.Rendering;

/// <summary>
/// Resolves per-category styling for console output. Returns null for
/// categories without configured styling (default: inherit whatever the
/// theme's Category element maps to).
///
/// The implementation parallels <see cref="IPropertyStyleResolver"/>. The
/// mapper builds a read-only dictionary at config-apply time; lookups
/// are case-insensitive because category names originate from user code
/// and should not silently fail on capitalization drift.
/// </summary>
public interface ICategoryStyleResolver
{
    CategoryStyle? Resolve(string categoryName);
}
