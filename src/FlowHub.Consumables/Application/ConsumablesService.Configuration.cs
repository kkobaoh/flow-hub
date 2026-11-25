namespace FlowHub.Consumables.Application;

/// <summary>
/// 消耗品の構成（加工の種類、品種、入庫の予定）を、動いている間に加える・変える・消す（消耗品の構成の画面から）。
/// 変えたら保存し、判断にかかわるので在庫が変わったことを知らせる。名前（ID）は変えられない。
/// </summary>
public sealed partial class ConsumablesService
{
    /// <summary>画面に出す、消耗品の構成。</summary>
    public ConsumablesConfigurationView ReadConfiguration() => Locked(() =>
    {
        var processTypes = _processTypes.Values.OrderBy(p => p.Id.Value, StringComparer.Ordinal).ToArray();
        var varieties = _varieties
            .OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)
            .Select(pair => new VarietyConfigurationView(
                pair.Key,
                pair.Value,
                _expected.Where(e => e.Value == pair.Key).Select(e => e.Key).OrderBy(id => id.Value, StringComparer.Ordinal).ToArray(),
                _consumables.Values.Where(c => c.Variety == pair.Key).Select(c => c.Id).OrderBy(id => id.Value, StringComparer.Ordinal).ToArray(),
                IsVarietyInUse(pair.Key)))
            .ToArray();
        return new ConsumablesConfigurationView(processTypes, varieties);
    });

    /// <summary>加工の種類を加える。</summary>
    public Task<ConfigurationChangeResult> AddProcessTypeAsync(ProcessTypeSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return ChangeConfigurationAsync($"process:{settings.Id}", () =>
        {
            if (_processTypes.ContainsKey(settings.Id))
            {
                return ConfigurationChangeResult.AlreadyRegistered;
            }

            _persistence.SaveProcessType(settings);
            _processTypes[settings.Id] = settings;
            return ConfigurationChangeResult.Saved;
        }, cancellationToken);
    }

    /// <summary>加工の種類のキャリアの種類と、キャリアに入る個数を変える（品種があっても変えられ、次の判断から効く）。</summary>
    public Task<ConfigurationChangeResult> UpdateProcessTypeAsync(ProcessTypeSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return ChangeConfigurationAsync($"process:{settings.Id}", () =>
        {
            if (!_processTypes.ContainsKey(settings.Id))
            {
                return ConfigurationChangeResult.NotFound;
            }

            _persistence.SaveProcessType(settings);
            _processTypes[settings.Id] = settings;
            return ConfigurationChangeResult.Saved;
        }, cancellationToken);
    }

    /// <summary>加工の種類を消す（その加工の種類の品種がないときだけ）。</summary>
    public Task<ConfigurationChangeResult> DeleteProcessTypeAsync(ProcessTypeId processType, CancellationToken cancellationToken) =>
        ChangeConfigurationAsync($"process:{processType}", () =>
        {
            if (!_processTypes.ContainsKey(processType))
            {
                return ConfigurationChangeResult.NotFound;
            }

            if (_varieties.ContainsValue(processType))
            {
                return ConfigurationChangeResult.InUse;
            }

            _persistence.DeleteProcessType(processType);
            _processTypes.Remove(processType);
            return ConfigurationChangeResult.Saved;
        }, cancellationToken);

    /// <summary>品種を加える。</summary>
    public Task<ConfigurationChangeResult> AddVarietyAsync(VarietyId variety, ProcessTypeId processType, CancellationToken cancellationToken) =>
        ChangeConfigurationAsync($"variety:{variety}", () =>
        {
            if (_varieties.ContainsKey(variety))
            {
                return ConfigurationChangeResult.AlreadyRegistered;
            }

            if (!_processTypes.ContainsKey(processType))
            {
                return ConfigurationChangeResult.NotFound;
            }

            _persistence.SaveVariety(variety, processType);
            _varieties[variety] = processType;
            return ConfigurationChangeResult.Saved;
        }, cancellationToken);

    /// <summary>品種の加工の種類を変える（使われていないときだけ）。</summary>
    public Task<ConfigurationChangeResult> UpdateVarietyAsync(VarietyId variety, ProcessTypeId processType, CancellationToken cancellationToken) =>
        ChangeConfigurationAsync($"variety:{variety}", () =>
        {
            if (!_varieties.ContainsKey(variety) || !_processTypes.ContainsKey(processType))
            {
                return ConfigurationChangeResult.NotFound;
            }

            if (IsVarietyInUse(variety))
            {
                return ConfigurationChangeResult.InUse;
            }

            _persistence.SaveVariety(variety, processType);
            _varieties[variety] = processType;
            return ConfigurationChangeResult.Saved;
        }, cancellationToken);

    /// <summary>
    /// 品種を消す（使われていないときだけ）。入庫の予定、ストッカーの設定に残っている個数 0 の行、補充を頼んだ数も一緒に消す。
    /// </summary>
    public Task<ConfigurationChangeResult> DeleteVarietyAsync(VarietyId variety, CancellationToken cancellationToken) =>
        ChangeConfigurationAsync($"variety:{variety}", () =>
        {
            if (!_varieties.ContainsKey(variety))
            {
                return ConfigurationChangeResult.NotFound;
            }

            if (IsVarietyInUse(variety))
            {
                return ConfigurationChangeResult.InUse;
            }

            _persistence.DeleteVariety(variety);
            _varieties.Remove(variety);
            foreach (var id in _expected.Where(e => e.Value == variety).Select(e => e.Key).ToArray())
            {
                _expected.Remove(id);
            }

            foreach (var (device, counts) in _settings.Where(s => s.Value.ContainsKey(variety)).ToArray())
            {
                _settings[device] = counts.Where(c => c.Key != variety).ToDictionary(c => c.Key, c => c.Value);
            }

            _outstanding.Remove(variety);
            return ConfigurationChangeResult.Saved;
        }, cancellationToken);

    /// <summary>入庫の予定（ID と品種の対応）を加える。どの品種にもまだ対応させておらず、消耗品の記録にもない ID だけ。</summary>
    public Task<ConfigurationChangeResult> AddExpectedArrivalAsync(ConsumableId id, VarietyId variety, CancellationToken cancellationToken) =>
        ChangeConfigurationAsync($"arrival:{id}", () =>
        {
            if (_expected.ContainsKey(id) || _consumables.ContainsKey(id))
            {
                return ConfigurationChangeResult.AlreadyRegistered;
            }

            if (!_varieties.ContainsKey(variety))
            {
                return ConfigurationChangeResult.NotFound;
            }

            _persistence.AddExpectedArrival(id, variety);
            _expected[id] = variety;
            return ConfigurationChangeResult.Saved;
        }, cancellationToken);

    /// <summary>入庫の予定を外す（入庫の予定にある ID だけ。入庫した ID は、もう予定にない）。</summary>
    public Task<ConfigurationChangeResult> RemoveExpectedArrivalAsync(ConsumableId id, CancellationToken cancellationToken) =>
        ChangeConfigurationAsync($"arrival:{id}", () =>
        {
            if (!_expected.ContainsKey(id))
            {
                return ConfigurationChangeResult.NotFound;
            }

            _persistence.RemoveExpectedArrival(id);
            _expected.Remove(id);
            return ConfigurationChangeResult.Saved;
        }, cancellationToken);

    /// <summary>品種が使われているか（消耗品の記録にあるか、ストッカーの設定で1個以上にしている装置がある）。</summary>
    private bool IsVarietyInUse(VarietyId variety) =>
        _consumables.Values.Any(c => c.Variety == variety)
        || _settings.Values.Any(counts => counts.GetValueOrDefault(variety) > 0);

    /// <summary>ロックの中で構成を変え、保存したら在庫が変わったことを知らせる（保存はロックの中で、知らせるのはロックの外で）。</summary>
    private async Task<ConfigurationChangeResult> ChangeConfigurationAsync(
        string correlationId, Func<ConfigurationChangeResult> change, CancellationToken cancellationToken)
    {
        var result = await LockedAsync(change, cancellationToken).ConfigureAwait(false);
        if (result == ConfigurationChangeResult.Saved)
        {
            await PublishStockChangedAsync($"configuration:{correlationId}", cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
