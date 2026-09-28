using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using HelpMotivateMe.SaaS.Api.Models;
using HelpMotivateMe.SaaS.Api.Options;
using HelpMotivateMe.SaaS.Api.Persistence;
using HelpMotivateMe.SaaS.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HelpMotivateMe.SaaS.Api.Endpoints;

public static class TenantEndpoints
{
    public static void MapTenantEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/tenants");
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/", CreateAsync);
        group.MapPost("/{id:guid}/start", StartAsync);
        group.MapPost("/{id:guid}/stop", StopAsync);
        group.MapPost("/{id:guid}/restart", RestartAsync);
        group.MapPut("/{id:guid}/settings", UpdateSettingsAsync);
        group.MapGet("/{id:guid}/backups", ListBackupsAsync);
        group.MapPost("/{id:guid}/backups", CreateBackupAsync);
        group.MapPost("/{id:guid}/backups/{backupId:guid}/restore", RestoreBackupAsync);
    }

    private static async Task<Ok<IEnumerable<TenantResponse>>> ListAsync(ControlPlaneDbContext db,
        ITenantRuntime runtime, IOptions<TenantRuntimeOptions> options, CancellationToken cancellationToken)
    {
        var tenants = await db.Tenants.OrderBy(item => item.Subdomain).ToListAsync(cancellationToken);
        var responses = new List<TenantResponse>(tenants.Count);
        foreach (var tenant in tenants)
        {
            var state = await runtime.InspectAsync(tenant, cancellationToken);
            responses.Add(ToResponse(tenant, options.Value, state.ImageId));
        }

        return TypedResults.Ok(responses.AsEnumerable());
    }

    private static async Task<Results<Ok<TenantResponse>, NotFound>> GetAsync(Guid id, ControlPlaneDbContext db,
        ITenantRuntime runtime, IOptions<TenantRuntimeOptions> options, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        var imageId = tenant is null ? null : (await runtime.InspectAsync(tenant, cancellationToken)).ImageId;
        return tenant is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ToResponse(tenant, options.Value, imageId));
    }

    private static async Task<IResult> CreateAsync(CreateTenantRequest request, ControlPlaneDbContext db,
        TenantLifecycleService lifecycle, IOptions<TenantRuntimeOptions> options,
        CancellationToken cancellationToken)
    {
        if (!SubdomainPolicy.TryNormalize(request.Subdomain, out var subdomain))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Subdomain)] = ["Use 1-63 lowercase letters, numbers, or internal hyphens; reserved names are unavailable."]
            });

        if (!MailAddress.TryCreate(request.OwnerEmail, out _) || request.OwnerEmail.Length > 254)
            return Validation(nameof(request.OwnerEmail), "Enter a valid email address.");

        if (request.AppName?.Trim().Length > 30)
            return Validation(nameof(request.AppName), "Habit Space name may contain at most 30 characters.");

        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 100 ||
            string.IsNullOrEmpty(request.Password))
            return Validation(nameof(request.Password), "Username and password are required.");

        var aiValidation = ValidateAi(request.Plan, request.EnableAi, request.AiApiKey, options.Value);
        if (aiValidation is not null) return aiValidation;

        if (await db.Tenants.AnyAsync(item => item.Subdomain == subdomain, cancellationToken))
            return TypedResults.Conflict(new { message = "That subdomain is already in use." });

        var id = Guid.NewGuid();
        var username = request.Username.Trim();
        var aiMode = TenantPlanPolicy.GetAiMode(request.Plan, request.EnableAi);
        var tenant = new Tenant
        {
            Id = id,
            OwnerEmail = request.OwnerEmail.Trim(),
            Subdomain = subdomain,
            AppName = string.IsNullOrWhiteSpace(request.AppName) ? subdomain : request.AppName.Trim(),
            Username = username,
            Plan = request.Plan,
            AiMode = aiMode,
            AiProvider = aiMode == TenantAiMode.Disabled ? null : "OpenAI",
            AiModel = aiMode == TenantAiMode.Disabled ? null : "gpt-4.1-mini",
            HasAiApiKey = aiMode == TenantAiMode.Managed || !string.IsNullOrWhiteSpace(request.AiApiKey),
            VolumeName = $"hmm-data-{id:N}",
            Status = TenantStatus.Provisioning,
            CreatedUtc = DateTimeOffset.UtcNow,
            LastRequestUtc = DateTimeOffset.UtcNow
        };

        try
        {
            await lifecycle.ProvisionAsync(tenant,
                new TenantConfiguration(tenant.AppName, username, request.Password, aiMode, tenant.AiProvider, tenant.AiModel,
                    request.AiApiKey?.Trim()),
                cancellationToken);
        }
        catch (Exception)
        {
            return TypedResults.Problem("Tenant provisioning failed. Check the control-plane logs.", statusCode: 500);
        }

        return TypedResults.Created($"/api/tenants/{tenant.Id}", ToResponse(tenant, options.Value));
    }

    private static async Task<IResult> StartAsync(Guid id, TenantLifecycleService lifecycle,
        CancellationToken cancellationToken) =>
        await lifecycle.StartAsync(id, cancellationToken) ? TypedResults.Ok() : TypedResults.NotFound();

    private static async Task<IResult> StopAsync(Guid id, TenantLifecycleService lifecycle,
        CancellationToken cancellationToken) =>
        await lifecycle.StopAsync(id, cancellationToken) ? TypedResults.Ok() : TypedResults.NotFound();

    private static async Task<IResult> RestartAsync(Guid id, TenantLifecycleService lifecycle,
        CancellationToken cancellationToken) =>
        await lifecycle.RestartAsync(id, cancellationToken) ? TypedResults.Ok() : TypedResults.NotFound();

    private static async Task<IResult> UpdateSettingsAsync(Guid id, UpdateTenantSettingsRequest request,
        ControlPlaneDbContext db, TenantLifecycleService lifecycle, IOptions<TenantRuntimeOptions> options,
        CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (tenant is null) return TypedResults.NotFound();
        if (request.AppName?.Trim().Length > 30)
            return Validation(nameof(request.AppName), "Habit Space name may contain at most 30 characters.");
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 100)
            return Validation(nameof(request.Username), "Username is required and may contain at most 100 characters.");

        var aiValidation = ValidateAi(tenant.Plan, request.EnableAi, request.AiApiKey, options.Value,
            tenant.AiMode == TenantAiMode.BringYourOwn && tenant.HasAiApiKey);
        if (aiValidation is not null) return aiValidation;

        try
        {
            await lifecycle.ReconfigureAsync(id,
                new TenantSettingsUpdate(
                    string.IsNullOrWhiteSpace(request.AppName) ? tenant.AppName : request.AppName,
                    request.Username, request.Password, request.EnableAi, "OpenAI",
                    "gpt-4.1-mini", request.AiApiKey), cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return Validation(nameof(request.AiApiKey), exception.Message);
        }
        catch (Exception)
        {
            return TypedResults.Problem("Habit Space configuration failed. The previous container was preserved.",
                statusCode: 500);
        }

        return TypedResults.Ok();
    }

    private static async Task<IResult> ListBackupsAsync(Guid id, ControlPlaneDbContext db,
        CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (tenant is null) return TypedResults.NotFound();
        if (tenant.Plan != TenantPlan.Paid) return TypedResults.StatusCode(StatusCodes.Status403Forbidden);

        var backups = (await db.Backups.AsNoTracking().Where(item => item.TenantId == id)
                .Select(item => new TenantBackupResponse(item.Id, item.CreatedUtc, item.SizeBytes))
                .ToListAsync(cancellationToken))
            .OrderByDescending(item => item.CreatedUtc)
            .ToList();
        return TypedResults.Ok(backups);
    }

    private static async Task<IResult> CreateBackupAsync(Guid id, TenantLifecycleService lifecycle,
        CancellationToken cancellationToken)
    {
        try
        {
            var backup = await lifecycle.CreateBackupAsync(id, cancellationToken);
            return backup is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(new TenantBackupResponse(backup.Id, backup.CreatedUtc, backup.SizeBytes));
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> RestoreBackupAsync(Guid id, Guid backupId,
        TenantLifecycleService lifecycle, CancellationToken cancellationToken)
    {
        try
        {
            return await lifecycle.RestoreBackupAsync(id, backupId, cancellationToken)
                ? TypedResults.Ok()
                : TypedResults.NotFound();
        }
        catch (FileNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    public static async Task<IResult> TrackActivityAsync(Guid id, HttpContext context, ControlPlaneDbContext db,
        IOptions<TenantRuntimeOptions> options, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (tenant is null) return TypedResults.NotFound();

        var forwardedHost = context.Request.Headers["X-Forwarded-Host"].ToString().Split(':', 2)[0];
        var expectedHost = $"{tenant.Subdomain}.{options.Value.BaseDomain}";
        if (!string.Equals(forwardedHost, expectedHost, StringComparison.OrdinalIgnoreCase))
            return TypedResults.StatusCode(StatusCodes.Status403Forbidden);

        var now = DateTimeOffset.UtcNow;
        var isLogin = string.Equals(context.Request.Headers["X-Forwarded-Method"], "POST",
            StringComparison.OrdinalIgnoreCase) &&
            context.Request.Headers["X-Forwarded-Uri"].ToString().StartsWith("/api/auth/login",
            StringComparison.OrdinalIgnoreCase);
        if (isLogin) tenant.LastLoginUtc = now;
        var activityWriteInterval = TimeSpan.FromMinutes(Math.Min(5, options.Value.IdleTimeoutMinutes / 2d));
        if (isLogin || tenant.LastRequestUtc < now.Subtract(activityWriteInterval))
        {
            tenant.LastRequestUtc = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static TenantResponse ToResponse(Tenant tenant, TenantRuntimeOptions options, string? imageId = null) => new(
        tenant.Id,
        tenant.OwnerEmail,
        tenant.Subdomain,
        tenant.AppName,
        tenant.Username,
        tenant.Plan,
        tenant.AiMode,
        TenantPlanPolicy.GetWorkspaceType(tenant.Plan, tenant.AiMode),
        tenant.AiProvider,
        tenant.AiModel,
        tenant.HasAiApiKey,
        imageId,
        tenant.Status,
        $"http://{tenant.Subdomain}.{options.BaseDomain}:{options.PublicPort}",
        $"/workspace/{tenant.Id}",
        tenant.CreatedUtc,
        tenant.LastStartedUtc,
        tenant.LastRequestUtc,
        tenant.LastLoginUtc,
        GetEstimatedUptimeSeconds(tenant));

    private static long GetEstimatedUptimeSeconds(Tenant tenant)
    {
        if (tenant.Status != TenantStatus.Running || tenant.LastStartedUtc is null)
            return tenant.TotalUptimeSeconds;

        return tenant.TotalUptimeSeconds +
            Math.Max(0, (long)(DateTimeOffset.UtcNow - tenant.LastStartedUtc.Value).TotalSeconds);
    }

    private static IResult? ValidateAi(TenantPlan plan, bool enableAi, string? apiKey,
        TenantRuntimeOptions options, bool hasExistingKey = false)
    {
        var error = TenantPlanPolicy.GetValidationError(plan, enableAi, !string.IsNullOrWhiteSpace(apiKey),
            hasExistingKey, !string.IsNullOrWhiteSpace(options.ManagedAiApiKey));
        return error is null ? null : Validation(plan == TenantPlan.Free ? nameof(apiKey) : nameof(enableAi), error);
    }

    private static IResult Validation(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

}

/// <summary>Payload for creating a locally hosted tenant.</summary>
public sealed record CreateTenantRequest
{
    [Required, MaxLength(254)]
    public required string OwnerEmail { get; init; }

    [Required, MaxLength(63)]
    public required string Subdomain { get; init; }

    [MaxLength(30)]
    public string? AppName { get; init; }

    [Required, MaxLength(100)]
    public required string Username { get; init; }

    [Required]
    public required string Password { get; init; }

    public TenantPlan Plan { get; init; } = TenantPlan.Free;

    public bool EnableAi { get; init; }

    public string? AiApiKey { get; init; }
}

/// <summary>Payload for changing a tenant's credentials and AI configuration.</summary>
public sealed record UpdateTenantSettingsRequest
{
    [MaxLength(30)]
    public string? AppName { get; init; }

    [Required, MaxLength(100)]
    public required string Username { get; init; }

    public string? Password { get; init; }

    public bool EnableAi { get; init; }

    public string? AiApiKey { get; init; }
}

/// <summary>Represents a managed tenant.</summary>
public sealed record TenantResponse(
    Guid Id,
    string OwnerEmail,
    string Subdomain,
    string AppName,
    string Username,
    TenantPlan Plan,
    TenantAiMode AiMode,
    string WorkspaceType,
    string? AiProvider,
    string? AiModel,
    bool HasAiApiKey,
    string? CurrentImageId,
    TenantStatus Status,
    string Url,
    string ConfigurationUrl,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? LastStartedUtc,
    DateTimeOffset LastRequestUtc,
    DateTimeOffset? LastLoginUtc,
    long EstimatedUptimeSeconds);

/// <summary>Represents a managed tenant backup.</summary>
public sealed record TenantBackupResponse(Guid Id, DateTimeOffset CreatedUtc, long SizeBytes);