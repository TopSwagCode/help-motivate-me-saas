using HelpMotivateMe.SaaS.Api.Models;

namespace HelpMotivateMe.SaaS.Tests;

public sealed class TenantPlanPolicyTests
{
    [Theory]
    [InlineData(TenantPlan.Free, false, TenantAiMode.Disabled, "Free")]
    [InlineData(TenantPlan.Free, true, TenantAiMode.BringYourOwn, "Free+AI config")]
    [InlineData(TenantPlan.Paid, false, TenantAiMode.Disabled, "Paid")]
    [InlineData(TenantPlan.Paid, true, TenantAiMode.Managed, "Paid")]
    public void PlanAndAiSelection_DetermineWorkspaceType(TenantPlan plan, bool enableAi,
        TenantAiMode expectedMode, string expectedType)
    {
        var mode = TenantPlanPolicy.GetAiMode(plan, enableAi);

        Assert.Equal(expectedMode, mode);
        Assert.Equal(expectedType, TenantPlanPolicy.GetWorkspaceType(plan, mode));
    }

    [Fact]
    public void FreeAi_RequiresProvidedOrExistingKey()
    {
        var error = TenantPlanPolicy.GetValidationError(TenantPlan.Free, true, false, false, false);

        Assert.NotNull(error);
        Assert.Null(TenantPlanPolicy.GetValidationError(TenantPlan.Free, true, true, false, false));
        Assert.Null(TenantPlanPolicy.GetValidationError(TenantPlan.Free, true, false, true, false));
    }

    [Fact]
    public void PaidAi_RequiresManagedConfiguration()
    {
        Assert.NotNull(TenantPlanPolicy.GetValidationError(TenantPlan.Paid, true, false, false, false));
        Assert.Null(TenantPlanPolicy.GetValidationError(TenantPlan.Paid, true, false, false, true));
    }
}