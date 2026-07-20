namespace ContentWriter.Application.Services;

/// <summary>The fixed set of department/category slugs shared by project creation, the categories list, and publish.</summary>
public static class Departments
{
    public static readonly IReadOnlyList<string> Slugs =
    [
        "accounting",
        "customer-service",
        "human-resource",
        "marketing",
        "sales",
    ];

    public static bool IsValid(string? slug) =>
        !string.IsNullOrWhiteSpace(slug) && Slugs.Contains(slug, StringComparer.OrdinalIgnoreCase);

    public static string DisplayName(string slug) => slug switch
    {
        "accounting" => "Accounting",
        "customer-service" => "Customer Service",
        "human-resource" => "Human Resource",
        "marketing" => "Marketing",
        "sales" => "Sales",
        _ => slug,
    };
}
