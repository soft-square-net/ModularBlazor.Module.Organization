using soft.square.net.WebApi.Modules.Organization.UseCases.Notes;
using soft.square.net.WebApi.Modules.Organization.UseCases.Notes.Create;

namespace soft.square.net.WebApi.Modules.Organization.Web.Notes;

/// <summary>POST /notes: write a new note; 201 with the note.</summary>
public sealed class Create : IEndpoint
{
    /// <summary>The request body.</summary>
    public sealed record NewNote(string Title, string? Body);

    public static RouteHandlerBuilder Map(IEndpointRouteBuilder notes) =>
        notes.MapPost("/", HandleAsync)
            .RequirePermission(OrganizationPermissions.Write)
            .WithName("Organization_CreateNote");

    private static async Task<Results<Created<NoteDto>, ProblemHttpResult>> HandleAsync(NewNote request, ISender sender, CancellationToken ct) =>
        (await sender.Send(new CreateNoteCommand(request.Title, request.Body), ct)).ToCreatedResult(note => $"notes/{note.Id}");
}
