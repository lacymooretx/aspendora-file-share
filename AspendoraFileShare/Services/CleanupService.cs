using AspendoraFileShare.Data;
using Microsoft.EntityFrameworkCore;

namespace AspendoraFileShare.Services;

/// <summary>
/// Background service that runs daily to clean up expired share links
/// </summary>
public class CleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CleanupService> _logger;
    private readonly IConfiguration _configuration;

    public CleanupService(
        IServiceProvider serviceProvider,
        ILogger<CleanupService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cleanup Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Run cleanup at configured time (default 2 AM)
                var cleanupHour = _configuration.GetValue<int>("Cleanup:Hour", 2);
                var now = DateTime.UtcNow;
                var nextRun = now.Date.AddHours(cleanupHour);

                if (nextRun <= now)
                {
                    nextRun = nextRun.AddDays(1);
                }

                var delay = nextRun - now;
                _logger.LogInformation("Next cleanup scheduled for {NextRun} (in {Delay})", nextRun, delay);

                await Task.Delay(delay, stoppingToken);

                await RunCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in cleanup service");
                // Wait an hour before retrying
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }

        _logger.LogInformation("Cleanup Service stopped");
    }

    private async Task RunCleanupAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting cleanup of expired shares");

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var s3Service = scope.ServiceProvider.GetRequiredService<S3Service>();

        var now = DateTime.UtcNow;
        var expiredShares = await context.ShareLinks
            .Include(s => s.Files)
            .Where(s => s.ExpiresAt < now && !s.Deleted)
            .ToListAsync(stoppingToken);

        _logger.LogInformation("Found {Count} expired shares to clean up", expiredShares.Count);

        var deletedCount = 0;
        var errorCount = 0;

        foreach (var share in expiredShares)
        {
            try
            {
                // Delete files from S3
                await s3Service.DeleteShareFilesAsync(share.Id);

                // Mark as deleted in database
                share.Deleted = true;
                share.DeletedAt = now;

                deletedCount++;
                _logger.LogDebug("Cleaned up share {ShareId}", share.ShortId);
            }
            catch (Exception ex)
            {
                errorCount++;
                _logger.LogError(ex, "Failed to clean up share {ShareId}", share.ShortId);
            }
        }

        await context.SaveChangesAsync(stoppingToken);

        _logger.LogInformation(
            "Cleanup complete. Deleted: {Deleted}, Errors: {Errors}",
            deletedCount, errorCount);
    }
}
