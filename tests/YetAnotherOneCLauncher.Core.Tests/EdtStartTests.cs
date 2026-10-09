using System.Text;
using YetAnotherOneCLauncher.Core.Edt;

namespace YetAnotherOneCLauncher.Core.Tests;

/// <summary>Проекты 1C:EDT: данные EDT Start, подбор Java, строка запуска, открытая рабочая область, привязки баз.</summary>
public sealed class EdtStartTests : IDisposable
{
    private const string Products = """
        {
          "version" : "1.1",
          "data" : [ {
            "id" : "p-2024",
            "label" : "1C:EDT",
            "location" : "C:\\EDT\\2024.2\\1cedt\\1cedt.exe",
            "installedVersion" : { "id" : "p-2024", "label" : "2024.2", "name" : "2024.2.0" }
          }, {
            "id" : "p-2025",
            "label" : "1C:EDT",
            "location" : "C:\\EDT\\2025.2\\1cedt\\1cedt.exe",
            "installedVersion" : { "id" : "p-2025", "label" : "2025.2", "name" : "2025.2.6" }
          }, {
            "label" : "без id — пропускается",
            "location" : "C:\\x.exe"
          } ]
        }
        """;

    private const string Projects = """
        {
          "version" : "1.1",
          "data" : [ {
            "id" : "pr-1",
            "label" : "Торговля",
            "location" : "D:\\edt\\trade",
            "productId" : "p-2025"
          }, {
            "id" : "pr-2",
            "label" : "",
            "location" : "D:\\edt\\old-hr",
            "productId" : "removed-version"
          }, {
            "id" : "pr-3",
            "label" : "Без каталога"
          } ]
        }
        """;

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static EdtCatalog Catalog() => EdtStartReader.Parse(Encoding.UTF8.GetBytes(Projects), Encoding.UTF8.GetBytes(Products));

