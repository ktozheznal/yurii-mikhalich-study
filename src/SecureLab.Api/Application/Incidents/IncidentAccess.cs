using System.Diagnostics;
using System.Security.Claims;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Incidents;

// One server-side rule for the parent, its projections, and direct child access.
public sealed class IncidentAccess(IHttpContextAccessor accessor, ILogger<IncidentAccess> logger)
{
    private HttpContext Context => accessor.HttpContext
        ?? throw new InvalidOperationException("An HTTP context is required.");
    public Guid UserId => Guid.TryParse(Context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : Guid.Empty;
    public bool IsAnalyst => Context.User.IsInRole(DbSeeder.AnalystRole);
    public bool IsReporter => Context.User.IsInRole(DbSeeder.ReporterRole);
    public bool CanRead(Incident incident) => IsAnalyst || IsReporter && incident.OwnerUserId == UserId;
    public bool CanEdit(Incident incident) => IsReporter && incident.OwnerUserId == UserId;
    public bool CanDelete(Incident incident) => CanEdit(incident) && incident.Status == IncidentStatus.New;
    public bool CanTriage(Incident incident) => IsAnalyst && incident.Status == IncidentStatus.New;
    public bool CanReadInternal => IsAnalyst;

    public IQueryable<Incident> Scope(IQueryable<Incident> query)
    {
        if (IsAnalyst) return query;
        var id = UserId;
        return IsReporter ? query.Where(item => item.OwnerUserId == id) : query.Where(_ => false);
    }

    public IResult Denied(string action, Guid resourceId)
    {
        var traceId = Activity.Current?.Id ?? Context.TraceIdentifier;
        logger.LogWarning("authorization.denied subject={Subject} action={Action} resource={Resource} result=deny utc={Utc} traceId={TraceId}",
            UserId, action, resourceId, DateTimeOffset.UtcNow.ToString("O"), traceId);
        return Results.Problem(title: "Доступ заборонено", statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
    }
}
