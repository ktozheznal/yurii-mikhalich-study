using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Application.Incidents;

public sealed class IncidentQueries(SecureLabDbContext dbContext, ILogger<IncidentQueries> logger)
{
    public async Task<IReadOnlyList<IncidentListItemResponse>> GetListAsync(
        IncidentStatus? status,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incidents with status filter {Status}", status);

        var query = dbContext.Incidents.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        return await query
            .OrderByDescending(incident => incident.CreatedAtUtc)
            .Select(incident => new IncidentListItemResponse(
                incident.Id,
                incident.Title,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Рахує інциденти за рівнем критичності.
    /// Політика нульових груп: повний перелік рівнів enum; рівень без інцидентів має count = 0.
    /// Порядок: за критичністю (Low, Medium, High, Critical), а не лексикографічний,
    /// тому сортування виконується в пам'яті після матеріалізації агрегату.
    /// </summary>
    public async Task<IReadOnlyList<IncidentSeveritySummaryResponse>> GetSeveritySummaryAsync(
        IncidentStatus? status,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Incidents.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(incident => incident.Status == status);
        }

        var groups = await query
            .GroupBy(incident => incident.Severity)
            .Select(group => new { Severity = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var counts = groups.ToDictionary(group => group.Severity, group => group.Count);

        var summary = Enum.GetValues<IncidentSeverity>()
            .OrderBy(severity => severity)
            .Select(severity => new IncidentSeveritySummaryResponse(
                severity.ToString(),
                counts.TryGetValue(severity, out var count) ? count : 0))
            .ToList();

        logger.LogInformation(
            "Built severity summary for status filter {Status}: {LevelCount} levels, {NonEmptyCount} with incidents",
            status,
            summary.Count,
            groups.Count);

        return summary;
    }

    public Task<IncidentDetailsResponse?> GetDetailsAsync(Guid id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading incident {IncidentId}", id);

        return dbContext.Incidents
            .AsNoTracking()
            .Where(incident => incident.Id == id)
            .Select(incident => new IncidentDetailsResponse(
                incident.Id,
                incident.Title,
                incident.Description,
                incident.Severity.ToString(),
                incident.Status.ToString(),
                incident.OccurredAtUtc,
                incident.CreatedAtUtc,
                incident.Owner.DisplayName,
                incident.Comments
                    .Where(comment => !comment.IsInternal)
                    .OrderBy(comment => comment.CreatedAtUtc)
                    .Select(comment => new IncidentCommentResponse(
                        comment.Id,
                        comment.Author.DisplayName,
                        comment.Text,
                        comment.CreatedAtUtc))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
