using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Application.Common;
using SmartSchool.Shared.Contracts;

namespace SmartSchool.Api.Middleware;

/// <summary>Turns exceptions into a consistent <see cref="ApiError"/> JSON body.</summary>
public sealed class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception) when (context.RequestAborted.IsCancellationRequested)
        {
            // The browser navigated away; nothing to report.
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var error = new ApiError { TraceId = context.TraceIdentifier };
            switch (ex)
            {
                case ValidationException v:
                    error.Status = 400; error.Title = "Validation failed"; error.Detail = v.Message; error.Errors = v.Errors;
                    break;
                case AppException a:
                    error.Status = a.StatusCode; error.Title = a.Message;
                    break;
                case DbUpdateException:
                    logger.LogWarning(ex, "Database update failed");
                    error.Status = 409; error.Title = "The change conflicts with existing data.";
                    break;
                case OperationCanceledException when context.RequestAborted.IsCancellationRequested:
                    return;
                default:
                    logger.LogError(ex, "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path);
                    error.Status = 500; error.Title = "An unexpected error occurred.";
                    break;
            }

            context.Response.StatusCode = error.Status;
            await context.Response.WriteAsJsonAsync(error);
        }
    }
}

/// <summary>Logs API requests that take longer than the configured threshold.</summary>
public sealed class SlowRequestMiddleware(RequestDelegate next, ILogger<SlowRequestMiddleware> logger, IConfiguration config)
{
    private readonly int _thresholdMs = config.GetValue("Monitoring:SlowRequestMs", 800);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        await next(context);
        sw.Stop();

        if (sw.ElapsedMilliseconds > _thresholdMs)
            logger.LogWarning("Slow request {Method} {Path} took {Elapsed} ms (status {Status})",
                context.Request.Method, context.Request.Path, sw.ElapsedMilliseconds, context.Response.StatusCode);
    }
}