    [Fact]
    public void Reads_projects_and_installed_versions()
    {
        var catalog = Catalog();

        Assert.Equal(["2024.2", "2025.2"], catalog.Installations.Select(i => i.Version));
        Assert.Equal(@"C:\EDT\2025.2\1cedt\1cedt.exe", catalog.Installations[1].ExecutablePath);
        Assert.Equal(2, catalog.Projects.Count); // без каталога — пропущен
        var trade = catalog.Projects[0];
        Assert.Equal(("Торговля", @"D:\edt\trade"), (trade.Name, trade.Workspace));
        Assert.Equal("2025.2", catalog.InstallationOf(trade)!.Version);

        // Без названия — по каталогу; версии проекта больше нет — null, по умолчанию — самая новая.
        var old = catalog.Projects[1];
        Assert.Equal(OperatingSystem.IsWindows() ? "old-hr" : @"D:\edt\old-hr", old.Name);
        Assert.Null(catalog.InstallationOf(old));
        Assert.Equal("2025.2", catalog.Newest!.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("не json")]
    [InlineData("{\"version\":\"2.0\",\"items\":[]}")]
    [InlineData("[1,2]")]
    public void Unknown_or_broken_format_is_empty(string projects)
    {
        var catalog = EdtStartReader.Parse(Encoding.UTF8.GetBytes(projects), Encoding.UTF8.GetBytes(Products));
        Assert.True(catalog.IsEmpty);
    }

    [Fact]
    public void Missing_data_directory_is_empty() =>
        Assert.Same(EdtCatalog.Empty, EdtStartReader.Load(_temp.Combine("нет такого")));

    [Fact]
    public void Loads_from_data_directory()
    {
        File.WriteAllText(_temp.Combine(EdtStartReader.ProjectsFile), Projects);
        File.WriteAllText(_temp.Combine(EdtStartReader.ProductsFile), Products);
        Assert.Equal(2, EdtStartReader.Load(_temp.Path).Projects.Count);
    }

    [Fact]
    public void Java_is_found_by_required_version_preferring_axiom_and_newest()
    {
        var edt = Directory.CreateDirectory(_temp.Combine("EDT", "1cedt")).FullName;
        File.WriteAllText(Path.Combine(edt, "1cedt.ini"), "-vmargs\n-Dosgi.requiredJavaVersion=17\n-Xmx8g\n");
        Assert.Equal(17, EdtJava.RequiredVersion(Path.Combine(edt, "1cedt.exe")));
        Assert.Null(EdtJava.RequiredVersion(_temp.Combine("нет", "1cedt.exe")));

        var components = _temp.Combine("components");
        string Jdk(string name)
        {
            var bin = Directory.CreateDirectory(Path.Combine(components, name, "bin")).FullName;
            var java = Path.Combine(bin, OperatingSystem.IsWindows() ? "javaw.exe" : "java");
            File.WriteAllText(java, string.Empty);
            return java;
        }

        Jdk("azul-jdk-full-17.0.13+11-x86_64");
        Jdk("axiom-jdk-full-17.0.10+7-x86_64");
        var newest17 = Jdk("axiom-jdk-full-17.0.16+12-x86_64");
        var java25 = Jdk("axiom-jdk-full-25.0.2+12-x86_64");
        Directory.CreateDirectory(Path.Combine(components, "1c-edt-start-0.10.0+448-x86_64"));

        Assert.Equal(newest17, EdtJava.Find([components, _temp.Combine("нет")], 17));
        Assert.Equal(java25, EdtJava.Find([components], 25));
        Assert.Null(EdtJava.Find([components], 21)); // такой Java нет — EDT ищет сам
        Assert.Null(EdtJava.Find([components], null));
    }

    [Fact]
    public void Command_line_has_workspace_and_java()
    {
        Assert.Equal(
            "\"C:\\EDT\\1cedt.exe\" -data \"D:\\edt\\trade\" -vm \"C:\\jdk\\bin\\javaw.exe\"",
            EdtLaunch.CommandLine(@"C:\EDT\1cedt.exe", @"D:\edt\trade", @"C:\jdk\bin\javaw.exe"));
        Assert.Equal("-data \"D:\\edt\\trade\"", EdtLaunch.Arguments(@"D:\edt\trade", null));
    }

    [Fact]
    public void Open_workspace_is_detected_by_held_lock_file()
    {
        var workspace = _temp.Path;
        Assert.False(EdtLaunch.IsWorkspaceOpen(workspace)); // ещё не открывали

        var metadata = Directory.CreateDirectory(Path.Combine(workspace, ".metadata")).FullName;
        var lockFile = Path.Combine(metadata, ".lock");
        File.WriteAllText(lockFile, string.Empty);
        Assert.False(EdtLaunch.IsWorkspaceOpen(workspace)); // закрыта — файл остался, но свободен

        using (new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.True(EdtLaunch.IsWorkspaceOpen(workspace)); // EDT держит файл
        }
    }

    [Fact]
    public void Infobase_bindings_are_read_from_workspace()
    {
        string Association(string project, string branch, string content)
        {
            var directory = Directory.CreateDirectory(Path.Combine(_temp.Path, ".metadata", ".plugins", "org.eclipse.core.resources", ".projects",
                project, "com._1c.g5.v8.dt.platform.services.core", "refs", "heads", branch)).FullName;
            File.WriteAllText(Path.Combine(directory, "AssociationData.properties"), content);
            return directory;
        }

        Association("trade", "main", "#Infobase association data of the project\nInfobases=AAAA-1\nDefaultInfobase=AAAA-1\n");
        Association("trade-ext", "develop", "Infobases=BBBB-2, cccc-3\n");

        var ids = EdtWorkspaceBindings.InfobaseIds(_temp.Path);

        Assert.Equal(3, ids.Count);
        Assert.Contains("aaaa-1", ids); // регистр не важен
        Assert.Contains("CCCC-3", ids);
        Assert.Empty(EdtWorkspaceBindings.InfobaseIds(_temp.Combine("нет")));
    }
}
