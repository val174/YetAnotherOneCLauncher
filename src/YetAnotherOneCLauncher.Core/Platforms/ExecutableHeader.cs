using System.Buffers.Binary;

namespace YetAnotherOneCLauncher.Core.Platforms;

/// <summary>
/// Определяет архитектуру исполняемого файла по заголовку: PE (Windows) или ELF (Linux).
/// Надёжнее, чем угадывать по пути: <c>InstalledLocation</c> может указывать куда угодно.
/// </summary>
public static class ExecutableHeader
{
    private const int HeaderBufferSize = 4096;

    public static PlatformArchitecture ReadArchitecture(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[HeaderBufferSize];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return ReadArchitecture(buffer.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PlatformArchitecture.Unknown;
        }
    }

    public static PlatformArchitecture ReadArchitecture(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 20 && header[0] == 0x7F && header[1] == (byte)'E' && header[2] == (byte)'L' && header[3] == (byte)'F')
        {
            return ReadElf(header);
        }

        if (header.Length >= 0x40 && header[0] == (byte)'M' && header[1] == (byte)'Z')
        {
            return ReadPe(header);
        }

        return PlatformArchitecture.Unknown;
    }

    private static PlatformArchitecture ReadElf(ReadOnlySpan<byte> header)
    {
        // e_ident[EI_DATA] (смещение 5): 1 — little-endian, 2 — big-endian. e_machine — по смещению 18.
        var machineBytes = header.Slice(18, 2);
        var machine = header[5] == 2
            ? BinaryPrimitives.ReadUInt16BigEndian(machineBytes)
            : BinaryPrimitives.ReadUInt16LittleEndian(machineBytes);

        return machine switch
        {
            0x03 => PlatformArchitecture.X86,
            0x3E => PlatformArchitecture.X64,
            0xB7 => PlatformArchitecture.Arm64,
            0xAF => PlatformArchitecture.E2k,
            _ => PlatformArchitecture.Unknown,
        };
    }

    private static PlatformArchitecture ReadPe(ReadOnlySpan<byte> header)
    {
        // e_lfanew (смещение 0x3C) указывает на сигнатуру "PE\0\0", за ней — поле Machine.
        var peOffset = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(0x3C, 4));
        if (peOffset < 0 || peOffset + 6 > header.Length
            || header[peOffset] != (byte)'P' || header[peOffset + 1] != (byte)'E'
            || header[peOffset + 2] != 0 || header[peOffset + 3] != 0)
        {
            return PlatformArchitecture.Unknown;
        }

        var machine = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(peOffset + 4, 2));
        return machine switch
        {
            0x014C => PlatformArchitecture.X86,
            0x8664 => PlatformArchitecture.X64,
            0xAA64 => PlatformArchitecture.Arm64,
            _ => PlatformArchitecture.Unknown,
        };
    }
}
