using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using soft.square.net.WebApi.Modules.Organization.Infrastructure.Data;
using soft.square.net.WebApi.SharedKernel;

namespace soft.square.net.WebApi.Modules.Organization.Web.Extensions;

/// <summary>
/// The exceptions that still reach the endpoints, as problem details:
/// <list type="bullet">
/// <item>a broken domain rule (an aggregate throws <see cref="DomainException"/>): 409 with its message;</item>
/// <item>a lost race on a concurrency token: 409;</item>
/// <item>a save the database refused (<see cref="DbUpdateException"/>), read with <see cref="DbFailures"/>: a unique
/// key it breaks or a row it refers to that is gone is 409; anything else (a value the engine refuses, a lost
/// connection) is a 500 that names neither a duplicate nor the database's own message, and is logged.</item>
/// </list>
/// Expected outcomes come back as an Ardalis Result instead (see <see cref="ResultExtensions"/>). On the module's route
/// group, so it is module code and unloads with the module.
/// </summary>
public sealed class OrganizationProblemsFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (DbUpdateConcurrencyException) { return Results.Problem("Someone else changed this first; reload it and try again.", statusCode: StatusCodes.Status409Conflict); }
        catch (DbUpdateException e) { return Refused(context.HttpContext, e); }
        catch (DomainException e) { return Results.Problem(e.Message, statusCode: StatusCodes.Status409Conflict); }
    }

    /// <summary>The problem for a save the database refused.</summary>
    public static IResult Refused(HttpContext http, DbUpdateException exception)
    {
        switch (DbFailures.Classify(exception))
        {
            case DbFailure.Duplicate:
                return Results.Problem("The change conflicts with data that already exists (the same key, or a second primary membership).",
                    statusCode: StatusCodes.Status409Conflict, title: "Duplicate");
            case DbFailure.MissingReference:
                return Results.Problem("The change refers to a row that was removed, or removes a row that is still in use; reload and try again.",
                    statusCode: StatusCodes.Status409Conflict, title: "Missing reference");
            default:
                http.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("soft.square.net.WebApi.Modules.Organization.Web")
                    .LogError(exception, "The database refused a change of {Method} {Path}.", http.Request.Method, http.Request.Path);
                return Results.Problem($"The database could not save the change ({(exception.InnerException ?? exception).GetType().Name}); the server log has the details.",
                    statusCode: StatusCodes.Status500InternalServerError, title: "Not saved");
        }
    }
}
