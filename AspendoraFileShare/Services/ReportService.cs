using AspendoraFileShare.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace AspendoraFileShare.Services;

/// <summary>
/// Background service that sends weekly activity reports
/// </summary>
public class ReportService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ReportService> _logger;
    private readonly IConfiguration _configuration;

    public ReportService(
        IServiceProvider serviceProvider,
        ILogger<ReportService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Report Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Run report on configured day/time (default Monday 9 AM)
                var reportDay = _configuration.GetValue<DayOfWeek>("Report:Day", DayOfWeek.Monday);
                var reportHour = _configuration.GetValue<int>("Report:Hour", 9);

                var now = DateTime.UtcNow;
                var nextRun = GetNextOccurrence(now, reportDay, reportHour);

                var delay = nextRun - now;
                _logger.LogInformation("Next report scheduled for {NextRun} (in {Delay})", nextRun, delay);

                await Task.Delay(delay, stoppingToken);

                await GenerateAndSendReportAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in report service");
                // Wait an hour before retrying
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }

        _logger.LogInformation("Report Service stopped");
    }

    private static DateTime GetNextOccurrence(DateTime from, DayOfWeek dayOfWeek, int hour)
    {
        var result = from.Date.AddHours(hour);
        var daysUntil = ((int)dayOfWeek - (int)from.DayOfWeek + 7) % 7;

        if (daysUntil == 0 && from.Hour >= hour)
        {
            daysUntil = 7;
        }

        return result.AddDays(daysUntil);
    }

    private async Task GenerateAndSendReportAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Generating weekly report");

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<EmailService>();

        var now = DateTime.UtcNow;
        var weekAgo = now.AddDays(-7);

        // Gather statistics
        var stats = new ReportStats
        {
            // This week's activity
            SharesCreatedThisWeek = await context.ShareLinks
                .Where(s => s.CreatedAt >= weekAgo)
                .CountAsync(stoppingToken),

            FilesUploadedThisWeek = await context.Files
                .Where(f => f.UploadedAt >= weekAgo)
                .CountAsync(stoppingToken),

            DownloadsThisWeek = await context.AuditLogs
                .Where(l => l.Action == "DOWNLOAD" && l.CreatedAt >= weekAgo)
                .CountAsync(stoppingToken),

            StorageUsedThisWeek = await context.Files
                .Where(f => f.UploadedAt >= weekAgo)
                .SumAsync(f => f.FileSize, stoppingToken),

            // All-time totals
            TotalShares = await context.ShareLinks.CountAsync(stoppingToken),
            TotalActiveShares = await context.ShareLinks
                .Where(s => !s.Deleted && s.ExpiresAt > now)
                .CountAsync(stoppingToken),
            TotalFiles = await context.Files.CountAsync(stoppingToken),
            TotalStorage = await context.Files.SumAsync(f => f.FileSize, stoppingToken),
            TotalDownloads = await context.ShareLinks.SumAsync(s => s.Downloads, stoppingToken),

            // Top users this week
            TopUsers = await context.ShareLinks
                .Where(s => s.CreatedAt >= weekAgo)
                .GroupBy(s => s.User!.Email)
                .Select(g => new TopUser
                {
                    Email = g.Key,
                    ShareCount = g.Count(),
                    FileCount = g.Sum(s => s.Files.Count)
                })
                .OrderByDescending(u => u.ShareCount)
                .Take(5)
                .ToListAsync(stoppingToken)
        };

        // Generate HTML report
        var html = GenerateReportHtml(stats, weekAgo, now);

        // Send to configured recipients
        var recipients = _configuration.GetSection("Report:Recipients").Get<string[]>()
            ?? new[] { "lacy@aspendora.com" };

        foreach (var recipient in recipients)
        {
            try
            {
                await emailService.SendReportEmailAsync(recipient, "Aspendora File Share Weekly Report", html);
                _logger.LogInformation("Weekly report sent to {Recipient}", recipient);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send weekly report to {Recipient}", recipient);
            }
        }

        _logger.LogInformation("Weekly report generation complete");
    }

    private string GenerateReportHtml(ReportStats stats, DateTime from, DateTime to)
    {
        var sb = new StringBuilder();

        sb.Append(@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body { font-family: Arial, sans-serif; line-height: 1.6; color: #333; }
        .header { background-color: #660000; color: white; padding: 20px; text-align: center; }
        .content { padding: 20px; }
        .stats-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 15px; margin: 20px 0; }
        .stat-card { background: #f5f5f5; border-radius: 8px; padding: 15px; }
        .stat-value { font-size: 24px; font-weight: bold; color: #660000; }
        .stat-label { font-size: 14px; color: #666; }
        table { width: 100%; border-collapse: collapse; margin: 20px 0; }
        th, td { padding: 10px; text-align: left; border-bottom: 1px solid #ddd; }
        th { background: #f0f0f0; }
        .footer { background: #f5f5f5; padding: 15px; text-align: center; font-size: 12px; color: #666; }
    </style>
</head>
<body>
    <div class=""header"">
        <h1>Aspendora File Share</h1>
        <p>Weekly Activity Report</p>
    </div>
    <div class=""content"">
        <p><strong>Report Period:</strong> ");
        sb.Append(from.ToString("MMM dd, yyyy"));
        sb.Append(" - ");
        sb.Append(to.ToString("MMM dd, yyyy"));
        sb.Append(@"</p>

        <h2>This Week's Activity</h2>
        <div class=""stats-grid"">
            <div class=""stat-card"">
                <div class=""stat-value"">");
        sb.Append(stats.SharesCreatedThisWeek);
        sb.Append(@"</div>
                <div class=""stat-label"">Shares Created</div>
            </div>
            <div class=""stat-card"">
                <div class=""stat-value"">");
        sb.Append(stats.FilesUploadedThisWeek);
        sb.Append(@"</div>
                <div class=""stat-label"">Files Uploaded</div>
            </div>
            <div class=""stat-card"">
                <div class=""stat-value"">");
        sb.Append(stats.DownloadsThisWeek);
        sb.Append(@"</div>
                <div class=""stat-label"">Downloads</div>
            </div>
            <div class=""stat-card"">
                <div class=""stat-value"">");
        sb.Append(FormatBytes(stats.StorageUsedThisWeek));
        sb.Append(@"</div>
                <div class=""stat-label"">Data Uploaded</div>
            </div>
        </div>

        <h2>All-Time Totals</h2>
        <div class=""stats-grid"">
            <div class=""stat-card"">
                <div class=""stat-value"">");
        sb.Append(stats.TotalActiveShares);
        sb.Append(@"</div>
                <div class=""stat-label"">Active Shares</div>
            </div>
            <div class=""stat-card"">
                <div class=""stat-value"">");
        sb.Append(stats.TotalFiles);
        sb.Append(@"</div>
                <div class=""stat-label"">Total Files</div>
            </div>
            <div class=""stat-card"">
                <div class=""stat-value"">");
        sb.Append(FormatBytes(stats.TotalStorage));
        sb.Append(@"</div>
                <div class=""stat-label"">Total Storage</div>
            </div>
            <div class=""stat-card"">
                <div class=""stat-value"">");
        sb.Append(stats.TotalDownloads);
        sb.Append(@"</div>
                <div class=""stat-label"">Total Downloads</div>
            </div>
        </div>");

        if (stats.TopUsers.Any())
        {
            sb.Append(@"
        <h2>Top Users This Week</h2>
        <table>
            <tr><th>User</th><th>Shares</th><th>Files</th></tr>");

            foreach (var user in stats.TopUsers)
            {
                sb.Append("<tr><td>");
                sb.Append(user.Email);
                sb.Append("</td><td>");
                sb.Append(user.ShareCount);
                sb.Append("</td><td>");
                sb.Append(user.FileCount);
                sb.Append("</td></tr>");
            }

            sb.Append("</table>");
        }

        sb.Append(@"
    </div>
    <div class=""footer"">
        <p>&copy; ");
        sb.Append(DateTime.Now.Year);
        sb.Append(@" Aspendora Technologies. All rights reserved.</p>
        <p>This is an automated report from Aspendora File Share.</p>
    </div>
</body>
</html>");

        return sb.ToString();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes == 0) return "0 B";
        var k = 1024.0;
        var sizes = new[] { "B", "KB", "MB", "GB", "TB" };
        var i = (int)Math.Floor(Math.Log(bytes) / Math.Log(k));
        return $"{Math.Round(bytes / Math.Pow(k, i), 1)} {sizes[i]}";
    }

    private class ReportStats
    {
        public int SharesCreatedThisWeek { get; set; }
        public int FilesUploadedThisWeek { get; set; }
        public int DownloadsThisWeek { get; set; }
        public long StorageUsedThisWeek { get; set; }
        public int TotalShares { get; set; }
        public int TotalActiveShares { get; set; }
        public int TotalFiles { get; set; }
        public long TotalStorage { get; set; }
        public int TotalDownloads { get; set; }
        public List<TopUser> TopUsers { get; set; } = new();
    }

    private class TopUser
    {
        public string Email { get; set; } = null!;
        public int ShareCount { get; set; }
        public int FileCount { get; set; }
    }
}
