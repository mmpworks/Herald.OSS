#nullable enable

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MMP.Herald.Routing.Loopback;

/// <summary>
/// Fire-and-forget NDJSON HTTP poster for the loopback URL leg.
/// Each event becomes one POST with <c>Content-Type:
/// application/x-ndjson</c> and a single JSON line as body. The
/// receiver (typically the Dashboard's loopback ingest endpoint) is
/// expected to be tolerant — failures here do not block the sink and
/// are not retried, because the loopback is a peek facility, not the
/// real send path.
///
/// <para>Single shared <see cref="HttpClient"/> with a short send
/// timeout. The poster lives for the duration of the pipeline build;
/// disposing it cancels in-flight sends.</para>
/// </summary>
public sealed class LoopbackUrlPoster : IDisposable
{
    private static readonly HttpClient _client = CreateClient();

    private readonly Uri _endpoint;
    private readonly CancellationTokenSource _cts = new();
    private int _disposed;

    public LoopbackUrlPoster(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Loopback URL '{endpoint}' is not an absolute URI.", nameof(endpoint));
        _endpoint = uri;
    }

    /// <summary>
    /// Post one entry. Returns immediately; the actual send runs on a
    /// thread-pool worker so the sink hot path never blocks on socket
    /// I/O. A failed POST is silently dropped.
    /// </summary>
    public void Post(LoopbackLogEntry entry)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        ArgumentNullException.ThrowIfNull(entry);

        // Serialize on the caller (cheap for one entry, source-generated
        // path) so the worker has nothing to do but the HTTP roundtrip.
        var line = JsonSerializer.Serialize(entry, LoopbackJsonContext.Default.LoopbackLogEntry);
        _ = SendAsync(line, _cts.Token);
    }

    private async Task SendAsync(string ndjsonLine, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(ndjsonLine + "\n", Encoding.UTF8, "application/x-ndjson");
            using var response = await _client.PostAsync(_endpoint, content, ct).ConfigureAwait(false);
            // Status is intentionally ignored — loopback is best-effort.
        }
        catch
        {
            // Swallow. Loopback failures must not propagate to the sink path.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _cts.Cancel(); } catch { /* ignore */ }
        _cts.Dispose();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        })
        {
            // A loopback receiver should respond quickly; a slow one
            // should not stall an in-flight task indefinitely.
            Timeout = TimeSpan.FromSeconds(5),
        };
        return client;
    }
}
