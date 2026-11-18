namespace FlowHub.Consumables.Application;

/// <summary>
/// 届ける消耗品の指名（ユーザーが画面から、装置へ届ける消耗品を選ぶ）。
/// 指名された消耗品は、その装置の不足を補うときに、先入れ先出しより先に使う。ほかの装置へは届けない。
/// </summary>
public sealed partial class ConsumablesService
{
    /// <summary>指名（消耗品 → 届け先の装置）。条件に合わなくなったものは、参照するときに外す。</summary>
    private readonly Dictionary<ConsumableId, DeviceId> _designations = [];

    /// <summary>
    /// 届ける消耗品を指名する。ステーションにある、要確認でない新品で、届ける予約も出庫の頼みも指名もないものに限る。
    /// </summary>
    public void Designate(DeviceId device, ConsumableId id) =>
        Locked(() =>
        {
            Layout(device);
            ForgetInvalidDesignations();
            if (!IsDeliverable(Consumable(id)) || _deliveries.ContainsKey(id) || _designations.ContainsKey(id))
            {
                throw new InvalidOperationException($"消耗品 {id} は指名できません（ステーションにある、要確認でない新品で、届ける予約も出庫の頼みも指名もないものに限ります）。");
            }

            _designations[id] = device;
        });

    /// <summary>指名を取り消す。割り当て前かどうかは、搬送の要求を持つ搬送計画が確かめてから呼ぶ。</summary>
    public void CancelDesignation(ConsumableId id) => Locked(() => _designations.Remove(id));

    /// <summary>指名されているか（条件に合わなくなった指名は外れている）。</summary>
    public bool IsDesignated(ConsumableId id) =>
        Locked(() =>
        {
            ForgetInvalidDesignations();
            return _designations.ContainsKey(id);
        });

    /// <summary>
    /// その装置へ届けるように指名された、その品種の消耗品のうち、予約していないもの（入庫した順）。
    /// </summary>
    public IReadOnlyList<ConsumableId> GetDesignated(DeviceId device, VarietyId variety) =>
        Locked(() =>
        {
            ForgetInvalidDesignations();
            return _designations
                .Where(pair => pair.Value == device && _consumables[pair.Key].Variety == variety && !_deliveries.ContainsKey(pair.Key))
                .Select(pair => pair.Key)
                .OrderBy(id => _arrivalOrder.GetValueOrDefault(id))
                .ToArray();
        });

    /// <summary>
    /// ステーションにある、要確認でない新品で、出庫を頼んでいないもの（届けられるもの）か。
    /// </summary>
    private bool IsDeliverable(ConsumableView consumable) =>
        consumable is { Location: AtStation, Condition: ConsumableCondition.New, NeedsCheck: false, Variety: not null }
        && !_shipping.Contains(consumable.Id);

    /// <summary>
    /// 条件に合わなくなった（積み込んだ、出庫した、要確認になったなど）指名を外す。
    /// </summary>
    private void ForgetInvalidDesignations()
    {
        foreach (var id in _designations.Keys.Where(id => !_consumables.TryGetValue(id, out var c) || !IsDeliverable(c)).ToArray())
        {
            _designations.Remove(id);
        }
    }
}
