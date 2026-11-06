using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;
using SmartSchool.Api.Middleware;
using SmartSchool.Api.Security;
using SmartSchool.Application;
using SmartSchool.Application.Common;
using SmartSchool.Infrastructure;
using SmartSchool.Infrastructure.Persistence;
using SmartSchool.Infrastructure.Security;
using SmartSchool.Shared.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    // Warnings and errors are also stored in the database for the Technical logs page.
    .WriteTo.Sink(new SmartSchool.Api.Logging.DatabaseLogSink(services),
        new Serilog.Configuration.BatchingOptions { BatchSizeLimit = 50, BufferingTimeLimit = TimeSpan.FromSeconds(3) },
        Serilog.Events.LogEventLevel.Warning));

// ---------- Core services ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<SessionValidator>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<SmartSchool.Api.Jobs.SubscriptionMaintenanceJob>();
// Relative upload folders live under the app's content root, not the process working directory.
builder.Services.PostConfigure<SmartSchool.Infrastructure.Storage.StorageSettings>(s =>
{
    if (!Path.IsPathRooted(s.RootPath)) s.RootPath = Path.Combine(builder.Environment.ContentRootPath, s.RootPath);
});

// ---------- Authentication ----------
var jwt = builder.Configuration.GetSection(JwtSettings.Section).Get<JwtSettings>() ?? new JwtSettings();
if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be set to a secret of at least 32 characters.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false; // keep "sub", "role", "perm" as issued
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = AppClaims.Name,
            RoleClaimType = AppClaims.Role
        };
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var principal = ctx.Principal!;
                if (!Guid.TryParse(principal.FindFirst(AppClaims.SessionId)?.Value, out var sid))
                {
                    ctx.Fail("Session missing.");
                    return;
                }
                Guid? tenantId = Guid.TryParse(principal.FindFirst(AppClaims.TenantId)?.Value, out var t) ? t : null;
                var validator = ctx.HttpContext.RequestServices.GetRequiredService<SessionValidator>();
                var error = await validator.ValidateAsync(sid, tenantId, ctx.HttpContext.RequestAborted);
                if (error is not null) ctx.Fail(error);
            }
        };
    });

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod()));

// ---------- API ----------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SmartSchool SaaS API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste the token returned by POST /api/auth/login"
    });
    c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", doc)] = []
    });
});

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

// ---------- Database migration and seed ----------
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DbSeeder>().SeedAsync();
}

// ---------- Pipeline ----------
// Behind Nginx/IIS/a load balancer: trust X-Forwarded-For/Proto so client IPs and HTTPS are seen correctly.
if (app.Configuration.GetValue("ReverseProxy:Enabled", false))
{
    var forwarded = new ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
    };
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
    app.UseForwardedHeaders(forwarded);
}

// Request logging wraps the exception handler so it records the final status (400, 404…), not the raw exception.
app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) =>
    ex is not null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
    : ctx.Request.Path.StartsWithSegments("/api") ? Serilog.Events.LogEventLevel.Information
    : Serilog.Events.LogEventLevel.Verbose);
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<SlowRequestMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.DocumentTitle = "SmartSchool SaaS API");
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            totalMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), ms = e.Value.Duration.TotalMilliseconds })
        }));
    }
});

// Unknown API routes return 404 instead of the Blazor page.
app.Map("/api/{**rest}", (HttpContext ctx) => Results.NotFound(new { status = 404, title = "Endpoint not found." }));
app.MapFallbackToFile("index.html");

app.Run();
