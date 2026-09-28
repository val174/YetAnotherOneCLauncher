using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using YetAnotherOneCLauncher.Platform.Abstractions;

namespace YetAnotherOneCLauncher.Platform.Windows;

/// <summary>
/// Список переходов Windows (ICustomDestinationList): категория «Последние базы» из ярлыков на лаунчер с
/// аргументом запуска базы. Вызовы COM — напрямую через таблицу методов: так нет зависимости от генераторов
/// обёрток и отражения (сборка одним файлом, NativeAOT).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class WindowsJumpList : IJumpList
{
    /// <summary>Идентификатор приложения в оболочке: у процесса и у списка должен совпадать.</summary>
    public const string AppUserModelId = "YetAnotherOneCLauncher";

    public const string CategoryName = "Последние базы";

    private const uint ClsctxInprocServer = 1;
    private const ushort VtLpwstr = 31;

    private static readonly Guid ClsidDestinationList = new("77f10cf0-3db5-4966-b520-b7c54fd35ed6");
    private static readonly Guid ClsidObjectCollection = new("2d3468c1-36a7-43b6-ac24-d3f02fd9607a");
    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IidCustomDestinationList = new("6332debf-87b5-4670-90c0-5e57b408a49e");
    private static readonly Guid IidObjectArray = new("92ca9dcd-5622-4bba-a805-5e9f541bd8c9");
    private static readonly Guid IidObjectCollection = new("5632b1a4-e38a-400a-928a-d4cd63230295");
    private static readonly Guid IidShellLink = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid IidPropertyStore = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");

    // PKEY_Title: название пункта в списке переходов.
    private static readonly PropertyKey TitleKey = new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);

    private readonly string _executablePath;

    public WindowsJumpList(string? executablePath = null)
    {
        _executablePath = executablePath ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("Не удалось определить путь к лаунчеру.");
    }

    /// <summary>Идентификатор приложения для процесса — вызвать до показа окна.</summary>
    public static void SetProcessAppUserModelId() => SetCurrentProcessExplicitAppUserModelID(AppUserModelId);

    public void SetRecent(IReadOnlyList<JumpListEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        nint list = 0, removed = 0, collection = 0;
        try
        {
            list = Create(ClsidDestinationList, IidCustomDestinationList);
            fixed (char* appId = AppUserModelId)
            {
                Check(Vt<nint, nint, int>(list, 3)(list, (nint)appId), "SetAppID");
            }

            uint minSlots;
            var iidObjectArray = IidObjectArray;
            Check(Vt<nint, nint, nint, nint, int>(list, 4)(list, (nint)(&minSlots), (nint)(&iidObjectArray), (nint)(&removed)), "BeginList");

            // Пункты, которые пользователь убрал из списка, добавлять нельзя — оболочка отвергнет всю категорию.
            var removedArguments = ArgumentsOf(removed);
            collection = Create(ClsidObjectCollection, IidObjectCollection);
            var added = 0;
            foreach (var entry in entries.Where(e => !removedArguments.Contains(e.Arguments)))
            {
                added++;
                var link = CreateLink(entry);
                try
                {
                    Check(Vt<nint, nint, int>(collection, 5)(collection, link), "AddObject");
                }
                finally
                {
                    Marshal.Release(link);
                }
            }

            // Пустую категорию оболочка отвергает; без неё CommitList просто очищает список.
            if (added > 0)
            {
                fixed (char* category = CategoryName)
                {
                    Check(Vt<nint, nint, nint, int>(list, 5)(list, (nint)category, collection), "AppendCategory");
                }
            }

            Check(Vt<nint, int>(list, 8)(list), "CommitList");
        }
        catch
        {
            if (list != 0)
            {
                _ = Vt<nint, int>(list, 11)(list); // AbortList
            }

            throw;
        }
        finally
        {
            ReleaseIfSet(collection);
            ReleaseIfSet(removed);
            ReleaseIfSet(list);
        }
    }

    private nint CreateLink(JumpListEntry entry)
    {
        var link = Create(ClsidShellLink, IidShellLink);
        try
        {
            fixed (char* path = _executablePath)
            fixed (char* arguments = entry.Arguments)
            fixed (char* description = Truncate(entry.Description, 255))
            {
                Check(Vt<nint, nint, int>(link, 20)(link, (nint)path), "SetPath");
                Check(Vt<nint, nint, int>(link, 11)(link, (nint)arguments), "SetArguments");
                Check(Vt<nint, nint, int>(link, 7)(link, (nint)description), "SetDescription");
            }

            if (entry.IconPath is { } icon)
            {
                fixed (char* iconPath = icon)
                {
                    Check(Vt<nint, nint, int, int>(link, 17)(link, (nint)iconPath, 0), "SetIconLocation");
                }
            }

            SetTitle(link, entry.Title);
            return link;
        }
        catch
        {
            Marshal.Release(link);
            throw;
        }
    }

    private static void SetTitle(nint link, string title)
    {
        var iid = IidPropertyStore;
        Check(Marshal.QueryInterface(link, in iid, out var store), "IPropertyStore");
        var text = Marshal.StringToCoTaskMemUni(title);
        try
        {
            var key = TitleKey;
            var value = new PropVariant { Type = VtLpwstr, Pointer = text };
            Check(Vt<nint, nint, nint, int>(store, 6)(store, (nint)(&key), (nint)(&value)), "SetValue");
            Check(Vt<nint, int>(store, 7)(store), "Commit");
        }
        finally
        {
            Marshal.FreeCoTaskMem(text);
            Marshal.Release(store);
        }
    }

    /// <summary>Аргументы ярлыков, убранных пользователем из списка.</summary>
    private static HashSet<string> ArgumentsOf(nint objectArray)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (objectArray == 0)
        {
            return result;
        }

        uint count;
        Check(Vt<nint, nint, int>(objectArray, 3)(objectArray, (nint)(&count)), "GetCount");
        var buffer = stackalloc char[1024];
        for (uint i = 0; i < count; i++)
        {
            var iid = IidShellLink;
            nint link;
            if (Vt<nint, uint, nint, nint, int>(objectArray, 4)(objectArray, i, (nint)(&iid), (nint)(&link)) < 0)
            {
                continue; // не ярлык
            }

            try
            {
                if (Vt<nint, nint, int, int>(link, 10)(link, (nint)buffer, 1024) >= 0)
                {
                    result.Add(new string(buffer));
                }
            }
            finally
            {
                Marshal.Release(link);
            }
        }

        return result;
    }

    private static nint Create(Guid clsid, Guid iid)
    {
        Check(CoCreateInstance(in clsid, 0, ClsctxInprocServer, in iid, out var instance), "CoCreateInstance " + clsid);
        return instance;
    }

    /// <summary>Метод COM по номеру в таблице (0–2 — IUnknown).</summary>
    private static delegate* unmanaged[Stdcall]<T1, TResult> Vt<T1, TResult>(nint instance, int slot) =>
        (delegate* unmanaged[Stdcall]<T1, TResult>)(*(nint**)instance)[slot];

    private static delegate* unmanaged[Stdcall]<T1, T2, TResult> Vt<T1, T2, TResult>(nint instance, int slot) =>
        (delegate* unmanaged[Stdcall]<T1, T2, TResult>)(*(nint**)instance)[slot];

    private static delegate* unmanaged[Stdcall]<T1, T2, T3, TResult> Vt<T1, T2, T3, TResult>(nint instance, int slot) =>
        (delegate* unmanaged[Stdcall]<T1, T2, T3, TResult>)(*(nint**)instance)[slot];

    private static delegate* unmanaged[Stdcall]<T1, T2, T3, T4, TResult> Vt<T1, T2, T3, T4, TResult>(nint instance, int slot) =>
        (delegate* unmanaged[Stdcall]<T1, T2, T3, T4, TResult>)(*(nint**)instance)[slot];

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private static void ReleaseIfSet(nint instance)
    {
        if (instance != 0)
        {
            Marshal.Release(instance);
        }
    }

    private static void Check(int hresult, string operation)
    {
        if (hresult < 0)
        {
            throw new InvalidOperationException(
                $"Список переходов Windows: {operation} — 0x{hresult:X8} ({Marshal.GetExceptionForHR(hresult)?.Message})");
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string appId);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PropertyKey(Guid FormatId, uint PropertyId);

    /// <summary>PROPVARIANT со строкой (VT_LPWSTR).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public nint Pointer;
        public nint Pointer2;
    }
}
