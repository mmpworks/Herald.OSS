#nullable enable

namespace MMP.Herald.Pipeline.Kernel;

/// <summary>
/// Marker interface declaring that a sink never consults the rendered
/// message string — it only reads the template and properties from the
/// <see cref="LogEventBuffer"/>. JSON sinks, OTLP sinks, and the null
/// sink are the canonical examples.
///
/// <para>
/// <b>Why this exists.</b> <c>WithDeferredRendering()</c> is an
/// optimization that skips building the rendered message string at the
/// event factory and defers it until a downstream stage asks for the
/// text. The optimization is a win when every configured sink is
/// structured-only; it is a <i>pessimization</i> when any configured
/// sink needs the rendered text — the deferred form has to be inflated
/// at the sink boundary, which costs more than rendering inline would
/// have.
/// </para>
///
/// <para>
/// Sinks that implement <c>IStructuredOnlySink</c> are telling the
/// builder "I won't read <see cref="LogEventBuffer.Message"/>; the
/// template and properties are enough." The builder checks this at
/// validation time and warns when <c>WithDeferredRendering</c> is
/// enabled alongside a sink that <i>does</i> render text (console,
/// text file, Slack-style user-facing formatters).
/// </para>
///
/// <para>
/// Not implementing this interface is never unsafe — it just means the
/// sink opts out of the deferred optimization. A sink that sometimes
/// renders and sometimes doesn't should not claim the interface; the
/// honest default is "I might render."
/// </para>
/// </summary>
public interface IStructuredOnlySink
{
}
