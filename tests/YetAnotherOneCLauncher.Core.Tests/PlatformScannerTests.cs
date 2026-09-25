using YetAnotherOneCLauncher.Core.Platforms;
using static YetAnotherOneCLauncher.Core.Tests.PlatformTestData;

namespace YetAnotherOneCLauncher.Core.Tests;

public class ExecutableHeaderTests
{
    [Theory]
    [InlineData((ushort)0x014C, PlatformArchitecture.X86)]
    [InlineData((ushort)0x8664, PlatformArchitecture.X64)]
    [InlineData((ushort)0xAA64, PlatformArchitecture.Arm64)]
    [InlineData((ushort)0x01C4, PlatformArchitecture.Unknown)]
    public void Reads_pe_machine(ushort machine, PlatformArchitecture expected)
    {
        Assert.Equal(expected, ExecutableHeader.ReadArchitecture(PeHeader(machine)));
    }

    [Theory]
    [InlineData((ushort)0x03, PlatformArchitecture.X86)]
    [InlineData((ushort)0x3E, PlatformArchitecture.X64)]
    [InlineData((ushort)0xB7, PlatformArchitecture.Arm64)]
    [InlineData((ushort)0xAF, PlatformArchitecture.E2k)]
    public void Reads_elf_machine(ushort machine, PlatformArchitecture expected)
    {
        Assert.Equal(expected, ExecutableHeader.ReadArchitecture(ElfHeader(machine)));
        Assert.Equal(expected, ExecutableHeader.ReadArchitecture(ElfHeader(machine, bigEndian: true)));
    }

    [Fact]
    public void Garbage_and_truncated_headers_are_unknown()
    {
        Assert.Equal(PlatformArchitecture.Unknown, ExecutableHeader.ReadArchitecture("#!/bin/sh\n"u8));
        Assert.Equal(PlatformArchitecture.Unknown, ExecutableHeader.ReadArchitecture("MZ"u8));

        var brokenPe = PeHeader(0x8664);
        brokenPe[0x3C] = 0xF0; // e_lfanew за пределами прочитанного
        Assert.Equal(PlatformArchitecture.Unknown, ExecutableHeader.ReadArchitecture(brokenPe));
    }

    [Fact]
    public void Missing_file_is_unknown()
    {
        Assert.Equal(PlatformArchitecture.Unknown, ExecutableHeader.ReadArchitecture(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }
}

public class PlatformScannerTests
{
    [Fact]
    public void Finds_windows_layout_in_several_roots_newest_first()
    {
        using var temp = new TempDirectory();
        var x64Root = temp.Combine("Program Files", "1cv8");
        var x86Root = temp.Combine("Program Files (x86)", "1cv8");
        CreatePlatform(Path.Combine(x64Root, "8.3.24.1667", "bin"), PeHeader(0x8664), "1cv8.exe", "1cv8c.exe");
        CreatePlatform(Path.Combine(x64Root, "8.5.1.189", "bin"), PeHeader(0x8664), "1cv8.exe", "1cv8c.exe");
        CreatePlatform(Path.Combine(x64Root, "8.5.1.1150", "bin"), PeHeader(0x8664), "1cv8c.exe");
        CreatePlatform(Path.Combine(x86Root, "8.3.22.2239", "bin"), PeHeader(0x014C), "1cv8.exe", "1cv8c.exe");
        Directory.CreateDirectory(Path.Combine(x64Root, "common"));
        Directory.CreateDirectory(Path.Combine(x64Root, "srvinfo", "reg_1541"));
        Directory.CreateDirectory(Path.Combine(x64Root, "8.3.20.1000", "bin")); // каталог без исполняемых файлов

        var result = PlatformScanner.Scan([x64Root, x86Root], PlatformExecutableNames.Windows);

        Assert.Empty(result.Warnings);
        Assert.Equal(
            new[] { "8.5.1.1150 x64", "8.5.1.189 x64", "8.3.24.1667 x64", "8.3.22.2239 x86" },
            result.Installations.Select(i => i.ToString()).ToArray());

        var thinOnly = result.Installations[0];
        Assert.Null(thinOnly.ThickClientPath);
        Assert.Equal(Path.Combine(x64Root, "8.5.1.1150", "bin", "1cv8c.exe"), thinOnly.ThinClientPath);
    }

    [Fact]
    public void Finds_linux_layout_with_architecture_level()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("opt", "1cv8");
        CreatePlatform(Path.Combine(root, "x86_64", "8.3.25.1374"), ElfHeader(0x3E), "1cv8", "1cv8c");
        CreatePlatform(Path.Combine(root, "i386", "8.3.25.1374"), ElfHeader(0x03), "1cv8");

        var result = PlatformScanner.Scan([root], PlatformExecutableNames.Linux);

        Assert.Equal(2, result.Installations.Count);
        Assert.Contains(result.Installations, i => i.Architecture == PlatformArchitecture.X64 && i.ThinClientPath is not null);
        Assert.Contains(result.Installations, i => i.Architecture == PlatformArchitecture.X86 && i.ThinClientPath is null);
        Assert.All(result.Installations, i => Assert.Equal(Path.GetDirectoryName(i.ThickClientPath), i.BinDirectory));
    }

    [Fact]
    public void Root_may_be_a_version_directory_and_duplicates_are_ignored()
    {
        using var temp = new TempDirectory();
        var versionDir = temp.Combine("1cv8", "8.3.27.2130");
        CreatePlatform(Path.Combine(versionDir, "bin"), PeHeader(0x8664), "1cv8.exe");

        var result = PlatformScanner.Scan(
            [versionDir, temp.Combine("1cv8"), temp.Combine("1cv8") + Path.DirectorySeparatorChar, temp.Combine("missing"), "  "],
            PlatformExecutableNames.Windows);

        Assert.Equal("8.3.27.2130", Assert.Single(result.Installations).Version.ToString());
    }

    [Fact]
    public void Does_not_search_deeper_than_two_levels()
    {
        using var temp = new TempDirectory();
        CreatePlatform(temp.Combine("a", "b", "c", "8.3.24.1667", "bin"), PeHeader(0x8664), "1cv8.exe");

        Assert.Empty(PlatformScanner.Scan([temp.Path], PlatformExecutableNames.Windows).Installations);
    }

    private static void CreatePlatform(string binDirectory, byte[] header, params string[] executables)
    {
        Directory.CreateDirectory(binDirectory);
        foreach (var executable in executables)
        {
            File.WriteAllBytes(Path.Combine(binDirectory, executable), header);
        }
    }
}
