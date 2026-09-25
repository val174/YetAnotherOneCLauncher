using YetAnotherOneCLauncher.Platform.Windows;

namespace YetAnotherOneCLauncher.App.Tests;

/// <summary>Настоящее хранилище ОС: запись создаётся и сразу удаляется.</summary>
public class CredentialStoreTests
{
    [Fact]
    public void Windows_credential_manager_round_trip()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Только Windows.");
            return;
        }

        var store = new WindowsCredentialStore();
        var key = "test-" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.Null(store.Read(key));

            store.Write(key, "YAOCL test", "Пользователь", "пароль с пробелом и \"кавычкой\"");
            Assert.Equal("пароль с пробелом и \"кавычкой\"", store.Read(key));

            store.Write(key, "YAOCL test", "Пользователь", string.Empty);
            Assert.Equal(string.Empty, store.Read(key));
        }
        finally
        {
            store.Delete(key);
        }

        Assert.Null(store.Read(key));
        store.Delete(key); // повторное удаление — не ошибка
    }
}
