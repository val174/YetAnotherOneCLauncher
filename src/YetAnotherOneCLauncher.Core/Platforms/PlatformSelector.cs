namespace YetAnotherOneCLauncher.Core.Platforms;

/// <summary>Откуда взята маска версии.</summary>
public enum VersionMaskSource
{
    /// <summary>Маска не задана: берётся самая новая платформа.</summary>
    None,

    /// <summary>Ключ <c>Version</c> базы в .v8i.</summary>
    InfoBase,

    /// <summary><c>DefaultVersion</c> из 1cestart.cfg.</summary>
    StarterDefault,

    /// <summary>Версия, выбранная пользователем для базы в лаунчере.</summary>
    UserOverride,
}

public enum PlatformSelectionStatus
{
    /// <summary>Подходящая платформа найдена.</summary>
    Selected,

    /// <summary>Платформы по маске нет, но есть другая: запускать на ней можно только с согласия пользователя.</summary>
    MaskNotInstalled,

    /// <summary>Нет ни одной платформы с нужным исполняемым файлом.</summary>
    NothingInstalled,
}

/// <summary>Результат выбора платформы.</summary>
/// <param name="Status">Итог выбора.</param>
/// <param name="Installation">
/// Выбранная платформа; при <see cref="PlatformSelectionStatus.MaskNotInstalled"/> — предлагаемая замена.
/// </param>
/// <param name="Mask">Маска, по которой выбирали (если была).</param>
/// <param name="MaskSource">Откуда взята маска.</param>
/// <param name="Warnings">Замечания: например, маска не разобрана и проигнорирована.</param>
public sealed record PlatformSelection(
    PlatformSelectionStatus Status,
    PlatformInstallation? Installation,
    VersionMask? Mask,
    VersionMaskSource MaskSource,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Выбирает платформу для запуска: по маске <c>Version</c> базы, иначе по <c>DefaultVersion</c> стартера,
/// иначе самую новую. Среди подходящих — самая новая; при равных версиях — предпочтительной разрядности.
/// </summary>
public static class PlatformSelector
{
    public static PlatformSelection Select(
        IEnumerable<PlatformInstallation> installations,
        PlatformExecutable executable,
        string? infoBaseVersion,
        string? starterDefaultVersion,
        PlatformArchitecture preferredArchitecture = PlatformArchitecture.X64,
        string? userOverrideVersion = null,
        bool architectureRequired = false)
    {
        ArgumentNullException.ThrowIfNull(installations);

        var warnings = new List<string>();
        // Разрядность, обязательная для базы (AppArch=x86 или x86_64): платформы другой разрядности не подходят.
        var candidates = installations
            .Where(i => i.Has(executable) && (!architectureRequired || i.Architecture == preferredArchitecture))
            .ToList();

        var (mask, source) = ResolveMask(userOverrideVersion, infoBaseVersion, starterDefaultVersion, warnings);
        if (candidates.Count == 0)
        {
            return new PlatformSelection(PlatformSelectionStatus.NothingInstalled, null, mask, source, warnings);
        }

        if (mask is null)
        {
            return new PlatformSelection(
                PlatformSelectionStatus.Selected, Best(candidates, preferredArchitecture), null, source, warnings);
        }

        var matching = candidates.Where(i => mask.Matches(i.Version)).ToList();
        return matching.Count > 0
            ? new PlatformSelection(PlatformSelectionStatus.Selected, Best(matching, preferredArchitecture), mask, source, warnings)
            : new PlatformSelection(PlatformSelectionStatus.MaskNotInstalled, Nearest(candidates, mask, preferredArchitecture), mask, source, warnings);
    }

    /// <summary>
    /// Замена для отсутствующей версии — из той же ветки: для 8.3.24.1467 сначала 8.3.24.x, затем 8.3.x.x.
    /// Самая новая вообще (например, 8.5) — только если ветки нет: переход на другую ветку платформы
    /// может потребовать конвертации базы.
    /// </summary>
    private static PlatformInstallation Nearest(
        List<PlatformInstallation> candidates,
        VersionMask mask,
        PlatformArchitecture preferred)
    {
        for (var length = mask.Length - 1; length >= 1; length--)
        {
            var prefix = mask.Truncate(length);
            var sameBranch = candidates.Where(i => prefix.Matches(i.Version)).ToList();
            if (sameBranch.Count > 0)
            {
                return Best(sameBranch, preferred);
            }
        }

        return Best(candidates, preferred);
    }

    private static (VersionMask? Mask, VersionMaskSource Source) ResolveMask(
        string? userOverrideVersion,
        string? infoBaseVersion,
        string? starterDefaultVersion,
        List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(userOverrideVersion))
        {
            if (VersionMask.TryParse(userOverrideVersion, out var mask))
            {
                return (mask, VersionMaskSource.UserOverride);
            }

            warnings.Add($"Выбранная для базы версия платформы «{userOverrideVersion}» не распознана и не учитывается.");
        }

        if (!string.IsNullOrWhiteSpace(infoBaseVersion))
        {
            if (VersionMask.TryParse(infoBaseVersion, out var mask))
            {
                return (mask, VersionMaskSource.InfoBase);
            }

            warnings.Add($"Версия платформы у базы «{infoBaseVersion}» не распознана и не учитывается.");
        }

        if (!string.IsNullOrWhiteSpace(starterDefaultVersion))
        {
            if (VersionMask.TryParse(starterDefaultVersion, out var mask))
            {
                return (mask, VersionMaskSource.StarterDefault);
            }

            warnings.Add($"Версия по умолчанию «{starterDefaultVersion}» из 1cestart.cfg не распознана и не учитывается.");
        }

        return (null, VersionMaskSource.None);
    }

    private static PlatformInstallation Best(List<PlatformInstallation> candidates, PlatformArchitecture preferred) =>
        candidates
            .OrderByDescending(i => i.Version)
            .ThenBy(i => i.Architecture == preferred ? 0 : 1)
            .First();
}
