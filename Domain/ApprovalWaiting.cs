namespace MiniWorkflowEngine.Domain;

public static class ApprovalWaiting
{
    public const int OverdueHours = 24;

    public static bool IsOverdue(DateTimeOffset since, DateTimeOffset now)
        => since <= now.AddHours(-OverdueHours);

    public static string Duration(DateTimeOffset since, DateTimeOffset now)
    {
        var elapsed = now - since;
        if (elapsed.TotalDays >= 1) return $"{(int)elapsed.TotalDays} gün {elapsed.Hours} sa.";
        if (elapsed.TotalHours >= 1) return $"{elapsed.Hours} sa. {elapsed.Minutes} dk.";
        return elapsed.TotalMinutes >= 1 ? $"{elapsed.Minutes} dk." : "1 dakikadan az";
    }
}
