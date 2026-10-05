using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

// Навчальний старт ЛР 02. Запускати лише з локальними штучними даними.
public static class Lab02Endpoints
{
    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapGet("/api/incidents/search", async (string? q, string? sortBy, SecureLabDbContext db, CancellationToken ct) =>
        {
            var normalizedSort = NormalizeSort(sortBy);
            if (normalizedSort is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["sortBy"] = ["Допустимі значення: createdAtUtc, severity, status."]
                });
            }

            var query = db.Incidents.AsNoTracking();
            var searchTerm = q?.Trim();
            if (!string.IsNullOrEmpty(searchTerm))
            {
                var pattern = $"%{EscapeLikePattern(searchTerm)}%";
                query = query.Where(item =>
                    EF.Functions.ILike(item.Title, pattern, "\\") ||
                    EF.Functions.ILike(item.Description, pattern, "\\"));
            }

            query = normalizedSort switch
            {
                "severity" => query
                    .OrderBy(item => item.Severity == IncidentSeverity.Critical ? 0
                        : item.Severity == IncidentSeverity.High ? 1
                        : item.Severity == IncidentSeverity.Medium ? 2 : 3)
                    .ThenBy(item => item.Id),
                "status" => query
                    .OrderBy(item => item.Status == IncidentStatus.New ? 0
                        : item.Status == IncidentStatus.Triaged ? 1
                        : item.Status == IncidentStatus.InProgress ? 2
                        : item.Status == IncidentStatus.Resolved ? 3 : 4)
                    .ThenBy(item => item.Id),
                _ => query.OrderByDescending(item => item.CreatedAtUtc)
                    .ThenBy(item => item.Id)
            };

            var rows = await query
                .Take(50)
                .Select(row => new SearchIncidentResponse(
                    row.Id,
                    row.Title,
                    row.Description,
                    row.Severity.ToString(),
                    row.Status.ToString(),
                    row.CreatedAtUtc))
                .ToListAsync(ct);

            return Results.Ok(rows);
        });

        app.MapPost("/api/incidents", async (CreateIncidentRequest request, SecureLabDbContext db, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var errors = new Dictionary<string, string[]>();

            var title = request.Title?.Trim();
            if (string.IsNullOrEmpty(title))
            {
                errors["title"] = ["Title не може бути порожнім."];
            }
            else if (request.Title!.Length > 160)
            {
                errors["title"] = ["Title не може перевищувати 160 символів."];
            }

            var description = request.Description?.Trim();
            if (string.IsNullOrEmpty(description))
            {
                errors["description"] = ["Description не може бути порожнім."];
            }
            else if (request.Description!.Length > 4000)
            {
                errors["description"] = ["Description не може перевищувати 4000 символів."];
            }

            var validSeverity = Enum.TryParse<IncidentSeverity>(
                request.Severity, ignoreCase: true, out var severity)
                && Enum.IsDefined(severity);
            if (!validSeverity)
            {
                errors["severity"] = ["Допустимі значення: Low, Medium, High, Critical."];
            }

            if (request.OccurredAtUtc is null || request.OccurredAtUtc > now.AddMinutes(5))
            {
                errors["occurredAtUtc"] = ["OccurredAtUtc є обов'язковим і не може бути більш ніж на 5 хвилин у майбутньому."];
            }

            if (validSeverity
                && severity is IncidentSeverity.High or IncidentSeverity.Critical
                && description is not null
                && description.Length < 40
                && !errors.ContainsKey("description"))
            {
                errors["description"] = ["Для High або Critical Description має містити щонайменше 40 символів."];
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var hasActiveTitle = await db.Incidents.AnyAsync(item =>
                item.Title == title && item.Status != IncidentStatus.Closed, ct);
            if (hasActiveTitle)
            {
                return Results.Problem(
                    title: "Інцидент уже існує",
                    detail: "Інцидент із таким заголовком уже є серед незакритих.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var incident = new Incident
            {
                Id = Guid.NewGuid(),
                OwnerUserId = DbSeeder.AliceId,
                Title = title!,
                Description = description!,
                Severity = severity,
                Status = IncidentStatus.New,
                OccurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            db.Incidents.Add(incident);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/incidents/{incident.Id}",
                new CreatedIncidentResponse(
                    incident.Id,
                    incident.Title,
                    incident.Severity.ToString(),
                    incident.Status.ToString(),
                    incident.OccurredAtUtc,
                    incident.CreatedAtUtc));
        });
    }

    private static string EscapeLikePattern(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    private static string? NormalizeSort(string? sortBy)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            return "createdAtUtc";
        }

        return sortBy.Trim() switch
        {
            "createdAtUtc" => "createdAtUtc",
            "severity" => "severity",
            "status" => "status",
            _ => null
        };
    }
}

public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);

public sealed record CreatedIncidentResponse(
    Guid Id, string Title, string Severity, string Status,
    DateTimeOffset OccurredAtUtc, DateTimeOffset CreatedAtUtc);

public sealed record SearchIncidentResponse(
    Guid Id, string Title, string Description, string Severity, string Status,
    DateTimeOffset CreatedAtUtc);
