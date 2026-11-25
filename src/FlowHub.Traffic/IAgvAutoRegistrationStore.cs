namespace FlowHub.Traffic;

/// <summary>
/// 未登録の AGV が初めて GET してきたときに、その AGV を、種類を決めないまま登録する保存場所。
/// </summary>
public interface IAgvAutoRegistrationStore
{
    /// <summary>種類を決めないまま、切り離しの AGV として登録する（もう登録してあれば、何もしない）。</summary>
    Task AddUnconfiguredAgvAsync(AgvId id, CancellationToken cancellationToken);
}

/// <summary>何も保存しない（DB を使わないとき。テスト用）。</summary>
public sealed class NoAgvAutoRegistrationStore : IAgvAutoRegistrationStore
{
    public static readonly NoAgvAutoRegistrationStore Instance = new();

    private NoAgvAutoRegistrationStore()
    {
    }

    public Task AddUnconfiguredAgvAsync(AgvId id, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>AGV を、動かす AGV から外した結果。</summary>
public enum AgvUnregisterResult
{
    Unregistered,

    /// <summary>切り離しでない（外していない）。</summary>
    NotDetached,

    /// <summary>動かす AGV に入っていない。</summary>
    NotFound,
}

/// <summary>AGV からの GET を受けたときの、その AGV の扱い。</summary>
internal enum AgvContact
{
    /// <summary>動かす AGV（種類が決まっている）。</summary>
    Registered,

    /// <summary>種類が決まっていない（切り離しの AGV と同じに答える）。</summary>
    Unconfigured,
}
