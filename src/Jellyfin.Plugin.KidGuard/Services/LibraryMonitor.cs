using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KidGuard.Services;
public sealed class LibraryMonitor(Manager manager, ILibraryManager library, ILogger<LibraryMonitor> logger) : BackgroundService
{
    private int _dirty = 1;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // No scanning or internet calls on Jellyfin's startup thread.
        await Task.Yield();
        library.ItemAdded += Changed;
        try
        {
            await manager.RecoverOnStart().ConfigureAwait(false);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            var ticks = 0;
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                ticks++;
                if (manager.Progress.Running || manager.RecoveryRequired) continue;
                if (Interlocked.Exchange(ref _dirty, 0) != 0 || ticks % 10 == 0)
                { try { manager.StartScan(); } catch (InvalidOperationException) { } }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception e) { logger.LogError(e, "KidGuard monitor stopped"); }
        finally { library.ItemAdded -= Changed; await manager.Stop().ConfigureAwait(false); }
    }
    private void Changed(object? sender, ItemChangeEventArgs args) => Interlocked.Exchange(ref _dirty, 1);
}
