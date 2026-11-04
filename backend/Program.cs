using AiExperiment.Api.Data;
using AiExperiment.Api.Endpoints;
using AiExperiment.Api.Services;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Produktion: PostgreSQL (Neon) via ConnectionStrings__Default. Lokalt: en SQLite-fil.
var pg = ToNpgsql(builder.Configuration.GetConnectionString("Default"));
builder.Services.AddDbContext<AppDb>(o =>
{
    if (pg is not null) o.UseNpgsql(pg);
    else o.UseSqlite("Data Source=experiment.db");
});
builder.Services.AddSingleton<Randomizer>();
builder.Services.AddMemoryCache();

// Begrænsning af antal forespørgsler pr. afsender, mod oversvømmelse og falske deltagere.
// Grænserne er sat højt nok til, at en hel klasse bag samme netværk (samme IP) kan deltage samtidig.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (ctx, _) =>
    {
        ctx.HttpContext.Response.Headers.RetryAfter = "10";
        return ValueTask.CompletedTask;
    };
    // Alle API-kald: højst 3000 pr. minut pr. afsender (≈ 50 aktive deltagere bag samme IP med god margin).
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        ctx.Request.Path.StartsWithSegments("/api")
            ? RateLimitPartition.GetSlidingWindowLimiter(ClientKey.For(ctx), _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 3000, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0,
            })
            : RateLimitPartition.GetNoLimiter("static"));
    // Nye deltagere: højst 150 pr. 5 minutter pr. afsender.
    o.AddPolicy(RateLimits.CreateParticipant, ctx =>
        RateLimitPartition.GetSlidingWindowLimiter(ClientKey.For(ctx), _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 150, Window = TimeSpan.FromMinutes(5), SegmentsPerWindow = 5, QueueLimit = 0,
        }));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.EnsureCreated();
    db.UpgradeSchema();
    Seed.Run(db);
}

// Samtidige ændringer af samme deltager (fx dobbeltklik) -> 409, så klienten blot henter sin aktuelle tilstand.
app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var conflict = ex is DbUpdateException;
    ctx.Response.StatusCode = conflict ? StatusCodes.Status409Conflict : StatusCodes.Status500InternalServerError;
    await ctx.Response.WriteAsJsonAsync(new { error = conflict ? "conflict" : "server_error" });
}));

// Angular-appen ligger i wwwroot (bygges ind af Dockerfile). index.html må ikke caches, så nye versioner slår igennem.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name == "index.html") ctx.Context.Response.Headers.CacheControl = "no-cache";
    },
});

app.UseRateLimiter();

app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapParticipantEndpoints();
app.MapAdminEndpoints();
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
});

app.Run();

// Neon giver en URI (postgresql://user:pass@host/db?sslmode=require). Npgsql vil have key=value-format.
static string? ToNpgsql(string? cs)
{
    if (string.IsNullOrWhiteSpace(cs)) return null;
    if (!cs.StartsWith("postgres://") && !cs.StartsWith("postgresql://")) return cs;
    var uri = new Uri(cs);
    var userInfo = uri.UserInfo.Split(':', 2);
    var db = uri.AbsolutePath.Trim('/');
    return $"Host={uri.Host};Port={(uri.Port > 0 ? uri.Port : 5432)};Database={db};" +
           $"Username={Uri.UnescapeDataString(userInfo[0])};Password={Uri.UnescapeDataString(userInfo.ElementAtOrDefault(1) ?? "")};" +
           "SSL Mode=Require;Maximum Pool Size=30";
}
