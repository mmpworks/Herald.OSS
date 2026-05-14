#nullable enable

namespace MMP.Herald.Output.Rich;
/// <summary>
/// Host adapter that emits rendered rich output.
/// </summary>
public interface IRenderedLogOutputWriter
{
    void Write(RenderedLogOutput output);
}