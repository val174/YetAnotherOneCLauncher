using Avalonia;
using Avalonia.Controls;

namespace YetAnotherOneCLauncher.App.Controls;

/// <summary>
/// Чередование цвета строк списка баз: строка (TreeViewItem, ListBoxItem) с <see cref="IsStripeProperty"/> получает класс
/// <c>stripe</c>, а цвет подложки задаёт окно по заметности (классы stripeSubtle, stripeModerate, stripeStrong).
/// Свойство привязывается стилем контейнера — класс стилем не привяжешь.
/// </summary>
public static class RowStripe
{
    public const string StripeClass = "stripe";

    public static readonly AttachedProperty<bool> IsStripeProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsStripe", typeof(RowStripe));

    static RowStripe() =>
        IsStripeProperty.Changed.AddClassHandler<Control>((control, e) => control.Classes.Set(StripeClass, e.NewValue is true));

    public static bool GetIsStripe(Control control) => control.GetValue(IsStripeProperty);

    public static void SetIsStripe(Control control, bool value) => control.SetValue(IsStripeProperty, value);
}
