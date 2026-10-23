using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosSystem.Application.Common;

namespace PosSystem.Api.Middleware;

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
            var (status, title) = ex switch
            {
                NotFoundException => (404, "Not found"),
                ConflictException => (409, "Conflict"),
                BadRequestException => (400, "Bad request"),
                UnauthorizedAppException => (401, "Unauthorized"),
                ForbiddenException => (403, "Forbidden"),
                ArgumentException => (400, "Bad request"),
                DbUpdateConcurrencyException => (409, "Conflict"),
                Npgsql.PostgresException { SqlState: "40001" } => (409, "Conflict"),
                DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "23505" } } => (409, "Duplicate value"),
                _ => (500, "Server error")
            };

            if (status == 500) logger.LogError(ex, "Unhandled exception");

            var detail = status == 500 ? "An unexpected error occurred."
                : ex is DbUpdateException ? "The record was changed by someone else or already exists. Reload and try again."
                : ex is Npgsql.PostgresException { SqlState: "40001" } ? "A concurrent update conflicted with this request. Reload and try again."
                : ex.Message;

            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = title, Detail = detail });
        }
    }
}
