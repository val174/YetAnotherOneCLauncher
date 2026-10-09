using System.Globalization;

namespace YetAnotherOneCLauncher.Core.Updates;

/// <summary>
/// Версия программы или тега релиза: <c>0.2.0</c>, <c>v0.2.0</c>, <c>0.2</c>, <c>0.2.0-beta.1</c>, <c>0.2.0+abc123</c>.
/// Сравнение — по числам; версия с суффиксом (<c>-beta.1</c>) младше той же версии без него.
/// </summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
{
    private ReleaseVersion(int major, int minor, int patch, int revision, string? preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Revision = revision;
        PreRelease = preRelease;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public int Revision { get; }

    /// <summary>Суффикс после «-»: <c>beta.1</c>; <c>null</c> — обычная версия.</summary>
    public string? PreRelease { get; }

    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = null!;
        var value = text?.Trim() ?? string.Empty;
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        // «+хеш сборки» не влияет на версию.
        var plus = value.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            value = value[..plus];
        }

        string? preRelease = null;
        var dash = value.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            preRelease = value[(dash + 1)..];
            value = value[..dash];
            if (preRelease.Length == 0)
            {
                return false;
            }
        }

        var parts = value.Split('.');
        if (parts.Length is < 1 or > 4)
        {
            return false;
        }

        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = new ReleaseVersion(numbers[0], numbers[1], numbers[2], numbers[3], preRelease);
        return true;
    }

    public static ReleaseVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"Не версия: «{text}».");

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byNumbers = (Major, Minor, Patch, Revision).CompareTo((other.Major, other.Minor, other.Patch, other.Revision));
        if (byNumbers != 0)
        {
            return byNumbers;
        }

        return (PreRelease, other.PreRelease) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            _ => string.CompareOrdinal(PreRelease, other.PreRelease),
        };
    }

    public bool Equals(ReleaseVersion? other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is ReleaseVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Revision, PreRelease);

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    public static bool operator ==(ReleaseVersion? left, ReleaseVersion? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ReleaseVersion? left, ReleaseVersion? right) => !(left == right);

    /// <summary>«0.2.0», «0.2.0.1», «0.2.0-beta.1» — без лишних нулей в четвёртой позиции.</summary>
    public override string ToString()
    {
        var text = Revision == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}.{Revision}");
        return PreRelease is null ? text : text + "-" + PreRelease;
    }
}
