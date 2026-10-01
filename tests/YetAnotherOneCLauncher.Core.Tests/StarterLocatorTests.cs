using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.Core.Tests;

/// <summary>Где искать стандартный стартер 1С (1cestart).</summary>
public class StarterLocatorTests
{
    [Fact]
    public void Starter_is_looked_for_in_common_of_install_root()
    {
        var root = Path.Combine("x", "1cv8");
        var expected = Path.Combine(root, "common", "1cestart.exe");

        // Каталог установки, каталог версии и его bin, сам common — один и тот же стартер, без повторов.
        Assert.Equal(
            [expected],
            StarterLocator.Candidates(
                [root, Path.Combine(root, "8.3.27.1936"), Path.Combine(root, "8.3.27.1936", "bin"), Path.Combine(root, "common"), "  "],
                PlatformExecutableNames.Windows));
        Assert.Equal(
            Path.Combine("opt", "1cv8", "common", "1cestart"),
            Assert.Single(StarterLocator.Candidates([Path.Combine("opt", "1cv8")], PlatformExecutableNames.Linux)));
    }

    [Fact]
    public void Only_existing_starters_are_found()
    {
        var first = Path.Combine("a", "1cv8");
        var second = Path.Combine("b", "1cv8");
        var secondStarter = Path.Combine(second, "common", "1cestart.exe");

        Assert.Equal([secondStarter], StarterLocator.FindAll([first, second], PlatformExecutableNames.Windows, p => p == secondStarter));
        Assert.Equal(2, StarterLocator.FindAll([first, second], PlatformExecutableNames.Windows, _ => true).Count);
        Assert.Empty(StarterLocator.FindAll([first], PlatformExecutableNames.Windows, _ => false));
    }
}
