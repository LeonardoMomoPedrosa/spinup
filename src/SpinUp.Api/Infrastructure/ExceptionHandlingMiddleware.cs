using SpinUp.Api.Contracts;

namespace SpinUp.Api.Infrastructure;

public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task Invoke(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception during request processing.");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            var error = new ApiError(
                "internal_error",
                "An unexpected error occurred.");

            await context.Response.WriteAsJsonAsync(error);
        }
    }
}
