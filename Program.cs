using octo_fiesta.Models.Settings;
using octo_fiesta.Services;
using octo_fiesta.Services.SquidWTF;
using octo_fiesta.Services.Local;
using octo_fiesta.Services.Validation;
using octo_fiesta.Services.Subsonic;
using octo_fiesta.Services.Common;
using octo_fiesta.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();

// Exception handling
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Configuration
builder.Services.Configure<SubsonicSettings>(
    builder.Configuration.GetSection("Subsonic"));
builder.Services.Configure<SquidWTFSettings>(
    builder.Configuration.GetSection("SquidWTF"));

// Get the configured subsonic settings
var subsonicSettings = new SubsonicSettings();
builder.Configuration.GetSection("Subsonic").Bind(subsonicSettings);
var enableExternalPlaylists = subsonicSettings.EnableExternalPlaylists;

// Business services
// Registered as Singleton to share state (mappings cache, scan debounce, download tracking, rate limiting)
builder.Services.AddSingleton<ILocalLibraryService, LocalLibraryService>();

// Subsonic services
builder.Services.AddSingleton<SubsonicRequestParser>();
builder.Services.AddSingleton<SubsonicResponseBuilder>();
builder.Services.AddSingleton<SubsonicModelMapper>();
builder.Services.AddScoped<SubsonicProxyService>();

// Monochrome API client with failover support
builder.Services.AddSingleton<MonochromeApiClient>();

// Register Monochrome/SquidWTF music services (no login required!)
if (enableExternalPlaylists)
{
    builder.Services.AddSingleton<PlaylistSyncService>();
}
builder.Services.AddSingleton<IMusicMetadataService, SquidWTFMetadataService>();
builder.Services.AddSingleton<IDownloadService, SquidWTFDownloadService>();

// Startup validation - register validators
builder.Services.AddSingleton<IStartupValidator, SubsonicStartupValidator>();
builder.Services.AddSingleton<IStartupValidator, SquidWTFStartupValidator>();

// Register orchestrator as hosted service
builder.Services.AddHostedService<StartupValidationOrchestrator>();

// Register cache cleanup service (only runs when StorageMode is Cache)
builder.Services.AddHostedService<CacheCleanupService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders("X-Content-Duration", "X-Total-Count", "X-Nd-Authorization");
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
// Enable request body buffering FIRST to allow multiple reads (for proxy forwarding)
app.UseRequestBodyBuffering();

app.UseExceptionHandler(_ => { }); // Global exception handler

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Only use HTTPS redirection in production
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.UseCors();

app.MapControllers();

// Start the application
app.Start();

// Display listening URL after startup
foreach (var url in app.Urls)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("✓ ");
    Console.ResetColor();
    Console.Write("Listening on: ");
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine(url);
    Console.ResetColor();
}

Console.WriteLine();

// Wait for shutdown
app.WaitForShutdown();