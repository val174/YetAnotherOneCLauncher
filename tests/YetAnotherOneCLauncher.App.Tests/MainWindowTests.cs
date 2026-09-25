using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>
/// Окно в headless-режиме: разметка, привязки и клавиатура.
/// Если задана переменная YAOCL_SCREENSHOTS, кадры окна сохраняются туда в PNG — для просмотра вёрстки.
/// </summary>
public class MainWindowTests
{
    [AvaloniaFact]
    public async Task Opens_with_tree_and_focused_search()
    {
        using var fixture = new ViewModelFixture();
        var window = await OpenAsync(fixture);

        var tree = window.FindControl<TreeView>("CatalogTree")!;
        Assert.True(tree.IsEffectivelyVisible);
        Assert.Equal(3, tree.ItemCount);
        Assert.True(window.FindControl<TextBox>("SearchBox")!.IsFocused);

        Snapshot(window, "01-tree");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Typing_searches_and_enter_launches_best_match()
    {
        using var fixture = new ViewModelFixture();
        var window = await OpenAsync(fixture);

        window.KeyTextInput(",e["); // «бух» в английской раскладке
        Render();

        var list = window.FindControl<ListBox>("CatalogList")!;
        Assert.True(list.IsEffectivelyVisible);
        Assert.Equal(2, list.ItemCount);
        Snapshot(window, "02-search");

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await WaitAsync(() => fixture.Processes.Started.Count > 0);

        Assert.Equal("ENTERPRISE", Assert.Single(fixture.Processes.Started).Arguments[0]);
        Assert.Equal("Бухгалтерия предприятия", fixture.Settings.Settings.History.Single().InfoBase.Name);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Ctrl_enter_launches_designer_and_ctrl_d_toggles_favorite()
    {
        using var fixture = new ViewModelFixture();
        var window = await OpenAsync(fixture);
        window.KeyTextInput("зуп");
        Render();

        window.KeyPress(Key.D, RawInputModifiers.Control, PhysicalKey.D, "d");
        window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null);
        await WaitAsync(() => fixture.Processes.Started.Count > 0);

        Assert.Equal("DESIGNER", Assert.Single(fixture.Processes.Started).Arguments[0]);
        Assert.True(fixture.ViewModel.InfoBases.Single(b => b.Name == "Зарплата и управление персоналом").IsFavorite);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Render();
        Assert.True(fixture.ViewModel.ShowTree);
        Snapshot(window, "03-favorites-and-recent");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Dark_theme_list_mode_snapshot()
    {
        using var fixture = new ViewModelFixture();
        var window = await OpenAsync(fixture);

        fixture.ViewModel.ThemeIndex = (int)ThemeMode.Dark;
        Assert.Equal(ThemeMode.Dark, fixture.Shell.AppliedTheme);
        Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; // вместо поддельной службы темы
        fixture.ViewModel.IsTreeMode = false;
        fixture.ViewModel.SelectedListItem = fixture.ViewModel.ListItems[1];
        Render();

        Assert.True(window.FindControl<ListBox>("CatalogList")!.IsEffectivelyVisible);
        Snapshot(window, "04-dark-list");
        window.Close();
    }

    private static async Task<MainWindow> OpenAsync(ViewModelFixture fixture)
    {
        // Настоящее окно применяет тему через Application; в тестах — через подделку, поэтому ставим вручную.
        Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        var window = new MainWindow(fixture.ViewModel) { Width = 1040, Height = 560 };
        window.Show();
        await WaitAsync(() => fixture.ViewModel.InfoBases.Count > 0);
        Render();
        return window;
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "Условие не выполнилось за отведённое время.");
    }

    private static void Render()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Snapshot(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("YAOCL_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        Render();
        window.CaptureRenderedFrame()?.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
