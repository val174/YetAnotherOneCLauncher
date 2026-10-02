using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using YetAnotherOneCLauncher.App.Controls;
using YetAnotherOneCLauncher.App.Services;
using YetAnotherOneCLauncher.App.ViewModels;
using YetAnotherOneCLauncher.Core.Settings;
using YetAnotherOneCLauncher.Platform;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Вкладка настроек «Средства администрирования»: таблица «Инструменты» и значки.</summary>
public class AdminToolsTests
{
    [AvaloniaFact]
    public void Tools_are_added_edited_moved_and_deleted_and_mark_the_tab()
    {
        var form = new SettingsViewModel(new SettingsValues());
        var tools = form.AdminTools;
        Assert.Equal("Средства администрирования", form.AdminToolsHeader);
        Assert.False(tools.ApplyCommand.CanExecute(null)); // нужны имя и строка запуска

        tools.NewName = "ПУСК";
        tools.NewTarget = " https://pusk.example/ ";
        tools.ChooseIconCommand.Execute(AdminToolIcon.BuiltIn("pusk"));
        tools.ApplyCommand.Execute(null);
        tools.NewName = "Управление компьютером";
        tools.NewTarget = "mmc.exe compmgmt.msc";
        tools.ApplyCommand.Execute(null);

        Assert.Equal(
            [
                new AdminTool { Name = "ПУСК", Target = "https://pusk.example/", Icon = "builtin:pusk" },
                new AdminTool { Name = "Управление компьютером", Target = "mmc.exe compmgmt.msc" },
            ],
            form.Result.AdminTools);
        Assert.Equal(("Настройки*", "Средства администрирования*"), (form.Title, form.AdminToolsHeader));
        Assert.Equal(("ПУСК", "Значок программы"), (tools.Rows[0].IconText, tools.Rows[1].IconText));
        Assert.Equal(string.Empty, tools.NewName); // форма — снова для нового

        // Имя не повторяется.
        tools.NewName = "пуск";
        tools.NewTarget = "https://other.example/";
        tools.ApplyCommand.Execute(null);
        Assert.Equal("Инструмент с таким именем уже есть.", tools.ErrorText);
        tools.CancelEditCommand.Execute(null);

        // Изменение — на прежнем месте.
        tools.SelectedRow = tools.Rows[0];
        tools.EditCommand.Execute(null);
        Assert.Equal(("ПУСК", "https://pusk.example/", "builtin:pusk"), (tools.NewName, tools.NewTarget, tools.NewIcon));
        Assert.False(tools.DeleteCommand.CanExecute(null)); // изменяемый не удаляется
        tools.ChooseIconCommand.Execute(null);
        tools.NewName = "Сервис ПУСК";
        tools.ApplyCommand.Execute(null);
        Assert.Equal(("Сервис ПУСК", null), (tools.Rows[0].Name, tools.Rows[0].Tool.Icon));
        Assert.Equal("Значок сайта", tools.Rows[0].IconText);

        // Порядок и удаление.
        Assert.False(tools.MoveUpCommand.CanExecute(null));
        tools.MoveDownCommand.Execute(null);
        Assert.Equal(["Управление компьютером", "Сервис ПУСК"], tools.Rows.Select(r => r.Name));
        tools.DeleteCommand.Execute(null);
        Assert.Equal(["Управление компьютером"], tools.Rows.Select(r => r.Name));
        Assert.Same(tools.Rows[0], tools.SelectedRow);
    }

    [AvaloniaFact]
    public void Default_icons_are_web_page_for_service_and_app_badge_for_program()
    {
        var web = AdminToolIconViewModel.For(null, "https://pusk.example/", null);
        Assert.False(web.IsAppBadge);
        Assert.IsType<DrawingImage>(web.Image);

        var program = AdminToolIconViewModel.For(null, @"C:\Tools\tool.exe", null);
        Assert.True(program.IsAppBadge);

        // Встроенные — все с картинкой, кроме «App».
        foreach (var icon in BuiltInToolIcon.All)
        {
            Assert.Equal(icon.Id == BuiltInToolIcon.App, AdminToolIconViewModel.For(AdminToolIcon.BuiltIn(icon.Id), "x", null).IsAppBadge);
        }

        // Свой значок, файла которого нет, — как автоматический.
        Assert.True(AdminToolIconViewModel.For(AdminToolIcon.File("gone.png"), @"C:\Tools\tool.exe", new FakeToolIcons()).IsAppBadge);
    }

    [AvaloniaFact]
    public async Task Auto_icon_replaces_default_when_loaded()
    {
        var icons = new FakeToolIcons();
        var loaded = new TaskCompletionSource<Bitmap?>();
        icons.Auto["https://pusk.example/"] = loaded.Task;
        var form = new SettingsViewModel(
            new SettingsValues { AdminTools = [new AdminTool { Name = "ПУСК", Target = "https://pusk.example/" }] }, toolIcons: icons);
        var icon = form.AdminTools.Rows[0].Icon;
        Assert.IsType<DrawingImage>(icon.Image); // пока грузится — веб-страница

        var bitmap = Png(8, Colors.Red);
        loaded.SetResult(bitmap);
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(bitmap, icon.Image);
    }

