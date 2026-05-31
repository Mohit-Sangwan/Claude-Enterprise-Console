using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace ClaudeEnterprise.Infrastructure.Anthropic;

/// <summary>
/// Polly v8 resilience pipeline applied around Anthropic SDK calls.
/// The SDK has its own retry; this adds an outer circuit-style retry plus per-attempt timeout
/// to cover transport-layer flakes the SDK does not retry (e.g. socket resets).
/// </summary>
public sealed class AnthropicResiliencePipeline
{
    public ResiliencePipeline Pipeline { get; }

    public AnthropicResiliencePipeline()
    {
        Pipeline = new ResiliencePipelineBuilder()
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(180),
            })
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(400),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .Handle<TaskCanceledException>(ex => ex.InnerException is TimeoutException),
            })
            .Build();
    }
}
