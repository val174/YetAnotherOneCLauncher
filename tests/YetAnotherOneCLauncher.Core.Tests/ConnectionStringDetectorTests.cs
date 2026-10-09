using YetAnotherOneCLauncher.Core.Parsing;

namespace YetAnotherOneCLauncher.Core.Tests;

/// <summary>Определение параметров базы по вставленной строке подключения (форма добавления существующей базы).</summary>
public class ConnectionStringDetectorTests
{
    [Theory]
    [InlineData("File=\"C:\\Bases\\Buh\";", @"C:\Bases\Buh")]
    [InlineData("file=C:\\Bases\\Buh", @"C:\Bases\Buh")] // без кавычек, ключ в другом регистре
    [InlineData("Connect=File=\"\\\\srv\\share\\buh\";", @"\\srv\share\buh")] // строка из ibases.v8i
    [InlineData(@"C:\Bases\Buh\", @"C:\Bases\Buh")] // просто путь, завершающий «\» убирается
    [InlineData(@"""C:\Bases\Buh""", @"C:\Bases\Buh")] // путь в кавычках
    [InlineData(@"C:\Bases\Buh\1Cv8.1CD", @"C:\Bases\Buh")] // путь к файлу базы — берётся каталог
    [InlineData(@"\\srv\share\buh", @"\\srv\share\buh")]
    [InlineData("/home/user/bases/buh", "/home/user/bases/buh")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"/F""C:\Bases\Buh""", @"C:\Bases\Buh")] // параметр командной строки
    [InlineData(@"ENTERPRISE /F ""D:\1C Bases\Buh"" /N Иванов", @"D:\1C Bases\Buh")]
    [InlineData(@"/FC:\Bases\Buh", @"C:\Bases\Buh")]
    public void Detects_file_base(string text, string directory)
    {
        var detected = ConnectionStringDetector.Detect(text);

        Assert.Equal(ConnectionKind.File, detected.Kind);
        Assert.Equal(directory, detected.FilePath);
        Assert.True(detected.IsRecognized);
        Assert.False(detected.IsIncomplete);
        Assert.Contains(directory, detected.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Srvr=\"srv1c:1541\";Ref=\"buh_prod\";", "srv1c:1541", "srv1c", 1541, "buh_prod")]
    [InlineData("SRVR=srv1c;REF=buh", "srv1c", "srv1c", null, "buh")]
    [InlineData(@"srv1c\buh", "srv1c", "srv1c", null, "buh")] // как показывает стартер
    [InlineData(@"srv1c:2541\buh", "srv1c:2541", "srv1c", 2541, "buh")]
    [InlineData("tcp://srv1c:1541/buh", "srv1c:1541", "srv1c", 1541, "buh")]
    [InlineData(@"/S""srv1c:1541\buh""", "srv1c:1541", "srv1c", 1541, "buh")]
    [InlineData(@"DESIGNER /S srv1c\buh /N Admin", "srv1c", "srv1c", null, "buh")]
    [InlineData("Srvr=\"[::1]:1541\";Ref=\"buh\";", "[::1]:1541", "::1", 1541, "buh")]
    [InlineData("Srvr=\"192.168.0.10\";Ref=\"zup\";", "192.168.0.10", "192.168.0.10", null, "zup")]
    public void Detects_server_base_with_host_port_and_name(string text, string server, string host, int? port, string name)
    {
        var detected = ConnectionStringDetector.Detect(text);

        Assert.Equal(ConnectionKind.Server, detected.Kind);
        Assert.Equal(server, detected.Server);
        Assert.Equal(host, detected.ServerHost);
        Assert.Equal(port, detected.ServerPort);
        Assert.Equal(name, detected.InfobaseName);
        Assert.Equal(name, detected.SuggestedName);
        Assert.False(detected.IsIncomplete);
    }

    [Fact]
    public void Server_list_keeps_cluster_and_has_no_single_host()
    {
        var detected = ConnectionStringDetector.Detect(@"srv1,srv2:1541\buh");

        Assert.Equal("srv1,srv2:1541", detected.Server);
        Assert.Equal(string.Empty, detected.ServerHost);
        Assert.Null(detected.ServerPort);
        Assert.Contains("кластер srv1,srv2:1541", detected.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Server_without_infobase_name_is_incomplete()
    {
        var detected = ConnectionStringDetector.Detect("Srvr=\"srv1c\";");

        Assert.Equal(ConnectionKind.Server, detected.Kind);
        Assert.True(detected.IsIncomplete);
        Assert.Contains("Не указано имя базы", detected.Description, StringComparison.Ordinal);
        Assert.Contains("порт 1541 (по умолчанию)", detected.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ws=\"http://web/buh\";", "http://web/buh", "buh")]
    [InlineData("https://web.example/retail", "https://web.example/retail", "retail")]
    [InlineData("https://web.example/retail/", "https://web.example/retail", "retail")]
    [InlineData("https://web.example/retail/ru_RU/", "https://web.example/retail", "retail")] // адрес веб-клиента
    [InlineData("https://web.example/retail/ru_RU/#e1cib/list/Документ.Заказ", "https://web.example/retail", "retail")]
    [InlineData("http://web:8080/1c/ut/odata/standard.odata/Catalog_Номенклатура", "http://web:8080/1c/ut", "ut")] // адрес сервиса
    [InlineData("http://web/buh/hs/api/v1/orders?id=5", "http://web/buh", "buh")]
    [InlineData("http://web/ru", "http://web/ru", "ru")] // публикация с двухбуквенным именем — не язык
    [InlineData(@"/WS""http://web/buh""", "http://web/buh", "buh")]
    [InlineData("http://web", "http://web", "web")]
    public void Detects_web_publication_address(string text, string url, string name)
    {
        var detected = ConnectionStringDetector.Detect(text);

        Assert.Equal(ConnectionKind.Web, detected.Kind);
        Assert.Equal(url, detected.WebUrl);
        Assert.Equal(name, detected.SuggestedName);
    }

    [Fact]
    public void Extra_keys_are_kept_but_user_and_password_dropped()
    {
        var detected = ConnectionStringDetector.Detect("ws=\"http://web/buh\";wsn=\"svc\";Usr=\"Иванов\";Pwd=\"secret\";");

        var connection = detected.Connection!;
        Assert.Equal("svc", connection["wsn"]);
        Assert.Null(connection["Usr"]);
        Assert.Null(connection["Pwd"]);
        Assert.Equal("http://web/buh", connection.WebUrl);
    }

    [Fact]
    public void Command_line_connection_string_is_parsed()
    {
        var detected = ConnectionStringDetector.Detect("1cv8.exe ENTERPRISE /IBConnectionString\"Srvr=\"\"srv\"\";Ref=\"\"buh\"\";\"");

        Assert.Equal(ConnectionKind.Server, detected.Kind);
        Assert.Equal("srv", detected.Server);
        Assert.Equal("buh", detected.InfobaseName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_text_is_nothing(string text)
    {
        var detected = ConnectionStringDetector.Detect(text);
        Assert.Equal(ConnectionKind.None, detected.Kind);
        Assert.False(detected.IsRecognized);
    }

    [Theory]
    [InlineData("какая-то база")]
    [InlineData("Usr=\"Иванов\";")]
    [InlineData("Ref=\"buh\";")]
    public void Unrecognized_text_explains_why(string text)
    {
        var detected = ConnectionStringDetector.Detect(text);

        Assert.Equal(ConnectionKind.Unknown, detected.Kind);
        Assert.False(detected.IsRecognized);
        Assert.NotEmpty(detected.Description);
    }

    [Theory]
    [InlineData("srv", "srv", null)]
    [InlineData("srv:1541", "srv", 1541)]
    [InlineData("srv:abc", "srv:abc", null)]
    [InlineData("srv:70000", "srv:70000", null)]
    [InlineData("[fe80::1]:2541", "fe80::1", 2541)]
    [InlineData("fe80::1", "fe80::1", null)]
    [InlineData("srv1,srv2", "", null)]
    public void Splits_cluster_into_host_and_port(string server, string host, int? port) =>
        Assert.Equal((host, port), ConnectionStringDetector.SplitServer(server));
}
