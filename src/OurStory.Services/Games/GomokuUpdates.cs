namespace OurStory.Services.Games;

/// <summary>
/// 唤醒正在等待棋局更新的请求。仅发送变更信号，棋谱仍需按当前账号重新鉴权读取。
/// 等待者在读取数据库之前取得信号，避免读取与订阅之间漏掉落子。
/// </summary>
public sealed class GomokuUpdates {
    private TaskCompletionSource signal = NewSignal();
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task NextChange => Volatile.Read(ref signal).Task;
    public void Publish() => Interlocked.Exchange(ref signal, NewSignal()).TrySetResult();
}
