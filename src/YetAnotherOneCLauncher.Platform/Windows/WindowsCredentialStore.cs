using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.Platform.Windows;

/// <summary>
/// Пароли в диспетчере учётных данных Windows (Credential Manager, «Учётные данные Windows» → «Общие»).
/// Запись видна только текущему пользователю; Windows шифрует её сама.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsCredentialStore : ICredentialStore
{
    /// <summary>Префикс имени записи — чтобы записи лаунчера было видно в диспетчере.</summary>
    public const string TargetPrefix = PlatformServices.AppFolderName + ":";

    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string? UnavailableReason => null;

    public unsafe string? Read(string key)
    {
        if (!CredRead(Target(key), CredTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound ? null : throw Failure("прочитать", error);
        }

        try
        {
            var credential = (NativeCredential*)pointer;
            return credential->CredentialBlobSize == 0
                ? string.Empty
                : new string((char*)credential->CredentialBlob, 0, (int)credential->CredentialBlobSize / sizeof(char));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public unsafe void Write(string key, string label, string userName, string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var target = Marshal.StringToHGlobalUni(Target(key));
        var comment = Marshal.StringToHGlobalUni(label);
        var user = Marshal.StringToHGlobalUni(userName);
        try
        {
            fixed (char* blob = password)
            {
                var credential = new NativeCredential
                {
                    Type = CredTypeGeneric,
                    TargetName = target,
                    Comment = comment,
                    CredentialBlobSize = (uint)(password.Length * sizeof(char)),
                    CredentialBlob = (nint)blob,
                    Persist = CredPersistLocalMachine,
                    UserName = user,
                };
                if (!CredWrite(credential, 0))
                {
                    throw Failure("сохранить", Marshal.GetLastPInvokeError());
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(target);
            Marshal.FreeHGlobal(comment);
            Marshal.FreeHGlobal(user);
        }
    }

    public void Delete(string key)
    {
        if (!CredDelete(Target(key), CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != ErrorNotFound)
            {
                throw Failure("удалить", error);
            }
        }
    }

    private static string Target(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return TargetPrefix + key;
    }

    private static CredentialStoreException Failure(string action, int error) =>
        new($"Не удалось {action} пароль в диспетчере учётных данных Windows: {new Win32Exception(error).Message}");

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, uint type, uint flags, out nint credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(in NativeCredential credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target, uint type, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(nint buffer);

    /// <summary>Структура CREDENTIALW.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public nint TargetName;
        public nint Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public nint TargetAlias;
        public nint UserName;
    }
}
