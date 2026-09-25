using System.Buffers.Binary;
using YetAnotherOneCLauncher.Core.Model;
using YetAnotherOneCLauncher.Core.Parsing;
using YetAnotherOneCLauncher.Core.Platforms;

namespace YetAnotherOneCLauncher.Core.Tests;

/// <summary>Заготовки для тестов платформ и запуска.</summary>
internal static class PlatformTestData
{
    public static byte[] PeHeader(ushort machine)
    {
        var bytes = new byte[0x100];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3C), 0x80);
        "PE\0\0"u8.CopyTo(bytes.AsSpan(0x80));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x84), machine);
        return bytes;
    }

    public static byte[] ElfHeader(ushort machine, bool bigEndian = false)
    {
        var bytes = new byte[64];
        bytes[0] = 0x7F;
        "ELF"u8.CopyTo(bytes.AsSpan(1));
        bytes[4] = 2; // 64 бита
        bytes[5] = bigEndian ? (byte)2 : (byte)1;
        if (bigEndian)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(18), machine);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18), machine);
        }

        return bytes;
    }

    public static PlatformInstallation Installation(
        string version,
        PlatformArchitecture architecture = PlatformArchitecture.X64,
        bool thick = true,
        bool thin = true)
    {
        var bin = Path.Combine("Program Files", "1cv8", version, architecture.ToString(), "bin");
        return new PlatformInstallation(
            PlatformVersion.Parse(version),
            architecture,
            bin,
            thick ? Path.Combine(bin, "1cv8.exe") : null,
            thin ? Path.Combine(bin, "1cv8c.exe") : null);
    }

    /// <summary>База из одной секции .v8i: строки после заголовка.</summary>
    public static InfoBase InfoBase(params string[] lines)
    {
        var text = "[Тестовая база]\r\n" + string.Join("\r\n", lines) + "\r\n";
        var section = V8iDocument.Parse(text).Sections.Single();
        return new InfoBase(section, new ListSource(ListSourceKind.Personal, "ibases.v8i"));
    }
}
