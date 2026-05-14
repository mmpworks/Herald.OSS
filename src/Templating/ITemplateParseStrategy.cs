#nullable enable

namespace MMP.Herald.Templating;

/// <summary>
/// A hardcoded path from template string to parsed <see cref="MessageTemplate"/>.
/// Each implementation is a single call chain — no branching on mode, no
/// per-call strategy selection. The strategy is chosen at parser construction
/// and dispatched through this interface for every call. Dispatch is paid once
/// per Parse, not once per code-path decision.
///
/// Named presets live on <see cref="TemplateParseStrategies"/>. Custom
/// strategies are free to implement this interface directly — e.g. a
/// closed-world <c>StaticOnlyStrategy</c> populated by the source generator
/// or an Enterprise-only native-parser strategy.
/// </summary>
public interface ITemplateParseStrategy
{
    /// <summary>
    /// Return the parsed template for <paramref name="template"/>. Implementations
    /// are expected to cache as needed; callers should treat the returned
    /// <see cref="MessageTemplate"/> as immutable and share-safe.
    /// </summary>
    MessageTemplate Parse(string template);
}
