using YetAnotherOneCLauncher.Core.Platforms;
using static YetAnotherOneCLauncher.Core.Tests.PlatformTestData;

namespace YetAnotherOneCLauncher.Core.Tests;

public class PlatformSelectorTests
{
    // Как на реальной машине: несколько x64 и старые x86.
    private static readonly PlatformInstallation[] Installed =
    [
        Installation("8.5.1.1150"),
        Installation("8.5.1.189"),
        Installation("8.3.27.2130"),
        Installation("8.3.24.1667"),
        Installation("8.3.22.2239", PlatformArchitecture.X86),
        Installation("8.3.18.1741", PlatformArchitecture.X86, thin: false),
    ];

    [Fact]
    public void Without_mask_takes_newest()
    {
        var selection = PlatformSelector.Select(Installed, PlatformExecutable.ThinClient, null, null);

        Assert.Equal(PlatformSelectionStatus.Selected, selection.Status);
        Assert.Equal("8.5.1.1150", selection.Installation!.Version.ToString());
        Assert.Equal(VersionMaskSource.None, selection.MaskSource);
    }

    [Fact]
    public void Info_base_mask_wins_over_starter_default()
    {
        var selection = PlatformSelector.Select(Installed, PlatformExecutable.ThinClient, "8.3", "8.3.24");

        Assert.Equal("8.3.27.2130", selection.Installation!.Version.ToString());
        Assert.Equal(VersionMaskSource.InfoBase, selection.MaskSource);
    }

    [Fact]
    public void Starter_default_is_used_when_info_base_has_no_version()
    {
        var selection = PlatformSelector.Select(Installed, PlatformExecutable.ThinClient, null, "8.3.24");

        Assert.Equal("8.3.24.1667", selection.Installation!.Version.ToString());
        Assert.Equal(VersionMaskSource.StarterDefault, selection.MaskSource);
    }

    [Fact]
    public void Missing_version_offers_newest_of_the_same_branch_not_newest_overall()
    {
        // 8.3.23 нет; 8.5 новее, но это другая ветка — предлагаем 8.3.27.
        var selection = PlatformSelector.Select(Installed, PlatformExecutable.ThinClient, "8.3.23", null);

        Assert.Equal(PlatformSelectionStatus.MaskNotInstalled, selection.Status);
        Assert.Equal("8.3.27.2130", selection.Installation!.Version.ToString());
        Assert.Equal("8.3.23", selection.Mask!.ToString());
    }

    [Fact]
    public void Missing_build_offers_same_release_first()
    {
        // Случай с реальной машины: у базы Version=8.3.24.1467, установлена только 8.3.24.1667.
        var selection = PlatformSelector.Select(Installed, PlatformExecutable.ThinClient, "8.3.24.1467", null);

        Assert.Equal(PlatformSelectionStatus.MaskNotInstalled, selection.Status);
        Assert.Equal("8.3.24.1667", selection.Installation!.Version.ToString());
    }

    [Fact]
    public void Missing_branch_offers_newest_overall()
    {
        var selection = PlatformSelector.Select(Installed, PlatformExecutable.ThinClient, "8.4", null);

        Assert.Equal(PlatformSelectionStatus.MaskNotInstalled, selection.Status);
        Assert.Equal("8.5.1.1150", selection.Installation!.Version.ToString());
    }

    [Fact]
    public void Only_installations_with_required_executable_are_considered()
    {
        var thin = PlatformSelector.Select(Installed, PlatformExecutable.ThinClient, "8.3.18", null);
        var thick = PlatformSelector.Select(Installed, PlatformExecutable.ThickClient, "8.3.18", null);

        Assert.Equal(PlatformSelectionStatus.MaskNotInstalled, thin.Status);
        Assert.Equal(PlatformSelectionStatus.Selected, thick.Status);
    }

    [Fact]
    public void Prefers_requested_architecture_for_equal_versions_only()
    {
        PlatformInstallation[] installed =
        [
            Installation("8.3.25.1374", PlatformArchitecture.X86),
            Installation("8.3.25.1374", PlatformArchitecture.X64),
            Installation("8.3.24.1667", PlatformArchitecture.X64),
        ];

        Assert.Equal(PlatformArchitecture.X64, PlatformSelector.Select(installed, PlatformExecutable.ThinClient, null, null).Installation!.Architecture);
        Assert.Equal(
            PlatformArchitecture.X86,
            PlatformSelector.Select(installed, PlatformExecutable.ThinClient, null, null, PlatformArchitecture.X86).Installation!.Architecture);

        // Более новая x86 важнее разрядности.
        PlatformInstallation[] newerX86 = [Installation("8.3.25.1374", PlatformArchitecture.X86), Installation("8.3.24.1667")];
        Assert.Equal("8.3.25.1374", PlatformSelector.Select(newerX86, PlatformExecutable.ThinClient, null, null).Installation!.Version.ToString());
    }

    [Fact]
    public void Nothing_installed()
    {
        var selection = PlatformSelector.Select([], PlatformExecutable.ThickClient, "8.3", null);

        Assert.Equal(PlatformSelectionStatus.NothingInstalled, selection.Status);
        Assert.Null(selection.Installation);
    }

    [Fact]
    public void Invalid_mask_is_ignored_with_warning()
    {
        var selection = PlatformSelector.Select(Installed, PlatformExecutable.ThinClient, "8.3.*", "8.3.24");

        Assert.Equal("8.3.24.1667", selection.Installation!.Version.ToString());
        Assert.Equal(VersionMaskSource.StarterDefault, selection.MaskSource);
        Assert.Single(selection.Warnings);
    }
}
