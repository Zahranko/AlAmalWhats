namespace WaPlatform.Api.Services;

/// <summary>
/// On the VPS a release starts the new container next to the old one (blue/green) and stops the
/// old one a few seconds after traffic switches. Holding the background workers back for
/// Workers:StartDelaySeconds keeps the two from sending and processing at the same time.
/// The claims in SendWorker would already prevent double sends; this also keeps the startup
/// recovery and webhook processing from racing the old instance. 0 (the default) = no wait.
/// </summary>
public sealed class WorkerStartDelay(IConfiguration config, ILogger<WorkerStartDelay> log)
{
    public Task WaitAsync(string worker, CancellationToken ct)
    {
        var seconds = config.GetValue("Workers:StartDelaySeconds", 0);
        if (seconds <= 0) return Task.CompletedTask;
        log.LogInformation("{Worker} starts in {Seconds} s.", worker, seconds);
        return Task.Delay(TimeSpan.FromSeconds(seconds), ct);
    }
}
