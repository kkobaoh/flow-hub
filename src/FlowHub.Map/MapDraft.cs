namespace FlowHub.Map;

/// <summary>
/// マップの下書き（マップの編集の画面で直している途中のもの）。
/// </summary>
/// <remarks>
/// 下書きは検証しない（ラインを引いている途中は、一時的に成り立たない形になるため）。このため、ID や種類は値の型にせず、
/// 届いたままの文字と数で持つ。値の型にして確かめるのは、公開する（スナップショットにする）ときだけ。
/// </remarks>
/// <param name="PanelSizeMm">パネルの一辺の長さ（mm）。</param>
public sealed record MapDraft(int PanelSizeMm, IReadOnlyList<DraftPanel> Panels, IReadOnlyList<DraftConnection> Connections)
{
    /// <summary>公開中の版の写しから、下書きを作る（下書きがないときは、ここから編集を始める）。</summary>
    public static MapDraft FromSnapshot(MapSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var drawing = snapshot.Drawing ?? throw new ArgumentException("描く情報（パネルの位置）のないマップからは、下書きを作れません。", nameof(snapshot));
        return new MapDraft(
            drawing.PanelSizeMm,
            [.. snapshot.Panels.Select(p =>
            {
                var placement = drawing.Placements[p.Id];
                return new DraftPanel(
                    p.Id.Value,
                    p.Type.ToString(),
                    p.HasChargingPin,
                    p.CarrierLoweringHeading?.Degrees,
                    placement.Position.Column,
                    placement.Position.Row,
                    placement.CrossOffset.EastMm,
                    placement.CrossOffset.SouthMm,
                    placement.Outline is { } o ? new DraftOutline(o.XMm, o.YMm, o.WidthMm, o.HeightMm) : null);
            })],
            [.. snapshot.Connections.Select(c => new DraftConnection(c.From.Value, c.To.Value, c.DistanceMm, c.Heading.Degrees, c.OneWay))]);
    }

    /// <summary>
    /// 公開するときと同じに検証し、違反がなければスナップショットにする。違反はすべてまとめて返す。
    /// 値の型にできない値（ID が空、知らない種類、範囲の外の角度など）があれば、それだけを返す（形が決まらないので、その先は確かめられないため）。
    /// </summary>
    public MapSnapshotResult ToSnapshot(MapValidationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var invalid = new List<MapValidationError>();
        void Invalid(string message) => invalid.Add(new MapValidationError(MapValidationErrorCode.InvalidValue, message));

        if (PanelSizeMm <= 0)
        {
            Invalid($"パネルの一辺の長さは、1 以上にしてください（{PanelSizeMm}）。");
        }

        var panels = new List<Panel>();
        var placements = new Dictionary<PanelId, PanelPlacement>();
        foreach (var (draft, index) in Panels.Select((p, i) => (p, i)))
        {
            var name = $"パネル「{draft.Id}」";
            if (string.IsNullOrWhiteSpace(draft.Id))
            {
                Invalid($"{index + 1} 枚目のパネルの ID がありません。");
            }

            if (!Enum.GetNames<PanelType>().Contains(draft.Type, StringComparer.Ordinal))
            {
                Invalid($"{name}の種類がわかりません（{draft.Type}）。");
            }

            if (draft.CarrierLoweringHeading is { } lowering && !IsHeading(lowering))
            {
                Invalid($"{name}のキャリアを下ろす角度は、0 以上 360 未満にしてください（{lowering}）。");
            }

            if (draft.Outline is { WidthMm: <= 0 } or { HeightMm: <= 0 })
            {
                Invalid($"{name}の外形の幅と奥行きは、1 以上にしてください。");
            }

            if (invalid.Count > 0)
            {
                continue; // 値の型にできないものがあれば、この先は作らない
            }

            var id = new PanelId(draft.Id);
            panels.Add(new Panel(id, Enum.Parse<PanelType>(draft.Type), draft.HasChargingPin)
            {
                CarrierLoweringHeading = draft.CarrierLoweringHeading is { } degrees ? new Heading(degrees) : null,
            });

            // 同じ ID のパネルは、検証で重複として返す（描く情報は、先のものを使う）
            placements.TryAdd(id, new PanelPlacement(
                new GridPosition(draft.Column, draft.Row),
                new CrossOffset(draft.CrossOffsetEastMm, draft.CrossOffsetSouthMm),
                draft.Outline is { } o ? new Outline(o.XMm, o.YMm, o.WidthMm, o.HeightMm) : null));
        }

        var connections = new List<Connection>();
        foreach (var draft in Connections)
        {
            if (string.IsNullOrWhiteSpace(draft.From) || string.IsNullOrWhiteSpace(draft.To))
            {
                Invalid("つながりの元か先のパネルがありません。");
                continue;
            }

            if (!IsHeading(draft.Heading))
            {
                Invalid($"{draft.From} から {draft.To} へのつながりの向きは、0 以上 360 未満にしてください（{draft.Heading}）。");
                continue;
            }

            connections.Add(new Connection(new PanelId(draft.From), new PanelId(draft.To), draft.DistanceMm, new Heading(draft.Heading), draft.OneWay));
        }

        return invalid.Count > 0
            ? new MapSnapshotResult(null, invalid)
            : MapSnapshot.Create(panels, connections, options, new MapDrawing(PanelSizeMm, placements));
    }

