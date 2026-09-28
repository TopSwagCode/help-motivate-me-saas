using System.Text.RegularExpressions;

namespace HelpMotivateMe.SaaS.Api.Services;

public static partial class SubdomainPolicy
{
    private static readonly HashSet<string> ReservedSubdomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "api", "admin", "app", "auth", "billing", "cdn", "docs", "mail", "saas", "smtp", "static", "status", "support"
    };

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return SubdomainRegex().IsMatch(normalized) && !ReservedSubdomains.Contains(normalized);
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex SubdomainRegex();
}