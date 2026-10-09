using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Оттенок тёмной темы: настройка с превью, применение к окнам (по умолчанию — «Графит»).</summary>
public class DarkShadeTests
{
    [Fact]
    public void Shades_are_current_black_and_three_softer_ones_graphite_by_default()
    {
        Assert.Equal(["Чёрный", "Графит", "Нейтральный серый", "Сине-серый"], DarkShades.All.Select(s => s.Name));
        Assert.Equal(DarkShade.Graphite, new UiSettings().DarkShade);
        Assert.Equal(Color.Parse("#262624"), DarkShades.Of(DarkShade.Graphite).Region);
        Assert.Null(DarkShades.Palette(DarkShade.Black)); // «Чёрный» — стандартная тёмная тема Fluent, как раньше
        Assert.Equal(Color.Parse("#22272E"), DarkShades.Palette(DarkShade.Slate)!.RegionColor);
    }

    [Fact]
    public async Task Shade_is_applied_on_open_and_saved_from_settings()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        await vm.InitializeAsync();
        Assert.Equal(DarkShade.Graphite, fixture.Shell.AppliedDarkShade);

        fixture.Dialogs.SettingsEditor = settings =>
        {
            Assert.Equal((int)DarkShade.Graphite, settings.DarkShadeIndex);
            settings.DarkShadeIndex = (int)DarkShade.Slate;
            Assert.Equal("Сине-серый", settings.DarkShadePreview.Name); // превью — выбранный оттенок
            Assert.True(settings.IsAppearanceDirty);
            return true;
        };
        await vm.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(DarkShade.Slate, fixture.Settings.Settings.Ui.DarkShade);
        Assert.Equal(DarkShade.Slate, fixture.Shell.AppliedDarkShade);
    }

    [AvaloniaFact]
    public void Applying_shade_recolors_open_dark_windows()
    {
        var app = Application.Current!;
        var window = new Window { RequestedThemeVariant = ThemeVariant.Dark, Width = 200, Height = 100 };
        window.Show();
        Color Region() => window.TryFindResource("SystemRegionColor", window.ActualThemeVariant, out var value) ? (Color)value! : default;
        try
        {
            DarkShades.Apply(app, DarkShade.Graphite);
            Assert.Equal(Color.Parse("#262624"), Region());
            DarkShades.Apply(app, DarkShade.Slate);
            Assert.Equal(Color.Parse("#22272E"), Region());
            Assert.Single(app.Styles.OfType<FluentTheme>()); // тема заменяется, а не добавляется
        }
        finally
        {
            DarkShades.Apply(app, DarkShade.Black); // остальным тестам — стандартная тема
            window.Close();
        }

        Assert.Equal(Color.Parse("#000000"), Region());
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Appearance_tab_shows_shade_choice_and_preview(string theme)
    {
        var window = new SettingsWindow(new SettingsViewModel(new SettingsValues()))
        {
            RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        window.Show();
        window.FindControl<TabControl>("Tabs")!.SelectedItem = window.FindControl<TabItem>("AppearanceTab");
        MainWindowTests.Render();

        var box = window.FindControl<ComboBox>("DarkShadeBox")!;
        Assert.Equal(4, box.ItemCount);
        Assert.Equal((int)DarkShade.Graphite, box.SelectedIndex);
        var preview = window.FindControl<Border>("DarkShadePreview")!;
        Assert.Equal(Color.Parse("#262624"), ((ISolidColorBrush)preview.Background!).Color);

        box.SelectedIndex = (int)DarkShade.Slate;
        MainWindowTests.Render();
        Assert.Equal(Color.Parse("#22272E"), ((ISolidColorBrush)preview.Background!).Color);
        MainWindowTests.Snapshot(window, "52-dark-shade-settings-" + theme);
        window.Close();
    }
}
