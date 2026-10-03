namespace OurStory.Services.Games.Draw;

/// <summary>单站点短写入串行化；等待更新时不持有锁，数据库版本号继续防止多进程覆盖。</summary>
public sealed class DrawCoordinator : IDisposable {
    public SemaphoreSlim Gate { get; } = new(1, 1);
    private TaskCompletionSource signal = NewSignal();
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task NextChange => Volatile.Read(ref signal).Task;
    public void Publish() => Interlocked.Exchange(ref signal, NewSignal()).TrySetResult();
    public void Dispose() => Gate.Dispose();
}
