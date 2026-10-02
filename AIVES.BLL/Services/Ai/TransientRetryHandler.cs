using System.Net;

namespace AIVES.BLL.Services.Ai;

/// <summary>
/// Retries a hosted model call that failed with 503 (overloaded) or 429 (rate limited).
/// Gemini returns these in short spikes, so a couple of quick retries usually succeed where
/// failing straight to the lecturer would not. Other status codes are returned unchanged.
/// </summary>
public sealed class TransientRetryHandler(Func<TimeSpan, CancellationToken, Task>? delay = null) : DelegatingHandler
{
    public const int MaxRetries = 2;
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(10);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (attempt == MaxRetries || !IsTransient(response.StatusCode))
                return response;

            var wait = response.Headers.RetryAfter?.Delta is { } retryAfter && retryAfter <= MaxRetryAfter
                ? retryAfter
                : TimeSpan.FromSeconds(attempt == 0 ? 1 : 3);
            response.Dispose();
            await _delay(wait, cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests;
}
