using System.Net;
using System.Net.Http.Headers;
using AIVES.BLL.Services.Ai;

namespace AIVES.Tests;

public sealed class TransientRetryHandlerTests
{
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task TransientFailureIsRetriedUntilItSucceeds(HttpStatusCode transient)
    {
        var (client, inner, waits) = Build(transient, HttpStatusCode.OK);

        var response = await client.PostAsync("http://model/generate", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Calls);
        Assert.Equal([TimeSpan.FromSeconds(1)], waits);
        // The body is re-sent intact on every attempt.
        Assert.All(inner.Bodies, body => Assert.Equal("{}", body));
    }

    [Fact]
    public async Task GivesUpAfterTwoRetriesAndReturnsTheLastResponse()
    {
        var (client, inner, waits) = Build(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);

        var response = await client.PostAsync("http://model/generate", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1 + TransientRetryHandler.MaxRetries, inner.Calls);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)], waits);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task OtherFailuresAreNotRetried(HttpStatusCode status)
    {
        var (client, inner, waits) = Build(status, HttpStatusCode.OK);

        var response = await client.PostAsync("http://model/generate", new StringContent("{}"));

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(1, inner.Calls);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task ShortRetryAfterIsHonouredAndLongOnesAreCapped()
    {
        var shortWait = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        shortWait.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(4));
        var longWait = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        longWait.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(5));
        var (client, _, waits) = Build([shortWait, longWait, new HttpResponseMessage(HttpStatusCode.OK)]);

        await client.PostAsync("http://model/generate", new StringContent("{}"));

        // A minutes-long Retry-After falls back to the normal backoff instead of holding the request.
        Assert.Equal([TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(3)], waits);
    }

    private static (HttpClient Client, ScriptedHandler Inner, List<TimeSpan> Waits) Build(params HttpStatusCode[] statuses) =>
        Build(statuses.Select(status => new HttpResponseMessage(status)).ToArray());

    private static (HttpClient Client, ScriptedHandler Inner, List<TimeSpan> Waits) Build(HttpResponseMessage[] responses)
    {
        var waits = new List<TimeSpan>();
        var inner = new ScriptedHandler(responses);
        var handler = new TransientRetryHandler((wait, _) => { waits.Add(wait); return Task.CompletedTask; }) { InnerHandler = inner };
        return (new HttpClient(handler), inner, waits);
    }

    private sealed class ScriptedHandler(HttpResponseMessage[] responses) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return responses[Calls++];
        }
    }
}
