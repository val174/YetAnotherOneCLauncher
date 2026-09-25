using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace YetAnotherOneCLauncher.Core.Platforms;

/// <summary>Полная версия платформы 1С: четыре числа, например 8.3.24.1548.</summary>
public readonly record struct PlatformVersion(int Major, int Minor, int Release, int Build)
    : IComparable<PlatformVersion>
{
    public static bool operator <(PlatformVersion left, PlatformVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(PlatformVersion left, PlatformVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(PlatformVersion left, PlatformVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(PlatformVersion left, PlatformVersion right) => left.CompareTo(right) >= 0;

    /// <summary>Разбирает версию из ровно четырёх неотрицательных чисел через точку.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out PlatformVersion version)
    {
        version = default;
        if (!VersionParts.TryParse(text, out var parts) || parts.Length != 4)
        {
            return false;
        }

        version = new PlatformVersion(parts[0], parts[1], parts[2], parts[3]);
        return true;
    }

    public static PlatformVersion Parse(string text) =>
        TryParse(text, out var version)
            ? version
            : throw new FormatException($"Некорректная версия платформы: «{text}».");

    /// <summary>Числовое сравнение: 8.5.1.189 младше 8.5.1.1150.</summary>
    public int CompareTo(PlatformVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result == 0)
        {
            result = Minor.CompareTo(other.Minor);
        }

        if (result == 0)
        {
            result = Release.CompareTo(other.Release);
        }

        return result != 0 ? result : Build.CompareTo(other.Build);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Release}.{Build}");
}

/// <summary>
/// Маска версии из ключа <c>Version</c> в .v8i или <c>DefaultVersion</c> в 1cestart.cfg:
/// "8.3" — любая 8.3.x.x, "8.3.24" — любая сборка 8.3.24, "8.3.24.1548" — точная версия.
/// </summary>
public sealed class VersionMask
{
    private readonly int[] _parts;

    private VersionMask(int[] parts)
    {
        _parts = parts;
    }

    /// <summary>Число заданных частей: от 1 до 4.</summary>
    public int Length => _parts.Length;

    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out VersionMask? mask)
    {
        mask = null;
        if (!VersionParts.TryParse(text, out var parts) || parts.Length > 4)
        {
            return false;
        }

        mask = new VersionMask(parts);
        return true;
    }

    /// <summary>Маска из первых <paramref name="length"/> частей: для 8.3.24.1548 и 2 — "8.3".</summary>
    public VersionMask Truncate(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, Length);
        return length == Length ? this : new VersionMask(_parts[..length]);
    }

    public bool Matches(PlatformVersion version)
    {
        ReadOnlySpan<int> actual = [version.Major, version.Minor, version.Release, version.Build];
        for (var i = 0; i < _parts.Length; i++)
        {
            if (_parts[i] != actual[i])
            {
                return false;
            }
        }

        return true;
    }

    public override string ToString() => string.Join('.', _parts.Select(p => p.ToString(CultureInfo.InvariantCulture)));
}

internal static class VersionParts
{
    public static bool TryParse(string? text, out int[] parts)
    {
        parts = [];
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var pieces = text.Trim().Split('.');
        var result = new int[pieces.Length];
        for (var i = 0; i < pieces.Length; i++)
        {
            if (pieces[i].Length == 0
                || !pieces[i].All(char.IsAsciiDigit)
                || !int.TryParse(pieces[i], NumberStyles.None, CultureInfo.InvariantCulture, out result[i]))
            {
                return false;
            }
        }

        parts = result;
        return true;
    }
}
