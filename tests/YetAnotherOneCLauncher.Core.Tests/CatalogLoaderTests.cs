using YetAnotherOneCLauncher.Core.Catalog;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Text;

namespace YetAnotherOneCLauncher.Core.Tests;

public class CatalogLoaderTests
{
    private static readonly TextFormat Utf16Cfg = new(TextEncodingKind.Utf16LittleEndian, true, "\r\n");

    [Fact]
    public async Task Merges_personal_and_common_lists()
    {
        using var temp = new TempDirectory();
        var personalPath = temp.Combine("personal", "ibases.v8i");
        var commonDir = temp.Combine("common");
        Directory.CreateDirectory(Path.GetDirectoryName(personalPath)!);
        Directory.CreateDirectory(commonDir);

        await WriteV8iAsync(personalPath,
            "[Личная]",
            "Connect=File=\"C:\\Bases\\Personal\";",
            "ID=aaaaaaaa-0000-0000-0000-000000000001",
            "Folder=/",
            "[Дубль (личный)]",
            "Connect=Srvr=\"srv\";Ref=\"dup\";",
            "ID=dddddddd-0000-0000-0000-000000000001",
            "Folder=/");

        await WriteV8iAsync(Path.Combine(commonDir, "common.v8i"),
            "[Общие]",
            "ID=ffffffff-0000-0000-0000-000000000001",
            "Folder=/",
            "[Дубль (общий)]",
            "Connect=Srvr=\"srv\";Ref=\"dup\";",
            "ID=DDDDDDDD-0000-0000-0000-000000000001",
            "Folder=/",
            "[Общая база]",
            "Connect=Srvr=\"srv\";Ref=\"common\";",
            "ID=cccccccc-0000-0000-0000-000000000001",
            "Folder=/Общие");

        // Путь к общему списку задан через переменную окружения, как это бывает в реальных cfg.
        var variable = "YAOCL_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, commonDir);
        try
        {
            var machineCfg = temp.Combine("machine.cfg");
            var userCfg = temp.Combine("user.cfg");
            await File.WriteAllBytesAsync(machineCfg, TextFileCodec.Encode(
                $"CommonInfoBases=%{variable}%{Path.DirectorySeparatorChar}common.v8i\r\n", Utf16Cfg));
            await File.WriteAllBytesAsync(userCfg, TextFileCodec.Encode(
                $"CommonInfoBases={temp.Combine("missing.v8i")}\r\nInternetService=http://server/ibases/\r\n",
                Utf16Cfg));

            var catalog = await new InfoBaseCatalogLoader()
                .LoadAsync(new CatalogSources(personalPath, [machineCfg, userCfg]));

            Assert.Equal(3, catalog.Lists.Count);
            Assert.Equal(
                new[] { "Личная", "Дубль (личный)", "Общая база" },
                catalog.InfoBases.Select(b => b.Name).ToArray());

            var common = catalog.InfoBases.Single(b => b.Name == "Общая база");
            Assert.True(common.IsReadOnly);
            Assert.Equal(ListSourceKind.Common, common.Source.Kind);
            Assert.False(catalog.InfoBases[0].IsReadOnly);

            Assert.Single(catalog.Folders);
            Assert.Equal("/Общие", catalog.Folders[0].FullPath);

            Assert.Contains(catalog.Warnings, w => w.Level == CatalogWarningLevel.Warning && w.Message.Contains("недоступен"));
            Assert.Contains(catalog.Warnings, w => w.Level == CatalogWarningLevel.Info && w.Message.Contains("Дубликат"));
            Assert.Contains(catalog.Warnings, w => w.Location == "http://server/ibases/");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task Missing_personal_list_is_empty_and_writable()
    {
        using var temp = new TempDirectory();

        var catalog = await new InfoBaseCatalogLoader()
            .LoadAsync(new CatalogSources(temp.Combine("nope", "ibases.v8i"), [temp.Combine("nope.cfg")]));

        Assert.Empty(catalog.InfoBases);
        Assert.Empty(catalog.Warnings);
        Assert.NotNull(catalog.PersonalList);
        Assert.True(catalog.PersonalList!.IsAvailable);
    }

    [Fact]
    public async Task Base_without_id_is_deduplicated_by_connection()
    {
        using var temp = new TempDirectory();
        var personalPath = temp.Combine("ibases.v8i");
        await WriteV8iAsync(personalPath,
            "[Первая]",
            "Connect=File=\"C:\\Bases\\Same\";",
            "[Вторая]",
            "Connect=File=\"c:/bases/same/\";");

        var catalog = await new InfoBaseCatalogLoader().LoadAsync(new CatalogSources(personalPath, []));

        Assert.Single(catalog.InfoBases);
        Assert.Equal("Первая", catalog.InfoBases[0].Name);
    }

    [Fact]
    public async Task Loads_real_sample_and_builds_tree()
    {
        var catalog = await new InfoBaseCatalogLoader()
            .LoadAsync(new CatalogSources(Fixtures.PathOf("ibases_sample.v8i"), []));

        Assert.Equal(5, catalog.InfoBases.Count);
        Assert.Single(catalog.Folders);

        var root = catalog.BuildTree();
        Assert.Equal(5, root.DescendantInfoBases.Count());

        // Корень по OrderInTree: "Тест" (8192), папка "Бухгалтерия" (16384), "Тест" (65536), без порядка — в конце.
        Assert.Equal(
            new[] { "Тест", "Бухгалтерия", "Тест", "База с \"кавычками\"" },
            root.Items.Select(i => i.Name).ToArray());

        var accounting = root.SubFolders.Single();
        Assert.NotNull(accounting.Folder);
        var archive = accounting.SubFolders.Single();
        Assert.Equal("/Бухгалтерия/Архив", archive.Path);
        Assert.Null(archive.Folder); // папка выведена из пути, отдельной записи нет
        Assert.Equal("Бухгалтерия (копия)", archive.InfoBases.Single().Name);
    }

    private static Task WriteV8iAsync(string path, params string[] lines) =>
        File.WriteAllBytesAsync(path, TextFileCodec.Encode(
            string.Join("\r\n", lines) + "\r\n",
            TextFormat.V8iDefault));
}