    [AvaloniaFact]
    public async Task Own_icon_is_imported_and_unused_ones_are_removed_on_save()
    {
        using var fixture = new ViewModelFixture();
        var iconsDirectory = Path.Combine(fixture.Directory, "tool-icons");
        var store = new AdminToolIconStore(null, new NoFileIconReader(), iconsDirectory, NullLogger<AdminToolIconStore>.Instance);
        var picture = Path.Combine(fixture.Directory, "logo.png");
        File.WriteAllBytes(picture, PngBytes(16, Colors.Green));
        var text = Path.Combine(fixture.Directory, "notes.txt");
        File.WriteAllText(text, "не картинка");

        var form = new SettingsViewModel(new SettingsValues(), toolIcons: store, files: fixture.Files);
        var tools = form.AdminTools;
        fixture.Files.OpenAnswer = text;
        await tools.ImportIconCommand.ExecuteAsync(null);
        Assert.StartsWith("Файл не открывается как картинка", tools.ErrorText, StringComparison.Ordinal);
        Assert.Null(tools.NewIcon);

        fixture.Files.OpenAnswer = picture;
        await tools.ImportIconCommand.ExecuteAsync(null);
        var fileName = AdminToolIcon.FileName(tools.NewIcon);
        Assert.NotNull(fileName);
        Assert.True(File.Exists(Path.Combine(iconsDirectory, fileName))); // копия: исходный файл можно удалить
        Assert.False(tools.NewIconView.IsAppBadge);
        Assert.Equal("Свой значок", tools.NewIconText);

        tools.NewName = "Свой";
        tools.NewTarget = "https://own.example/";
        tools.ApplyCommand.Execute(null);
        Assert.Equal(16, Assert.IsType<Bitmap>(tools.Rows[0].Icon.Image).PixelSize.Width);

        // Загружен ещё один, но инструмент сохранили со старым — лишний файл удаляется при сохранении настроек.
        await tools.ImportIconCommand.ExecuteAsync(null);
        Assert.Equal(2, Directory.GetFiles(iconsDirectory).Length);
        tools.CancelEditCommand.Execute(null);
        RemoveUnusedThroughMainWindow(fixture, form, store);
        Assert.Equal([fileName], Directory.GetFiles(iconsDirectory).Select(Path.GetFileName));
    }

    private static void RemoveUnusedThroughMainWindow(ViewModelFixture fixture, SettingsViewModel form, AdminToolIconStore store)
    {
        // Сохранение настроек: инструменты записываются в настройки, неиспользуемые значки удаляются.
        fixture.ViewModel.ApplySettings(form.Result);
        Assert.Equal(form.Result.AdminTools, fixture.Settings.Settings.AdminTools);
        store.RemoveUnused(fixture.Settings.Settings.AdminTools);
    }

