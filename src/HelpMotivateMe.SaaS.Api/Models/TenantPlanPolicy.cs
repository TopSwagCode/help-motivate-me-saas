namespace HelpMotivateMe.SaaS.Api.Models;

public static class TenantPlanPolicy
{
    public static TenantAiMode GetAiMode(TenantPlan plan, bool enableAi) => enableAi
        ? plan == TenantPlan.Paid ? TenantAiMode.Managed : TenantAiMode.BringYourOwn
        : TenantAiMode.Disabled;

    public static string GetWorkspaceType(TenantPlan plan, TenantAiMode aiMode) => plan == TenantPlan.Paid
        ? "Paid"
        : aiMode == TenantAiMode.BringYourOwn ? "Free+AI config" : "Free";

    public static string? GetValidationError(TenantPlan plan, bool enableAi, bool hasProvidedKey,
        bool hasExistingKey, bool managedAiConfigured)
    {
        if (!Enum.IsDefined(plan)) return "Select Free or Paid.";
        if (!enableAi) return null;
        if (plan == TenantPlan.Free && !hasProvidedKey && !hasExistingKey)
            return "Enter your OpenAI API key to enable AI on the Free plan.";
        if (plan == TenantPlan.Paid && !managedAiConfigured)
            return "Managed AI is not configured locally. Set SAAS_MANAGED_AI_API_KEY or disable AI.";
        return null;
    }
}