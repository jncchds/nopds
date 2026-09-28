using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Nopds.Conversion;
using Nopds.Formats;
using Nopds.Formats.Covers;
using Nopds.Infrastructure;
using Nopds.Infrastructure.Data;
using Nopds.Infrastructure.Settings;
using Nopds.Scanner;
using Nopds.Telegram;
using Nopds.Web.Auth;
using Nopds.Web.Endpoints;
using Nopds.Web.Infrastructure;
using Nopds.Web.Opds;
using Serilog;

if (args.Contains("--healthcheck"))
{
    // Used by the container HEALTHCHECK (the runtime image has no curl).
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0] ?? "8080";
    try
    {
        var res = await http.GetAsync($"http://127.0.0.1:{port}/health");
        return res.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException)
    {
        return 1;
    }
}

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.Configure<NopdsOptions>(builder.Configuration.GetSection(NopdsOptions.Section));
var nopds = builder.Configuration.GetSection(NopdsOptions.Section).Get<NopdsOptions>() ?? new NopdsOptions();
nopds.DataDir = Path.GetFullPath(nopds.DataDir, builder.Environment.ContentRootPath);
nopds.CacheDir = Path.GetFullPath(nopds.CacheDir, builder.Environment.ContentRootPath);
builder.Services.PostConfigure<NopdsOptions>(o =>
{
    o.DataDir = nopds.DataDir;
    o.CacheDir = nopds.CacheDir;
});
Directory.CreateDirectory(nopds.DataDir);
Directory.CreateDirectory(nopds.CacheDir);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(nopds.DataDir, "logs", "nopds-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));

var connectionString = builder.Configuration.GetConnectionString("Nopds")
                       ?? throw new InvalidOperationException("ConnectionStrings:Nopds is not configured.");

builder.Services.AddNopdsInfrastructure(connectionString);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddSingleton<SigningKeyProvider>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<SsoAccounts>();
builder.Services.AddScoped<ScopeFactory>();
builder.Services.AddScoped<BookFiles>();
builder.Services.AddScoped<Covers>();
builder.Services.AddScoped<OpdsCatalog>();
builder.Services.AddNopdsScanner();
builder.Services.AddNopdsTelegram();
builder.Services.AddSingleton<IScanObserver, ScanHubObserver>();
builder.Services.AddSingleton(sp => new CoverService(nopds.CacheDir, sp.GetRequiredService<BookParsers>()));
builder.Services.AddSingleton(sp => new ConversionService(nopds.CacheDir, sp.GetRequiredService<SettingsStore>(), sp.GetRequiredService<ILogger<ConversionService>>()));
builder.Services.AddSignalR().AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer()
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, OpdsAuthenticationHandler>(OpdsAuthenticationHandler.SchemeName, null)
    .AddNopdsSso(nopds.Oidc);
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<SigningKeyProvider>((o, keys) =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = nopds.Jwt.Issuer,
            ValidAudience = nopds.Jwt.Issuer,
            IssuerSigningKey = keys.Key,
            NameClaimType = NopdsClaims.Name,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        o.Events = new JwtBearerEvents
        {
            // SignalR sends the token as a query parameter for WebSockets.
            OnMessageReceived = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/hubs") && ctx.Request.Query["access_token"] is { Count: > 0 } t)
                {
                    ctx.Token = t;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization(o => Policies.Configure(o, JwtBearerDefaults.AuthenticationScheme, OpdsAuthenticationHandler.SchemeName));
builder.Services.AddSingleton<IAuthorizationHandler, ReaderRequirementHandler>();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.RateLimitPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddResponseCompression(o => o.EnableForHttps = true);
builder.Services.AddDataProtection()
    .SetApplicationName("nopds")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(nopds.DataDir, "keys")));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "database");

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>()
        .InitializeAsync(nopds.AutoMigrate, nopds.AdminUser, nopds.AdminPassword, nopds.AdminForce);
}

app.UseForwardedHeaders();
app.UseResponseCompression();
app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
    ex is not null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error : Serilog.Events.LogEventLevel.Debug);
app.UseDefaultFiles();
app.MapStaticAssets();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health");

// Lets browsers add the library as a search engine (search opens the SPA search page).
app.MapGet("/opensearch.xml", (HttpContext http, SettingsStore settings) =>
{
    var origin = $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}";
    var xml = Nopds.Opds.AtomWriter.OpenSearch(settings.Current.Title, settings.Current.Subtitle, origin + "/search?q={searchTerms}", "en", "text/html");
    return Results.Bytes(xml, "application/opensearchdescription+xml");
}).AllowAnonymous();

var api = app.MapGroup("/api/v1");
api.MapAuthEndpoints();
api.MapBrowseEndpoints();
api.MapReadingEndpoints();
api.MapAdminEndpoints();
app.MapHub<ScanHub>("/hubs/scan");
app.MapOpds();
app.MapKosync();

// Unknown API/OPDS paths must not fall through to the SPA.
app.Map("/api/{**rest}", () => Results.NotFound());

app.MapFallbackToFile("index.html");

await app.RunAsync();
return 0;

public partial class Program;
