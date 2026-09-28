using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Nopds.Infrastructure;
using Nopds.Infrastructure.Data;
using Nopds.Web.Auth;
using Nopds.Web.Endpoints;
using Nopds.Web.Infrastructure;
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

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer()
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, OpdsAuthenticationHandler>(OpdsAuthenticationHandler.SchemeName, null);
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
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddNpgSql(connectionString, name: "database");

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>()
        .InitializeAsync(nopds.AutoMigrate, nopds.AdminUser, nopds.AdminPassword);
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

var api = app.MapGroup("/api/v1");
api.MapAuthEndpoints();

app.MapFallbackToFile("index.html");

await app.RunAsync();
return 0;

public partial class Program;
