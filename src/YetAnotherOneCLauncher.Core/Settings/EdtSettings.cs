namespace YetAnotherOneCLauncher.Core.Settings;

/// <summary>Проекты 1C:EDT в списке баз (из EDT Start).</summary>
public sealed class EdtSettings
{
    /// <summary>Показывать группу «Проекты 1C:EDT» в дереве баз.</summary>
    public bool ShowProjects { get; set; } = true;

    /// <summary>
    /// В какой версии EDT открывать проект, чьей версии на компьютере уже нет: идентификатор проекта →
    /// идентификатор установленной версии (выбор пользователя).
    /// </summary>
    public Dictionary<string, string> ProjectInstallations { get; set; } = [];
}