    private static bool IsHeading(int degrees) => degrees is >= 0 and < 360;

    /// <summary>中身が同じか（並びの中身まで比べる）。</summary>
    public bool SameAs(MapDraft other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return PanelSizeMm == other.PanelSizeMm && Panels.SequenceEqual(other.Panels) && Connections.SequenceEqual(other.Connections);
    }
}

/// <summary>下書きのパネル。</summary>
/// <param name="Type">パネルの種類（<see cref="PanelType"/> の名前）。</param>
/// <param name="CarrierLoweringHeading">キャリアを下ろす角度（度）。なければ null。</param>
/// <param name="Outline">その下にある装置やステーションの外形。なければ null。</param>
public sealed record DraftPanel(
    string Id,
    string Type,
    bool HasChargingPin,
    int? CarrierLoweringHeading,
    int Column,
    int Row,
    int CrossOffsetEastMm,
    int CrossOffsetSouthMm,
    DraftOutline? Outline);

/// <summary>下書きのつながり。</summary>
/// <param name="Heading">From から To へ進むときの向き（度）。</param>
public sealed record DraftConnection(string From, string To, int DistanceMm, int Heading, bool OneWay);

/// <summary>下書きの外形（mm。マップの左上を原点にし、東と南を正にする）。</summary>
public sealed record DraftOutline(int XMm, int YMm, int WidthMm, int HeightMm);

/// <summary>保存してある下書きと、保存した日時。</summary>
public sealed record SavedMapDraft(MapDraft Draft, DateTimeOffset SavedAt);

/// <summary>
/// 今の下書き。
/// </summary>
/// <param name="Revision">下書きの番号。保存するたびと消すたびに進む（保存するときに、読んだときの番号を渡す）。まだ一度も保存していなければ 0。</param>
/// <param name="Saved">保存してある下書き。なければ（まだ保存していないか、公開して消したあと）null。</param>
public sealed record MapDraftState(int Revision, SavedMapDraft? Saved);

/// <summary>下書きを保存した結果。</summary>
public enum MapDraftSaveResult
{
    /// <summary>保存した。</summary>
    Saved,

    /// <summary>読んだあとに、ほかの人が保存したか消した。上書きしていない（読み直してもらう）。</summary>
    Stale,
}

/// <summary>下書きを保存した結果と、今の下書きの番号。</summary>
public sealed record MapDraftSave(MapDraftSaveResult Result, int Revision);
