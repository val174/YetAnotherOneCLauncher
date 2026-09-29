namespace YetAnotherOneCLauncher.App.Services;

/// <summary>
/// Метка «лаунчер уже работает» для текущего пользователя и сеанса: именованный мьютекс, который держит
/// каждый экземпляр, пока открыт. Нужна для запрета повторного запуска: проверка мгновенная,
/// в отличие от попытки подключиться к каналу, которая ждёт до таймаута.
/// </summary>
public sealed class InstanceLock : IDisposable
{
    private readonly Mutex _mutex;

    public InstanceLock(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        // Local\ — в пределах сеанса Windows; мьютекс только метка, владеть им не нужно.
        _mutex = new Mutex(initiallyOwned: false, @"Local\" + name, out var createdNew);
        IsFirst = createdNew;
    }

    /// <summary>Имя для лаунчера: то же, что у канала запуска баз.</summary>
    public static string DefaultName => LaunchRequestChannel.PipeName;

    /// <summary><c>false</c> — на момент создания уже работал другой экземпляр.</summary>
    public bool IsFirst { get; }

    public void Dispose() => _mutex.Dispose();
}
