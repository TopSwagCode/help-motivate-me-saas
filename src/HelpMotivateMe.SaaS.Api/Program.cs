using System.Text.Json.Serialization;
using HelpMotivateMe.SaaS.Api.Endpoints;
using HelpMotivateMe.SaaS.Api.Options;
using HelpMotivateMe.SaaS.Api.Persistence;
using HelpMotivateMe.SaaS.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOptions<TenantRuntimeOptions>()
    .Bind(builder.Configuration.GetSection(TenantRuntimeOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddDbContext<ControlPlaneDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ControlPlane")));
builder.Services.AddSingleton<ITenantRuntime, DockerTenantRuntime>();
builder.Services.AddSingleton<TenantOperationLocks>();
builder.Services.AddSingleton<TenantStartQueue>();
builder.Services.AddHostedService<TenantStartWorker>();
builder.Services.AddHostedService<IdleTenantWorker>();
builder.Services.AddScoped<TenantLifecycleService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ControlPlaneDbContext>();
    await db.Database.EnsureCreatedAsync();
    await ControlPlaneSchemaUpdater.UpgradeAsync(db);
}

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.MapTenantEndpoints();
app.MapGet("/internal/tenants/{id:guid}/activity", TenantEndpoints.TrackActivityAsync);
app.MapGet("/health/live", () => TypedResults.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (ControlPlaneDbContext db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
app.MapGet("/admin", DashboardEndpoints.WriteAdminAsync);
app.MapGet("/workspace/{id:guid}", DashboardEndpoints.WriteWorkspaceAsync);
app.MapGet("/", DashboardEndpoints.HandleRootAsync);
app.MapFallback(DashboardEndpoints.HandleFallbackAsync);

app.Run();