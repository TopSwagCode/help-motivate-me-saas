using HelpMotivateMe.SaaS.Api.Options;
using HelpMotivateMe.SaaS.Api.Persistence;
using HelpMotivateMe.SaaS.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HelpMotivateMe.SaaS.Api.Endpoints;

public static class DeploymentEndpoints
{
    public static void MapDeploymentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/deployments");
        group.MapGet("/latest", (TenantDeploymentQueue queue) => TypedResults.Ok(queue.Status));
        group.MapPost("/latest", StartLatestAsync);
    }

    private static async Task<IResult> StartLatestAsync(ControlPlaneDbContext db, TenantDeploymentQueue queue,
        IOptions<TenantRuntimeOptions> options, CancellationToken cancellationToken)
    {
        var tenants = await db.Tenants.AsNoTracking()
            .OrderBy(item => item.Subdomain)
            .Select(item => new TenantDeploymentTarget(item.Id, item.Subdomain))
            .ToListAsync(cancellationToken);

        if (tenants.Count == 0) return TypedResults.BadRequest(new { message = "No Habit Spaces exist." });
        if (!queue.TryEnqueue(tenants, options.Value.Image))
            return TypedResults.Conflict(new { message = "A deployment is already running." });

        return TypedResults.Accepted("/api/admin/deployments/latest", queue.Status);
    }
}