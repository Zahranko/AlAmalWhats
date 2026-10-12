namespace WaPlatform.Api.Services;

/// <summary>Wakes a background worker as soon as there is work, instead of waiting for its next poll.</summary>
public abstract class Wakeup
{
    private readonly SemaphoreSlim signal = new(0, 1);

    public void Pulse()
    {
        try { signal.Release(); }
        catch (SemaphoreFullException) { } // already pending
    }

    /// <summary>Returns when pulsed or after the fallback delay, whichever comes first.</summary>
    public Task WaitAsync(TimeSpan fallback, CancellationToken ct) => signal.WaitAsync(fallback, ct);
}

/// <summary>A webhook event was stored.</summary>
public class WebhookWakeup : Wakeup;

/// <summary>A message was queued.</summary>
public class SendWakeup : Wakeup;

/// <summary>
/// Version number that changes whenever messages or conversations change (incoming messages,
/// statuses, sends). Browsers long-poll it through /api/inbox/changes so the inbox updates at once.
/// In memory: the API runs as a single IIS process.
/// </summary>
public class ChangeFeed
{
    private readonly object gate = new();
    // Starts at the process start time (ms, safe for JavaScript numbers), so after a restart
    // every browser sees a different version and refreshes.
    private long version = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public long Version { get { lock (gate) return version; } }

    public void Publish()
    {
        TaskCompletionSource done;
        lock (gate)
        {
            version++;
            done = changed;
            changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        done.SetResult();
    }

    /// <summary>Returns the current version as soon as it differs from <paramref name="since"/>, or after the timeout.</summary>
    public async Task<long> WaitAsync(long? since, TimeSpan timeout, CancellationToken ct)
    {
        Task next;
        lock (gate)
        {
            if (since != version) return version;
            next = changed.Task;
        }
        await Task.WhenAny(next, Task.Delay(timeout, ct));
        return Version;
    }
}

/// <summary>
/// After every successful save: a stored webhook event wakes the processor, a queued message wakes
/// the sender, and any message or customer change moves the change feed. Bulk ExecuteUpdate calls
/// bypass this; the workers' fallback polling and the browsers' periodic refresh cover those.
/// </summary>
public class LiveUpdatesInterceptor(WebhookWakeup webhooks, SendWakeup sends, ChangeFeed feed)
    : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
{
    private sealed record Pending(bool Webhook, bool Queued, bool Changed);
    // What each context is saving, between "saving" and "saved". A context is never used concurrently.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Microsoft.EntityFrameworkCore.DbContext, Pending> pending = new();

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData e,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken ct = default)
    {
        Capture(e.Context);
        return base.SavingChangesAsync(e, result, ct);
    }

    public override Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> SavingChanges(
        Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData e, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result)
    {
        Capture(e.Context);
        return base.SavingChanges(e, result);
    }

    public override ValueTask<int> SavedChangesAsync(Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesCompletedEventData e, int result, CancellationToken ct = default)
    {
        Signal(e.Context);
        return base.SavedChangesAsync(e, result, ct);
    }

    public override int SavedChanges(Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesCompletedEventData e, int result)
    {
        Signal(e.Context);
        return base.SavedChanges(e, result);
    }

    private void Capture(Microsoft.EntityFrameworkCore.DbContext? db)
    {
        if (db is null) return;
        bool webhook = false, queued = false, changed = false;
        foreach (var entry in db.ChangeTracker.Entries())
        {
            if (entry.State is not (Microsoft.EntityFrameworkCore.EntityState.Added or Microsoft.EntityFrameworkCore.EntityState.Modified
                or Microsoft.EntityFrameworkCore.EntityState.Deleted)) continue;
            switch (entry.Entity)
            {
                case Domain.WebhookEvent when entry.State == Microsoft.EntityFrameworkCore.EntityState.Added:
                    webhook = true; break;
                case Domain.Message m:
                    changed = true;
                    if (m.Status == Domain.MessageStatuses.Queued) queued = true;
                    break;
                case Domain.Customer:
                    changed = true; break;
            }
        }
        pending.AddOrUpdate(db, new Pending(webhook, queued, changed));
    }

    private void Signal(Microsoft.EntityFrameworkCore.DbContext? db)
    {
        if (db is null || !pending.TryGetValue(db, out var p)) return;
        pending.Remove(db);
        var (webhook, queued, changed) = (p.Webhook, p.Queued, p.Changed);
        if (webhook) webhooks.Pulse();
        if (queued) sends.Pulse();
        if (changed) feed.Publish();
    }
}
