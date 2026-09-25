namespace YetAnotherOneCLauncher.Core.Catalog;

public enum CatalogWarningLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>Замечание при загрузке списков: недоступный файл, битая секция, дубликат и т. п.</summary>
public sealed record CatalogWarning(CatalogWarningLevel Level, string Message, string? Location = null)
{
    public override string ToString() =>
        Location is null ? $"[{Level}] {Message}" : $"[{Level}] {Message} ({Location})";
}
