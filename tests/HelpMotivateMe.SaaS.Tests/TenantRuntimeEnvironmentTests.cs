using HelpMotivateMe.SaaS.Api.Models;
using HelpMotivateMe.SaaS.Api.Options;
using HelpMotivateMe.SaaS.Api.Services;

namespace HelpMotivateMe.SaaS.Tests;

public sealed class TenantRuntimeEnvironmentTests
{
    [Fact]
    public void BuildEnvironment_ConfiguresPwaIdentityWithoutVapidSecrets()
    {
        var configuration = new TenantConfiguration(
            "North Star Habit Space",
            "owner",
            "password",
            TenantAiMode.Disabled,
            null,
            null,
            null);

        var environment = DockerTenantRuntime.BuildEnvironment(configuration, new TenantRuntimeOptions());

        Assert.Contains("Pwa__Name=North Star Habit Space", environment);
        Assert.Contains("Pwa__ShortName=North Star Habit Space", environment);
        Assert.DoesNotContain(environment, value => value.StartsWith("Vapid__", StringComparison.Ordinal));
    }
}