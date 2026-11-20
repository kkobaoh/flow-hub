namespace FlowHub.Dispatching;

/// <summary>
/// 配車の窓口のうち、消耗品の管理や搬送計画など、ほかのコンテキストから使うもの。
/// </summary>
public interface IDispatchingService
{
    /// <summary>搬送を頼む（1往復ぶん）。</summary>
    Task<TransportId> RequestTransportAsync(TransportOrder order, CancellationToken cancellationToken);

    /// <summary>割り当て前の搬送を取りやめる。割り当てたあとなら取りやめずに false を返す（そのまま運ぶ）。</summary>
    Task<bool> CancelTransportAsync(TransportId id, CancellationToken cancellationToken);

    /// <summary>
    /// 搬送を取りやめる。割り当てたあとでも、AGV へのコマンドの単位で取りやめ、<see cref="TransportCanceled"/> で知らせる。
    /// </summary>
    Task<TransportCancelResult> CancelAsync(TransportId id, CancellationToken cancellationToken);

    /// <summary>システムの運転状態がスタートか（停止中は、自動で判断して頼みごとをしない）。</summary>
    bool IsRunning { get; }

    /// <summary>受け渡しの相手が、受け渡しを終えた。</summary>
    Task ReportTransferCompletedAsync(TransportId id, CancellationToken cancellationToken);
}
