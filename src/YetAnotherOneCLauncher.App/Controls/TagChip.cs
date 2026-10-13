using Avalonia;
using Avalonia.Controls;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Controls;

/// <summary>
/// Цвет метки: элемент с <see cref="ColorProperty"/> получает класс <c>tagRed</c>, <c>tagBlue</c> и т. д.; цвета плашки,
/// текста и образца в палитре задают стили приложения (светлая и тёмная темы). Класс стилем не привяжешь — поэтому свойство.
/// </summary>
public static class TagChip
{
    public static readonly AttachedProperty<TagColor> ColorProperty =
        AvaloniaProperty.RegisterAttached<Control, TagColor>("Color", typeof(TagChip));

    static TagChip() =>
        ColorProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            foreach (var color in Enum.GetValues<TagColor>())
            {
                control.Classes.Set(ClassOf(color), Equals(e.NewValue, color));
            }
        });

    public static string ClassOf(TagColor color) => "tag" + color;

    public static TagColor GetColor(Control control) => control.GetValue(ColorProperty);

    public static void SetColor(Control control, TagColor value) => control.SetValue(ColorProperty, value);
}
