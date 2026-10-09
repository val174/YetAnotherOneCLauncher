using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Оттенки светлой и тёмной темы: настройки с превью, применение к окнам.</summary>
public class ThemeShadeTests
{
    // Палитры Fluent — объекты Avalonia: создавать их можно только в потоке Avalonia, иначе обычный тест привяжет
    // к своему потоку диспетчер, и следующие тесты окон упадут (как было на CI в Linux).
    [AvaloniaFact]
    public void Shades_with_current_ones_first_and_defaults()
    {
        Assert.Equal(["Белый", "Тёплый", "Светло-серый", "Голубоватый"], ThemeShades.Light.Select(s => s.Name));
        Assert.Equal(["Чёрный", "Графит", "Нейтральный серый", "Сине-серый"], ThemeShades.Dark.Select(s => s.Name));
        Assert.Equal((LightShade.White, DarkShade.Graphite), (new UiSettings().LightShade, new UiSettings().DarkShade));

        // «Белый» и «Чёрный» — стандартные палитры Fluent, как раньше.
        Assert.Null(ThemeShades.Palette(LightShade.White));
        Assert.Null(ThemeShades.Palette(DarkShade.Black));
        var warm = ThemeShades.Palette(LightShade.Warm)!;
        Assert.Equal((Color.Parse("#F7F5EF"), Colors.White), (warm.RegionColor, warm.AltHigh)); // фон тонирован, поля — белые
        Assert.Equal(Color.Parse("#22272E"), ThemeShades.Palette(DarkShade.Slate)!.RegionColor);
    }

    [Fact]
    public async Task Shades_are_applied_on_open_and_saved_from_settings()
    {
        using var fixture = new ViewModelFixture();
        await fixture.LoadAsync();
        var vm = fixture.ViewModel;
        await vm.InitializeAsync();
        Assert.Equal((LightShade.White, DarkShade.Graphite), fixture.Shell.AppliedShades);

        fixture.Dialogs.SettingsEditor = settings =>
        {
            settings.LightShadeIndex = (int)LightShade.Warm;
            settings.DarkShadeIndex = (int)DarkShade.Slate;
            Assert.Equal(("Тёплый", "Сине-серый"), (settings.LightShadePreview.Name, settings.DarkShadePreview.Name));
            Assert.True(settings.IsAppearanceDirty);
            return true;
        };
        await vm.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal((LightShade.Warm, DarkShade.Slate), (fixture.Settings.Settings.Ui.LightShade, fixture.Settings.Settings.Ui.DarkShade));
        Assert.Equal((LightShade.Warm, DarkShade.Slate), fixture.Shell.AppliedShades);
    }

    [AvaloniaFact]
    public void Applying_shades_recolors_open_windows_of_both_themes()
    {
        var app = Application.Current!;
        var dark = new Window { RequestedThemeVariant = ThemeVariant.Dark, Width = 200, Height = 100 };
        var light = new Window { RequestedThemeVariant = ThemeVariant.Light, Width = 200, Height = 100 };
        dark.Show();
        light.Show();
        static Color Region(Window w) => w.TryFindResource("SystemRegionColor", w.ActualThemeVariant, out var value) ? (Color)value! : default;
        try
        {
            ThemeShades.Apply(app, LightShade.Warm, DarkShade.Graphite);
            Assert.Equal((Color.Parse("#F7F5EF"), Color.Parse("#262624")), (Region(light), Region(dark)));
            ThemeShades.Apply(app, LightShade.Cool, DarkShade.Slate);
            Assert.Equal((Color.Parse("#F2F5F9"), Color.Parse("#22272E")), (Region(light), Region(dark)));
            Assert.Single(app.Styles.OfType<FluentTheme>()); // тема заменяется, а не добавляется
        }
        finally
        {
            ThemeShades.Apply(app, LightShade.White, DarkShade.Black); // остальным тестам — стандартная тема
            dark.Close();
            light.Close();
        }

        Assert.Equal(Color.Parse("#000000"), Region(dark));
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Appearance_tab_shows_both_shade_choices_with_previews(string theme)
    {
        var window = new SettingsWindow(new SettingsViewModel(new SettingsValues()))
        {
            RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        window.Show();
        window.FindControl<TabControl>("Tabs")!.SelectedItem = window.FindControl<TabItem>("AppearanceTab");
        MainWindowTests.Render();

        Color Preview(string name) =>
            ((ISolidColorBrush)window.FindControl<ContentControl>(name)!.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("shadePreview")).Background!).Color;
        var lightBox = window.FindControl<ComboBox>("LightShadeBox")!;
        var darkBox = window.FindControl<ComboBox>("DarkShadeBox")!;
        Assert.Equal((4, 4), (lightBox.ItemCount, darkBox.ItemCount));
        Assert.Equal((Color.Parse("#FFFFFF"), Color.Parse("#262624")), (Preview("LightShadePreview"), Preview("DarkShadePreview")));

        // Как на эскизе: светлая — слева, тёмная — справа, превью — под своим списком.
        Assert.True(lightBox.TranslatePoint(default, window)!.Value.X < darkBox.TranslatePoint(default, window)!.Value.X);
        Assert.True(window.FindControl<ContentControl>("DarkShadePreview")!.TranslatePoint(default, window)!.Value.Y > darkBox.TranslatePoint(default, window)!.Value.Y);

        lightBox.SelectedIndex = (int)LightShade.Warm;
        darkBox.SelectedIndex = (int)DarkShade.Slate;
        MainWindowTests.Render();
        Assert.Equal((Color.Parse("#F7F5EF"), Color.Parse("#22272E")), (Preview("LightShadePreview"), Preview("DarkShadePreview")));
        MainWindowTests.Snapshot(window, "54-shades-settings-" + theme);
        window.Close();
    }
}
