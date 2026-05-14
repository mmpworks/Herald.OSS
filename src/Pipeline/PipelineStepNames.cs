namespace MMP.Herald.Pipeline;

/// <summary>
/// Canonical names for every pipeline step the factory dispatches. These
/// are <c>const string</c> so they are usable in attribute arguments and
/// pattern-matched literals; they are the same values stored on each
/// <c>PipelineStep.Name</c> record in <c>PipelineStrategy.cs</c>.
///
/// <para>
/// <b>Why the separate file.</b> <c>MMP.Herald.Generators</c> (Roslyn
/// source-gen + analyzers) links this file into its netstandard2.0
/// assembly via <c>&lt;Compile Include="..."/&gt;</c>. That way
/// <c>HeraldStrategyAnalyzer</c>'s literal matches cannot drift when
/// Core renames a step — renaming a constant here is a compile-time
/// break on both sides, not a silent behaviour change in the analyzer.
/// </para>
///
/// <para>
/// Generators targets netstandard2.0, so this file must stay language-
/// feature-conservative: plain <c>public static class</c>, <c>const
/// string</c> only, no records, no pattern matching, no expression-bodied
/// members beyond trivial shapes.
/// </para>
/// </summary>
public static class PipelineStepNames
{
    public const string Swappable = "swappable";
    public const string HotPath = "hotPath";
    public const string Async = "async";
    public const string Rendering = "rendering";
    public const string Batching = "batching";
    public const string Filtering = "filtering";
    public const string PostFiltering = "postFiltering";
    public const string EventProcessing = "eventProcessing";
    public const string FlightRecorder = "flightRecorder";
    public const string CircuitBreaker = "circuitBreaker";
    public const string Retry = "retry";
    public const string DurableBuffer = "durableBuffer";
    public const string Fallback = "fallback";
    public const string Audit = "audit";
    public const string FanOut = "fanOut";
}
