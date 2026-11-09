namespace MiniWorkflowEngine.Domain;

public static class ApplicationRoles
{
    public const string Administrator = "Administrator";
    public const string Approver = "Approver";
    public const string Employee = "Employee";

    public static string DisplayName(string role) => role switch
    {
        Administrator => "Yönetici",
        Approver => "Onaylayıcı",
        Employee => "Çalışan",
        _ => role
    };

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { Administrator, Approver, Employee });
}
