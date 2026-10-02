using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using YetAnotherOneCLauncher.App.ViewModels;

namespace YetAnotherOneCLauncher.App.Controls;

/// <summary>
/// Значок средства администрирования (по умолчанию 20×20): картинка или, если её нет, плашка с надписью «App».
/// Подбираемый сам значок подменяется, когда загрузится.
/// </summary>
public sealed class AdminToolIconView : Panel
{
    public static readonly StyledProperty<AdminToolIconViewModel?> IconProperty =
        AvaloniaProperty.Register<AdminToolIconView, AdminToolIconViewModel?>(nameof(Icon));

    public AdminToolIconView()
    {
        Width = 20;
        Height = 20;
        var image = new Image { Stretch = Stretch.Uniform };
        image.Bind(Image.SourceProperty, new Binding(nameof(AdminToolIconViewModel.Image)));
        var badge = new Border
        {
            Name = "AppBadge",
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 14,
            Child = new TextBlock
            {
                Text = "App",
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        badge.Bind(Border.BackgroundProperty, badge.GetResourceObservable("IconViewBrush"));
        badge.Bind(IsVisibleProperty, new Binding(nameof(AdminToolIconViewModel.IsAppBadge)));
        image.Bind(IsVisibleProperty, new Binding(nameof(AdminToolIconViewModel.IsAppBadge)) { Converter = Avalonia.Data.Converters.BoolConverters.Not });
        Children.Add(image);
        Children.Add(badge);
    }

    public AdminToolIconViewModel? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconProperty)
        {
            foreach (var child in Children)
            {
                child.DataContext = Icon;
            }
        }
    }
}
