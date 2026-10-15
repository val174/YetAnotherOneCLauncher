using System.Globalization;

namespace YetAnotherOneCLauncher.Core.Formatting;

/// <summary>
/// «Сколько прошло»: «только что», «5 минут назад», «3 часа назад», «2 дня назад», «4 месяца назад», «2 года назад».
/// Месяцы и годы — по календарю (с 15 сентября по 14 октября — ещё дни). Время в будущем (часы другой машины спешили) —
/// «только что».
/// </summary>
public static class RelativeTime
{
    public static string Format(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "только что";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return Ago((int)elapsed.TotalMinutes, "минуту", "минуты", "минут");
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return Ago((int)elapsed.TotalHours, "час", "часа", "часов");
        }

        // Месяцы — по календарю в местном времени: сколько раз можно прибавить месяц, не перескочив «сейчас».
        var from = at.ToOffset(now.Offset).DateTime;
        var to = now.DateTime;
        var months = ((to.Year - from.Year) * 12) + to.Month - from.Month;
        if (months > 0 && from.AddMonths(months) > to)
        {
            months--;
        }

        if (months < 1)
        {
            return Ago(Math.Max(1, (int)elapsed.TotalDays), "день", "дня", "дней");
        }

        return months < 12
            ? Ago(months, "месяц", "месяца", "месяцев")
            : Ago(months / 12, "год", "года", "лет");
    }

    /// <summary>Точные дата и время — для подсказки.</summary>
    public static string Exact(DateTimeOffset at) =>
        at.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string Ago(int count, string one, string few, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {Plural(count, one, few, many)} назад");

    /// <summary>1 минуту, 2 минуты, 5 минут, 11 минут, 21 минуту.</summary>
    private static string Plural(int count, string one, string few, string many)
    {
        var lastTwo = count % 100;
        var last = count % 10;
        return lastTwo is >= 11 and <= 14 ? many
            : last == 1 ? one
            : last is >= 2 and <= 4 ? few
            : many;
    }
}
