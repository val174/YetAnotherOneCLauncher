using YetAnotherOneCLauncher.Core.Editing;
using YetAnotherOneCLauncher.Core.Launching;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Platforms;
using static YetAnotherOneCLauncher.Core.Tests.PlatformTestData;

namespace YetAnotherOneCLauncher.Core.Tests;

/// <summary>Создание базы: строка соединения и команда <c>CREATEINFOBASE</c>, проверка параметров, шаблоны.</summary>
public class InfoBaseCreationTests
{
    private static readonly InfoBaseCreation ServerBase = new()
    {
        Kind = ConnectionKind.Server,
        Server = "srv-1c:1541",
        InfobaseName = "buh_new",
        SecureConnection = SecureConnectionLevel.ConnectionOnly,
        Dbms = DbmsType.MSSQLServer,
        DatabaseServer = "sql01",
        DatabaseName = "buh_new_db",
        DatabaseUser = "sa",
        DatabasePassword = "p\"wd",
        DateOffset = 2000,
        CreateDatabase = true,
        BlockScheduledJobs = true,
        DisableLocalSpeechToText = true,
        Locale = "kk_KZ",
    };

    [Fact]
    public void Server_base_connection_has_all_parameters_from_the_starter_dialog()
    {
        Assert.Equal(
            "Srvr=\"srv-1c:1541\";Ref=\"buh_new\";DBMS=\"MSSQLServer\";DBSrvr=\"sql01\";DB=\"buh_new_db\";DBUID=\"sa\";DBPwd=\"p\"\"wd\";"
            + "SQLYOffs=\"2000\";CrSQLDB=\"Y\";SchJobDn=\"Y\";disstt=\"Y\";Locale=\"kk_KZ\";",
            ServerBase.BuildCreateConnection().ToString());

        // Смещение дат — только для MS SQL Server; пустой пользователь и пароль не пишутся.
        var postgres = ServerBase with { Dbms = DbmsType.PostgreSQL, DatabaseUser = " ", DatabasePassword = string.Empty, DisableLocalSpeechToText = false, CreateDatabase = false };
        Assert.Equal(
            "Srvr=\"srv-1c:1541\";Ref=\"buh_new\";DBMS=\"PostgreSQL\";DBSrvr=\"sql01\";DB=\"buh_new_db\";CrSQLDB=\"N\";SchJobDn=\"Y\";Locale=\"kk_KZ\";",
            postgres.BuildCreateConnection().ToString());

        // В список баз — только расположение.
        Assert.Equal("Srvr=\"srv-1c:1541\";Ref=\"buh_new\";", ServerBase.BuildListConnection().ToString());
    }

    [Fact]
    public void Command_creates_base_with_thick_client_and_hides_password_for_log()
    {
        var platform = Installation("8.3.27.2130");
        var command = ServerBase.BuildCommand(platform, @"C:\Temp\out.txt");

        Assert.Equal(platform.ThickClientPath, command.ExecutablePath);
        Assert.Equal("CREATEINFOBASE", command.Arguments[0]);
        Assert.Equal(ServerBase.BuildCreateConnection().ToString(), command.Arguments[1]);
        Assert.Equal(["/SLev1", "/DisableStartupDialogs", "/Out", @"C:\Temp\out.txt"], command.Arguments.Skip(2));
        // Строка соединения — одним аргументом в кавычках, кавычки внутри удвоены (так её принимает 1cv8).
        Assert.StartsWith("CREATEINFOBASE \"Srvr=\"\"srv-1c:1541\"\";", command.ToWindowsArguments(), StringComparison.Ordinal);

        var display = ServerBase.BuildCommand(platform, "out.txt", maskPasswords: true).ToDisplayString();
        Assert.Contains("DBPwd=\"\"***\"\"", display, StringComparison.Ordinal);
        Assert.DoesNotContain("p\"\"\"\"wd", display, StringComparison.Ordinal);

        // Файловая база из шаблона: Locale в строке соединения, /UseTemplate, без /SLev.
        var file = new InfoBaseCreation { FilePath = @"C:\Bases\New Buh", TemplatePath = @"C:\tmplts\1cv8.cf", SecureConnection = SecureConnectionLevel.Always };
        var fileCommand = file.BuildCommand(platform, "out.txt");
        Assert.Equal(
            ["CREATEINFOBASE", "File=\"C:\\Bases\\New Buh\";Locale=\"ru_RU\";", "/UseTemplate", @"C:\tmplts\1cv8.cf", "/DisableStartupDialogs", "/Out", "out.txt"],
            fileCommand.Arguments);

        Assert.Throws<InvalidOperationException>(() => file.BuildCommand(Installation("8.3.27.2130", thick: false), "out.txt"));
    }

