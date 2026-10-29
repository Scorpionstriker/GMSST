namespace Scheduler.Application.Executions;

/// <summary>Retry decisions with exponential back-off (Algorithm 6.9, FR-31).</summary>
public static class RetryPolicy
{
    /// <summary>True when an attempt that failed may be retried: attempts are numbered from 0.</summary>
    public static bool ShouldRetry(int attempt, int maxRetries, bool taskMayRun) => taskMayRun && attempt < maxRetries;

    /// <summary>min(base × 2^attempt, max) seconds: 30 s, 60 s, 120 s ... up to 900 s with the defaults.</summary>
    public static TimeSpan GetDelay(int attempt, int baseDelaySeconds, int maxDelaySeconds)
    {
        var exponent = Math.Clamp(attempt, 0, 30);
        var seconds = Math.Min((double)baseDelaySeconds * Math.Pow(2, exponent), maxDelaySeconds);
        return TimeSpan.FromSeconds(Math.Max(0, seconds));
    }
}
