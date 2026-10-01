using YetAnotherOneCLauncher.Core.Launching;
using static YetAnotherOneCLauncher.Core.Tests.PlatformTestData;

namespace YetAnotherOneCLauncher.Core.Tests;

/// <summary>С какой базой работает запущенный процесс 1С — по его командной строке.</summary>
public class PlatformCommandLineTests
{
    private static readonly Model.InfoBase Server = InfoBase("Connect=Srvr=\"srv-1c:1641\";Ref=\"buh_prod\";");
    private static readonly Model.InfoBase FileBase = InfoBase("Connect=File=\"C:\\Bases\\Buh Copy\";");
    private static readonly Model.InfoBase Web = InfoBase("Connect=ws=\"https://web.example/retail\";");

    [Theory]
    [InlineData("\"C:\\1cv8\\bin\\1cv8c.exe\" ENTERPRISE /S \"srv-1c:1641\\buh_prod\" /N \"Иванов\"")] // как запускает лаунчер
    [InlineData("1cv8.exe DESIGNER /S\"SRV-1C:1641\\Buh_Prod\"")] // слитно, другой регистр
    [InlineData("1cv8c.exe ENTERPRISE /S srv-1c:1641\\buh_prod")] // без кавычек
    [InlineData("1cv8c.exe ENTERPRISE /IBConnectionString \"Srvr=\"\"srv-1c:1641\"\";Ref=\"\"buh_prod\"\";\"")]
    public void Server_base_is_recognized(string commandLine) => Assert.True(PlatformCommandLine.Targets(commandLine, Server));

    [Theory]
    [InlineData("1cv8c.exe ENTERPRISE /S \"srv-1c:1641\\buh_test\"")] // другая база на том же сервере
    [InlineData("1cv8c.exe ENTERPRISE /S \"srv-2:1641\\buh_prod\"")] // та же база на другом сервере
    [InlineData("1cv8c.exe ENTERPRISE /SLev0 /F \"C:\\Bases\\Buh Copy\"")] // «/SLev0» — не «/S»
    [InlineData("1cv8c.exe")]
    public void Other_or_no_base_is_not_taken(string commandLine) => Assert.False(PlatformCommandLine.Targets(commandLine, Server));

    [Theory]
    [InlineData("1cv8.exe ENTERPRISE /F \"C:\\Bases\\Buh Copy\"")]
    [InlineData("1cv8.exe ENTERPRISE /F\"c:\\bases\\buh copy\\\"")] // регистр и «\» в конце пути (Windows)
    public void File_base_is_recognized(string commandLine)
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // регистр пути различается только в Windows
        }

        Assert.True(PlatformCommandLine.Targets(commandLine, FileBase));
        Assert.False(PlatformCommandLine.Targets(commandLine, Server));
    }

    [Fact]
    public void Web_base_and_base_name_are_recognized()
    {
        Assert.True(PlatformCommandLine.Targets("1cv8c.exe ENTERPRISE /WS \"https://web.example/retail/\"", Web));
        Assert.True(PlatformCommandLine.Targets("1cv8c.exe ENTERPRISE /IBName\"Тестовая база\"", Server)); // имя базы из списка
        Assert.False(PlatformCommandLine.Targets("1cv8c.exe ENTERPRISE /IBName \"Тестовая\"", Server)); // часть имени — не совпадение
    }
}