    [Fact]
    public void Validation_names_every_missing_parameter()
    {
        Assert.Empty(ServerBase.Validate());
        Assert.Equal(
            [
                "Укажите кластер серверов 1С:Предприятия.",
                "Укажите имя информационной базы в кластере.",
                "Укажите сервер баз данных.",
                "Укажите имя базы данных.",
            ],
            new InfoBaseCreation { Kind = ConnectionKind.Server }.Validate());

        Assert.Equal(["Укажите каталог новой информационной базы."], new InfoBaseCreation().Validate());
        Assert.Equal(
            ["Каталог не пуст: для новой базы выберите пустой или несуществующий каталог."],
            new InfoBaseCreation { FilePath = "C:\\Bases\\Old" }.Validate(_ => true));
        Assert.Empty(new InfoBaseCreation { FilePath = "C:\\Bases\\New" }.Validate(_ => false));
        Assert.Equal(["Выберите шаблон информационной базы."], new InfoBaseCreation { FilePath = "x", TemplatePath = "" }.Validate(_ => false));
        Assert.Single(new InfoBaseCreation { Kind = ConnectionKind.Web }.Validate());
    }

    [Fact]
    public void Template_manifest_gives_catalog_version_and_file()
    {
        string[] manifest =
        [
            "\uFEFFVendor=Фирма \"1С\"",
            "Name=РМКБазовая",
            "Version=1.1.3.14",
            "AppVersion=8.5",
            "[Config1]",
            "Catalog=1С:Рабочее место кассира/РМК",
            "Destination=1C\\PayDesk",
            "Source=1cv8.cf",
            "[Config2]",
            "Catalog=Демо/Выгрузка",
            "Source=1Cv8.dt",
        ];

        var templates = TemplateScanner.ParseManifest(Path.Combine("t", "1c", "PayDesk", "1_1_3_14"), manifest);

        Assert.Equal(2, templates.Count);
        Assert.Equal(new ConfigurationTemplate(Path.Combine("t", "1c", "PayDesk", "1_1_3_14", "1cv8.cf"), "1С:Рабочее место кассира/РМК", "1.1.3.14"), templates[0]);
        Assert.Equal("версия 1.1.3.14, конфигурация (.cf)", templates[0].Details);
        Assert.True(templates[1].IsDump);
    }

    [Fact]
    public void Templates_are_found_with_and_without_manifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "yaocl-tmplts-" + Guid.NewGuid().ToString("N"));
        try
        {
            var withManifest = Directory.CreateDirectory(Path.Combine(root, "1c", "Demo", "1_0_41_3")).FullName;
            File.WriteAllText(Path.Combine(withManifest, "1cv8.mft"), "Version=1.0.41.3\r\n[Config1]\r\nCatalog=Демо/Управляемое приложение\r\nSource=1cv8.dt\r\n");
            File.WriteAllBytes(Path.Combine(withManifest, "1cv8.dt"), [1]);
            var older = Directory.CreateDirectory(Path.Combine(root, "1c", "Demo", "1_0_40_1")).FullName;
            File.WriteAllText(Path.Combine(older, "1cv8.mft"), "Version=1.0.40.1\r\n[Config1]\r\nCatalog=Демо/Управляемое приложение\r\nSource=1cv8.dt\r\n");
            File.WriteAllBytes(Path.Combine(older, "1cv8.dt"), [1]);
            var noManifest = Directory.CreateDirectory(Path.Combine(root, "my", "Trade")).FullName;
            File.WriteAllBytes(Path.Combine(noManifest, "1Cv8.cf"), [1]);
            Directory.CreateDirectory(Path.Combine(root, "empty"));

            var templates = TemplateScanner.Scan([root, root, Path.Combine(root, "missing")]);

            // По каталогу шаблона, внутри — новые версии первыми; без манифеста — путь каталога.
            Assert.Equal(
                ["Демо/Управляемое приложение (1.0.41.3)", "Демо/Управляемое приложение (1.0.40.1)", "my/Trade"],
                templates.Select(t => t.ToString()));
            Assert.Equal(Path.Combine(noManifest, "1Cv8.cf"), templates[2].Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
