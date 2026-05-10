using System.Net;
using AspendoraFileShare.Components;
using AspendoraFileShare.Data;
using AspendoraFileShare.Services;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Microsoft.EntityFrameworkCore;

// Reference background services
using CleanupService = AspendoraFileShare.Services.CleanupService;
using ReportService = AspendoraFileShare.Services.ReportService;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to allow large file uploads (60MB per chunk with some overhead)
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 60 * 1024 * 1024; // 60MB
});

// Add database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add authentication with Microsoft Graph support
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddMicrosoftGraph(builder.Configuration.GetSection("AzureAd"))
    .AddInMemoryTokenCaches();

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Add services
builder.Services.AddScoped<S3Service>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddHttpClient<EmailService>();

// Add HttpClient for Blazor components to call local APIs
builder.Services.AddScoped(sp =>
{
    var navigationManager = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
    return new HttpClient { BaseAddress = new Uri(navigationManager.BaseUri) };
});

// Add background services
builder.Services.AddHostedService<CleanupService>();
builder.Services.AddHostedService<ReportService>();

// Add controllers for API endpoints and Microsoft Identity Web UI
builder.Services.AddControllersWithViews()
    .AddMicrosoftIdentityUI();
builder.Services.AddRazorPages();

// Add Razor components with extended timeouts for large file uploads
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddMicrosoftIdentityConsentHandler();

// Configure Blazor Server circuit options for long file uploads
builder.Services.AddServerSideBlazor(options =>
{
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromHours(1);
    options.JSInteropDefaultCallTimeout = TimeSpan.FromHours(4);
});

// Configure SignalR for large uploads
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 64 * 1024; // 64KB for SignalR messages
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(30);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
});

var app = builder.Build();

// Configure forwarded headers for reverse proxy (nginx-proxy handles SSL termination)
// This must be called BEFORE any middleware that relies on the request scheme
// Clear KnownProxies and KnownNetworks to accept forwarded headers from any source
// This is safe in Docker where only the nginx-proxy can reach the container
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    // Delegate-based error handler — writes the 500 response inline so monitoring
    // catches a real 5xx instead of the 200-rendered Razor /Error page.
    // See uptime-kuma repo: docs/error-page-monitoring.md (Phase 2).
    app.UseExceptionHandler(new ExceptionHandlerOptions
    {
        ExceptionHandler = async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "text/html; charset=utf-8";
            var requestId = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier;
            var encodedId = System.Net.WebUtility.HtmlEncode(requestId);
            await context.Response.WriteAsync(
                "<!DOCTYPE html><html><head><title>Error - File Share</title>"
                + "<style>body{font-family:sans-serif;max-width:560px;margin:4rem auto;padding:0 1rem;color:#333}"
                + "h2{color:#c00}code{background:#f4f4f4;padding:2px 6px;border-radius:3px}</style></head>"
                + "<body><h2>An error occurred while processing your request.</h2>"
                + "<p>The team has been notified. If the problem persists, share this Request ID:</p>"
                + "<p><code>" + encodedId + "</code></p>"
                + "<p><a href=\"/\">Return home</a></p></body></html>");
        },
        AllowStatusCode404Response = true,
    });
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapControllers();
app.MapRazorPages();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
