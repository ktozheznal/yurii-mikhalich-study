using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentMutationEndpoints
{
    public static void MapIncidentMutationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/incidents").RequireAuthorization().WithTags("Incidents");
        group.MapPut("/{id:guid}", async (Guid id, UpdateIncidentRequest request,
            IncidentAccess access, SecureLabDbContext db, CancellationToken ct) =>
        {
            var incident = await db.Incidents.SingleOrDefaultAsync(item => item.Id == id, ct);
            if (incident is null) return Missing();
            if (!access.CanEdit(incident)) return access.Denied("update", id);
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 160)
                errors["title"] = ["Вкажіть заголовок до 160 символів."];
            if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 4000)
                errors["description"] = ["Вкажіть опис до 4000 символів."];
            else if (incident.Severity is IncidentSeverity.High or IncidentSeverity.Critical
                     && request.Description.Trim().Length < 40)
                errors["description"] = ["Для High або Critical потрібно щонайменше 40 символів."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            // Deliberate A-08a: unknown JSON fields are ignored. Only content is copied.
            incident.Title = request.Title!.Trim();
            incident.Description = request.Description!.Trim();
            incident.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        group.MapDelete("/{id:guid}", async (Guid id, IncidentAccess access,
            SecureLabDbContext db, CancellationToken ct) =>
        {
            var incident = await db.Incidents.SingleOrDefaultAsync(item => item.Id == id, ct);
            if (incident is null) return Missing();
            if (!access.CanDelete(incident)) return access.Denied("delete", id);
            db.Incidents.Remove(incident);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        group.MapPost("/{id:guid}/triage", async (Guid id, IncidentAccess access,
            SecureLabDbContext db, CancellationToken ct) =>
        {
            var incident = await db.Incidents.SingleOrDefaultAsync(item => item.Id == id, ct);
            if (incident is null) return Missing();
            if (!access.CanTriage(incident)) return access.Denied("triage", id);
            var now = DateTimeOffset.UtcNow;
            incident.Status = IncidentStatus.Triaged;
            incident.UpdatedAtUtc = now;
            db.IncidentStatusHistory.Add(new IncidentStatusHistory
            {
                Id = Guid.NewGuid(), IncidentId = id, ChangedByUserId = access.UserId,
                OldStatus = IncidentStatus.New, NewStatus = IncidentStatus.Triaged,
                Note = "Первинну оцінку завершено.", CreatedAtUtc = now
            });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { incident.Id, status = incident.Status.ToString() });
        });
        app.MapGet("/api/incident-comments/{id:guid}", async (Guid id, IncidentAccess access,
            SecureLabDbContext db, CancellationToken ct) =>
        {
            var comment = await db.IncidentComments.AsNoTracking().Include(item => item.Incident)
                .Include(item => item.Author).SingleOrDefaultAsync(item => item.Id == id, ct);
            if (comment is null) return Missing();
            if (!access.CanRead(comment.Incident) || comment.IsInternal && !access.CanReadInternal)
                return access.Denied("read-comment", id);
            return Results.Ok(new Contracts.IncidentCommentResponse(comment.Id,
                comment.Author.DisplayName, comment.Text, comment.CreatedAtUtc));
        }).RequireAuthorization();
    }

    private static IResult Missing() => Results.Problem(title: "Ресурс не знайдено", statusCode: 404);
}

public sealed record UpdateIncidentRequest(string? Title, string? Description);
