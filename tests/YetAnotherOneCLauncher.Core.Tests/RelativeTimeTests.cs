using YetAnotherOneCLauncher.Core.Formatting;

namespace YetAnotherOneCLauncher.Core.Tests;

/// <summary>«5 минут назад» и т. п. (задача #29).</summary>
public class RelativeTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.FromHours(3));

    [Theory]
    [InlineData(0, "только что")]
    [InlineData(59, "только что")]
    [InlineData(-3600, "только что")] // в будущем
    [InlineData(60, "1 минуту назад")]
    [InlineData(2 * 60, "2 минуты назад")]
    [InlineData(5 * 60, "5 минут назад")]
    [InlineData(11 * 60, "11 минут назад")]
    [InlineData(21 * 60, "21 минуту назад")]
    [InlineData(22 * 60, "22 минуты назад")]
    [InlineData(59 * 60 + 59, "59 минут назад")]
    [InlineData(3600, "1 час назад")]
    [InlineData(3 * 3600, "3 часа назад")]
    [InlineData(23 * 3600 + 3599, "23 часа назад")]
    [InlineData(24 * 3600, "1 день назад")]
    [InlineData(2 * 86400, "2 дня назад")]
    [InlineData(15 * 86400, "15 дней назад")]
    public void Seconds_ago(int seconds, string expected) =>
        Assert.Equal(expected, RelativeTime.Format(Now.AddSeconds(-seconds), Now));

    [Theory]
    [InlineData("2026-09-15T12:00:01+03:00", "29 дней назад")] // месяц ещё не прошёл
    [InlineData("2026-09-15T12:00:00+03:00", "1 месяц назад")]
    [InlineData("2026-07-01T09:00:00+03:00", "3 месяца назад")]
    [InlineData("2025-11-01T09:00:00+03:00", "11 месяцев назад")]
    [InlineData("2025-10-15T12:00:00+03:00", "1 год назад")]
    [InlineData("2024-01-01T00:00:00+03:00", "2 года назад")]
    [InlineData("2021-05-05T00:00:00+03:00", "5 лет назад")]
    [InlineData("2026-09-15T09:00:00+00:00", "1 месяц назад")] // другой пояс — по местному времени «сейчас»
    public void Calendar_months_and_years(string at, string expected) =>
        Assert.Equal(expected, RelativeTime.Format(DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture), Now));
}
