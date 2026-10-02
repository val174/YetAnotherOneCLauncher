using YetAnotherOneCLauncher.Core.Settings;

namespace YetAnotherOneCLauncher.Core.Tests;

/// <summary>Средства администрирования: строка запуска, значок в настройках, значки сайта из разметки.</summary>
public class AdminToolsTests
{
    [Theory]
    [InlineData("https://pusk.example/", AdminToolKind.WebService)]
    [InlineData("  http://srv:8080/console ", AdminToolKind.WebService)]
    [InlineData(@"C:\Program Files\1cv8\common\1cestart.exe", AdminToolKind.Application)]
    [InlineData("mmc.exe compmgmt.msc", AdminToolKind.Application)]
    [InlineData("ftp://files.example", AdminToolKind.Application)]
    public void Target_is_web_service_only_for_http_address(string target, AdminToolKind kind) =>
        Assert.Equal(kind, AdminToolTarget.KindOf(target));

    [Fact]
    public void Program_path_is_quoted_part_or_whole_string()
    {
        Assert.Equal((@"C:\Program Files\x\tool.exe", "-a \"b c\""),
            AdminToolTarget.SplitProgram(@" ""C:\Program Files\x\tool.exe"" -a ""b c"" "));
        Assert.Equal((@"C:\Program Files\x\tool.exe", string.Empty), AdminToolTarget.SplitProgram(@"C:\Program Files\x\tool.exe"));
        Assert.Equal((@"C:\x\tool.exe", string.Empty), AdminToolTarget.SplitProgram(@"""C:\x\tool.exe"));

        Environment.SetEnvironmentVariable("YAOCL_TEST_DIR", @"D:\Tools");
        Assert.Equal(@"D:\Tools\a.exe", AdminToolTarget.SplitProgram(@"%YAOCL_TEST_DIR%\a.exe").Program);
    }

    [Fact]
    public void Icon_value_is_auto_built_in_or_own_file_without_path()
    {
        Assert.Equal("pusk", AdminToolIcon.BuiltInId(AdminToolIcon.BuiltIn("pusk")));
        Assert.Null(AdminToolIcon.BuiltInId(null));
        Assert.Equal("a1.png", AdminToolIcon.FileName(AdminToolIcon.File("a1.png")));
        Assert.Null(AdminToolIcon.FileName(@"file:..\..\secret.png")); // из каталога значков не выйти
        Assert.Null(AdminToolIcon.FileName("file:"));
        Assert.Null(AdminToolIcon.FileName(AdminToolIcon.BuiltIn("pusk")));
    }

    [Fact]
    public void Favicons_come_from_page_links_then_favicon_ico()
    {
        const string html = """
            <html><head>
            <link rel="stylesheet" href="/site.css">
            <link rel="apple-touch-icon" href="/apple.png">
            <link rel="icon" type="image/svg+xml" href="/icon.svg">
            <LINK REL='shortcut icon' HREF='static/fav.ico?v=2&amp;x=1'>
            <link href="https://cdn.example/i.png" rel="icon" sizes="32x32">
            <link rel="mask-icon" href="/mask.png">
            </head></html>
            """;
        var page = new Uri("https://pusk.example/app/index.html");

        Assert.Equal(
            [
                "https://pusk.example/app/static/fav.ico?v=2&x=1",
                "https://cdn.example/i.png",
                "https://pusk.example/apple.png",
                "https://pusk.example/favicon.ico",
            ],
            AdminToolTarget.FaviconCandidates(html, page).Select(u => u.ToString()));

        Assert.Equal(["https://pusk.example/favicon.ico"], AdminToolTarget.FaviconCandidates(null, page).Select(u => u.ToString()));
    }

    [Fact]
    public async Task Tools_are_saved_in_settings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "yaocl-tools-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(directory);
            var settings = new LauncherSettings
            {
                AdminTools =
                [
                    new AdminTool { Name = "ПУСК", Target = "https://pusk.example/", Icon = AdminToolIcon.BuiltIn("pusk") },
                    new AdminTool { Name = "Управление компьютером", Target = "mmc.exe compmgmt.msc" },
                ],
            };
            await store.SaveAsync(settings);

            Assert.Equal(settings.AdminTools, (await store.LoadAsync()).Settings.AdminTools);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
