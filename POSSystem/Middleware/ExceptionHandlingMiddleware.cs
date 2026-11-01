using System.Net;
using System.Text.Json;

namespace POSSystem.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access on {Path}", context.Request.Path);
            await HandleExceptionAsync(context, HttpStatusCode.Forbidden, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Business rule violation on {Path}: {Message}", context.Request.Path, ex.Message);
            await HandleExceptionAsync(context, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Path}", context.Request.Path);
            await HandleExceptionAsync(context, HttpStatusCode.InternalServerError, "An unexpected error occurred while processing your request.");
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, HttpStatusCode statusCode, string userFriendlyMessage)
    {
        if (context.Response.HasStarted)
            return;

        bool wantsJson = context.Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase)
                         || context.Request.Headers["X-Requested-With"] == "XMLHttpRequest";

        if (wantsJson)
        {
            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";
            var payload = JsonSerializer.Serialize(new { success = false, message = userFriendlyMessage });
            await context.Response.WriteAsync(payload);
            return;
        }

        context.Response.Redirect($"/Error/{(int)statusCode}?message={Uri.EscapeDataString(userFriendlyMessage)}");
    }
}
