#nullable enable

namespace MMP.Herald.Output.Writers;
/// <summary>
/// Emits already-formatted text to some destination.
/// </summary>
public interface ILineWriter
{
    void WriteLine(string line);
}