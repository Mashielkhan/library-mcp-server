using LibraryMCPServer.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibraryMCPServer.Services;

public sealed class OpenLibraryRefreshService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OpenLibrarySettings _settings;
    private readonly ILogger<OpenLibraryRefreshService> _logger;

    public OpenLibraryRefreshService(IServiceScopeFactory scopeFactory, OpenLibrarySettings settings, ILogger<OpenLibraryRefreshService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.EnableBackgroundRefresh) return;
        var errors = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var refreshed = await scope.ServiceProvider.GetRequiredService<OpenLibraryService>().RefreshStaleBooksAsync(stoppingToken);
                _logger.LogInformation("Open Library background refresh updated {Count} books.", refreshed);
                errors = 0;
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                errors++;
                _logger.LogError(ex, "Open Library background refresh failed ({Count}/{Max}).", errors, _settings.MaxConsecutiveErrors);
                if (errors >= Math.Max(1, _settings.MaxConsecutiveErrors)) return;
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}