    [AvaloniaFact]
    public async Task Program_icon_and_browse_fill_the_form()
    {
        using var fixture = new ViewModelFixture();
        var reader = new FakeFileIcons();
        var store = new AdminToolIconStore(null, reader, null, NullLogger<AdminToolIconStore>.Instance);
        var program = Path.Combine(fixture.Directory, "Admin Tool.exe");
        File.WriteAllText(program, "MZ");
        var form = new SettingsViewModel(new SettingsValues(), toolIcons: store, files: fixture.Files);
        var tools = form.AdminTools;

        fixture.Files.OpenAnswer = program;
        await tools.BrowseProgramCommand.ExecuteAsync(null);
        Assert.Equal(($"\"{program}\"", "Admin Tool"), (tools.NewTarget, tools.NewName)); // путь с пробелами — в кавычках

        for (var i = 0; i < 200 && tools.NewIconView.IsAppBadge; i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal(program, Assert.Single(reader.Paths));
        Assert.Equal(4, Assert.IsType<Bitmap>(tools.NewIconView.Image).PixelSize.Width);
    }

    [Fact]
    public void Program_without_folder_is_found_in_path()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(Path.Combine(Environment.SystemDirectory, "notepad.exe"), AdminToolIconStore.ResolveProgram("notepad"), ignoreCase: true);
        // Программа с параметрами без кавычек: путь — начало строки до пробела.
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "mmc.exe"), AdminToolIconStore.ResolveProgram("mmc.exe compmgmt.msc"), ignoreCase: true);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "compmgmt.msc"), AdminToolIconStore.ResolveProgram(@"%windir%\system32\compmgmt.msc"), ignoreCase: true);
        Assert.Null(AdminToolIconStore.ResolveProgram(@"C:\нет\такой\программы.exe"));
    }

    [Fact]
    public async Task Windows_reads_program_icon()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var icon = PlatformServices.CreateFileIconReader().Read(Path.Combine(Environment.SystemDirectory, "notepad.exe"));
        Assert.NotNull(icon);
        Assert.Equal((32, 32), (icon.Width, icon.Height));
        Assert.Contains(icon.Bgra.Where((_, i) => i % 4 == 3), a => a == 255); // непрозрачные точки есть
        Assert.Contains(icon.Bgra.Where((_, i) => i % 4 == 3), a => a == 0); // и прозрачные — по краям

        // Значок по типу файла (оснастка .msc) — через расширения оболочки; несколько значков сразу из фоновых потоков
        // (так их грузит таблица) — все прочитаны.
        var reader = PlatformServices.CreateFileIconReader();
        var icons = await Task.WhenAll(new[] { "notepad.exe", "mmc.exe", "compmgmt.msc" }.Select(file =>
            Task.Run(() => reader.Read(Path.Combine(Environment.SystemDirectory, file)), TestContext.Current.CancellationToken)));
        Assert.All(icons, Assert.NotNull);
    }

    [AvaloniaFact]
    public async Task Site_icon_comes_from_page_link_or_favicon_ico()
    {
        var handler = new FakeHttp();
        handler.Pages["https://pusk.example/"] = ("text/html", "<html><head><link rel=\"icon\" href=\"/static/logo.png\"></head></html>"u8.ToArray());
        handler.Pages["https://pusk.example/static/logo.png"] = ("image/png", PngBytes(24, Colors.Blue));
        handler.Pages["https://plain.example/favicon.ico"] = ("image/png", PngBytes(12, Colors.Blue));
        using var http = new HttpClient(handler);
        var store = new AdminToolIconStore(http, new NoFileIconReader(), null, NullLogger<AdminToolIconStore>.Instance);

        Assert.Equal(24, (await store.LoadAutoAsync("https://pusk.example/"))?.PixelSize.Width);
        Assert.Equal(12, (await store.LoadAutoAsync("https://plain.example/"))?.PixelSize.Width); // страницы нет — /favicon.ico
        Assert.Null(await store.LoadAutoAsync("https://none.example/"));

        // Значок сайта скачивается один раз.
        var requests = handler.Requests.Count;
        await store.LoadAutoAsync(" https://pusk.example/ ");
        Assert.Equal(requests, handler.Requests.Count);
    }

    [AvaloniaFact]
    public void Settings_window_has_tab_with_tools_table_and_icon_menu()
    {
        var form = new SettingsViewModel(new SettingsValues
        {
            AdminTools =
            [
                new AdminTool { Name = "ПУСК", Target = "https://pusk.example/", Icon = AdminToolIcon.BuiltIn("pusk") },
                new AdminTool { Name = "Блокнот", Target = "notepad.exe" },
            ],
        });
        var window = new SettingsWindow(form);
        window.Show();
        var tabs = window.FindControl<TabControl>("Tabs")!;
        var tab = window.FindControl<TabItem>("AdminToolsTab")!;
        Assert.Equal("Средства администрирования", tab.Header);
        tabs.SelectedItem = tab;
        Dispatcher.UIThread.RunJobs();
        MainWindowTests.Snapshot(window, "40-admin-tools");

        var view = window.FindControl<AdminToolsView>("AdminToolsView")!;
        var rows = view.FindControl<ListBox>("RowsList")!;
        Assert.Equal(2, rows.ItemCount);

        var menu = AdminToolsView.CreateIconMenu(form.AdminTools);
        var headers = menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToList();
        Assert.Equal(["Автоматически", .. BuiltInToolIcon.All.Select(i => i.Title), "Загрузить свой…"], headers);
        Assert.True(menu.Items.OfType<MenuItem>().First().IsChecked); // у нового — автоматически
        Assert.Contains("ПУСК", headers);
        window.Close();
    }

    private static Bitmap Png(int size, Color color) => new(new MemoryStream(PngBytes(size, color)));

    internal static byte[] PngBytes(int size, Color color)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var buffer = bitmap.Lock())
        {
            var pixels = new byte[size * size * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (color.B, color.G, color.R, 255);
            }

            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private sealed class FakeToolIcons : IAdminToolIconSource
    {
        public Dictionary<string, Task<Bitmap?>> Auto { get; } = [];

        public Task<Bitmap?> LoadAutoAsync(string target) => Auto.GetValueOrDefault(target.Trim()) ?? Task.FromResult<Bitmap?>(null);

        public Bitmap? LoadFile(string fileName) => null;

        public string? Import(string sourcePath) => null;

        public void RemoveUnused(IEnumerable<AdminTool> tools)
        {
        }
    }

    private sealed class FakeFileIcons : IFileIconReader
    {
        public List<string> Paths { get; } = [];

        public IconPixels? Read(string path)
        {
            Paths.Add(path);
            return new IconPixels(4, 4, Enumerable.Repeat((byte)200, 4 * 4 * 4).ToArray());
        }
    }

    private sealed class FakeHttp : HttpMessageHandler
    {
        public Dictionary<string, (string ContentType, byte[] Body)> Pages { get; } = [];

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            if (!Pages.TryGetValue(request.RequestUri!.ToString(), out var page))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
            }

            var content = new ByteArrayContent(page.Body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(page.ContentType);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }
}
