namespace Retro.Ai;

/// <summary>Apply the same response-size bound before the SDK buffers or parses Claude's response.</summary>
internal sealed class BoundedAiTransport(HttpClient inner) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (inner.Timeout != Timeout.InfiniteTimeSpan) deadline.CancelAfter(inner.Timeout);
        // The SDK's outer client has already marked 'request' as sent. Forward an independent
        // copy, preserving its original body for any SDK retry rather than re-sending that instance.
        using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri)
            { Version = request.Version, VersionPolicy = request.VersionPolicy };
        foreach (var header in request.Headers) forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (var option in request.Options) forwarded.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        if (request.Content != null)
        {
            forwarded.Content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(deadline.Token));
            foreach (var header in request.Content.Headers) forwarded.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        var response = await inner.SendAsync(forwarded, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        try
        {
            byte[] body = await JsonApi.ReadBodyAsync(response, "Claude", deadline.Token);
            var original = response.Content;
            var buffered = new ByteArrayContent(body);
            foreach (var header in original.Headers)
                if (!header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content = buffered;
            original.Dispose();
            return response;
        }
        catch { response.Dispose(); throw; }
    }
    // The caller or SDK owns 'inner'; disposing this adapter must not dispose a borrowed client.
}
