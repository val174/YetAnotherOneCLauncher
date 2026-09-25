namespace YetAnotherOneCLauncher.Platform.Abstractions;

/// <summary>
/// Пароли баз в хранилище ОС: Credential Manager в Windows, libsecret в Linux.
/// Лаунчер хранит у себя только ключ записи; сам пароль — никогда (ни в JSON, ни в v8i, ни в логе).
/// </summary>
public interface ICredentialStore
{
    /// <summary>Почему хранилище недоступно; <c>null</c> — доступно.</summary>
    string? UnavailableReason { get; }

    /// <returns>Пароль или <c>null</c>, если записи нет.</returns>
    /// <exception cref="CredentialStoreException">Хранилище недоступно или вернуло ошибку.</exception>
    string? Read(string key);

    /// <param name="key">Ключ записи.</param>
    /// <param name="label">Подпись записи для диспетчера учётных данных: база и пользователь.</param>
    /// <param name="userName">Пользователь 1С.</param>
    /// <param name="password">Пароль.</param>
    /// <exception cref="CredentialStoreException">Хранилище недоступно или вернуло ошибку.</exception>
    void Write(string key, string label, string userName, string password);

    /// <summary>Удаляет запись; отсутствие записи — не ошибка.</summary>
    /// <exception cref="CredentialStoreException">Хранилище недоступно или вернуло ошибку.</exception>
    void Delete(string key);
}

/// <summary>Ошибка хранилища паролей. Текст можно показать пользователю: пароля в нём нет.</summary>
public sealed class CredentialStoreException : Exception
{
    public CredentialStoreException()
    {
    }

    public CredentialStoreException(string message)
        : base(message)
    {
    }

    public CredentialStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
