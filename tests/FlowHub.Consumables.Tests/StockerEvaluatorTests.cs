using FlowHub.Consumables.Domain;

namespace FlowHub.Consumables.Tests;

public class StockerEvaluatorTests
{
    private static readonly VarietyId A = new("A");
    private static readonly VarietyId B = new("B");
    private static readonly VarietyId X = new("X");

    private static int _sequence;

    private static StockedItem New(VarietyId variety, string? id = null) =>
        new(new ConsumableId(id ?? $"{variety}-{Interlocked.Increment(ref _sequence)}"), variety, ConsumableCondition.New);

    private static StockedItem Used(VarietyId variety, string? id = null) =>
        new(new ConsumableId(id ?? $"{variety}-used-{Interlocked.Increment(ref _sequence)}"), variety, ConsumableCondition.Used);

    private static StockerSetting Setting(int? capacity, params (VarietyId Variety, int Count)[] counts) =>
        new(counts.ToDictionary(c => c.Variety, c => c.Count), capacity);

    private static StockerEvaluation Evaluate(
        StockerSetting setting,
        IEnumerable<StockedItem> items,
        IReadOnlyDictionary<VarietyId, int>? inbound = null,
        IEnumerable<ConsumableId>? recoveryRequested = null) =>
        StockerEvaluator.Evaluate(setting, new StockerContents(
            items.ToArray(),
            inbound ?? new Dictionary<VarietyId, int>(),
            (recoveryRequested ?? []).ToHashSet()));

    // ---- 不足 ----

    [Fact]
    public void 新品が設定どおりあれば不足はない()
    {
        var result = Evaluate(Setting(6, (A, 2)), [New(A), New(A)]);

        Assert.Empty(result.Shortages);
    }

    [Fact]
    public void 新品が設定を1個でも下回ったら不足()
    {
        var result = Evaluate(Setting(6, (A, 3)), [New(A), New(A)]);

        Assert.Equal(new Dictionary<VarietyId, int> { [A] = 1 }, result.Shortages);
    }

    [Fact]
    public void 中古品は在庫として数えない()
    {
        var result = Evaluate(Setting(6, (A, 2)), [New(A), Used(A)]);

        Assert.Equal(new Dictionary<VarietyId, int> { [A] = 1 }, result.Shortages);
    }

    [Fact]
    public void 届ける途中の新品も数える()
    {
        var result = Evaluate(Setting(6, (A, 3)), [New(A)], inbound: new Dictionary<VarietyId, int> { [A] = 2 });

        Assert.Empty(result.Shortages);
    }

    [Fact]
    public void 品種ごとに不足を数える()
    {
        var result = Evaluate(Setting(6, (A, 2), (B, 2)), [New(A)]);

        Assert.Equal(new Dictionary<VarietyId, int> { [A] = 1, [B] = 2 }, result.Shortages);
    }

    // ---- 設定にない品種 ----

    [Fact]
    public void 設定にない品種の新品は回収する()
    {
        var x = New(X, "x1");

        var result = Evaluate(Setting(6, (A, 1)), [New(A), x]);

        Assert.Equal([new RecoveryNeed(x.Id, RecoveryReason.NotInSetting)], result.Recoveries);
    }

    [Fact]
    public void 設定にない品種でも中古品は自動では回収しない()
    {
        // 中古品は装置からの廃棄リクエストで回収する
        var result = Evaluate(Setting(6, (A, 1)), [New(A), Used(X)]);

        Assert.Empty(result.Recoveries);
    }

    [Fact]
    public void すでに回収を頼んだ消耗品はもう一度回収を頼まない()
    {
        var x = New(X, "x1");

        var result = Evaluate(Setting(6, (A, 1)), [New(A), x], recoveryRequested: [x.Id]);

        Assert.Empty(result.Recoveries);
    }

    [Fact]
    public void 回収を頼んだ消耗品は在庫として数えない()
    {
        // 回収を頼んだ品種 A の新品は、もうすぐなくなるので不足を補う必要がある
        var a = New(A, "a1");

        var result = Evaluate(Setting(6, (A, 1)), [a], recoveryRequested: [a.Id]);

        Assert.Equal(new Dictionary<VarietyId, int> { [A] = 1 }, result.Shortages);
    }

    // ---- 設定より多い品種 ----

    [Fact]
    public void 設定より多い品種も空きが足りていれば回収しない()
    {
        // 容量 6：A が 4個（設定 2）、B が不足 2 → 空き 2 で足りる
        var result = Evaluate(Setting(6, (A, 2), (B, 2)), [New(A), New(A), New(A), New(A)]);

        Assert.Equal(new Dictionary<VarietyId, int> { [B] = 2 }, result.Shortages);
        Assert.Empty(result.Recoveries);
    }

    [Fact]
    public void 設定より多い品種のせいでほかの品種を置けないときは多い分を回収する()
    {
        // 容量 4：A が 4個（設定 2）、B が不足 2 → 空き 0 なので、A を 2個回収する
        var result = Evaluate(
            Setting(4, (A, 2), (B, 2)),
            [New(A, "a1"), New(A, "a2"), New(A, "a3"), New(A, "a4")]);

        Assert.Equal(2, result.Recoveries.Count);
        Assert.All(result.Recoveries, r => Assert.Equal(RecoveryReason.Surplus, r.Reason));
    }

    [Fact]
    public void 多い分の回収は足りない空きの数だけにする()
    {
        // 容量 5：A が 4個（設定 2）、B が不足 2 → 空き 1 なので、A を 1個だけ回収する
        var result = Evaluate(
            Setting(5, (A, 2), (B, 2)),
            [New(A, "a1"), New(A, "a2"), New(A, "a3"), New(A, "a4")]);

        Assert.Single(result.Recoveries);
    }

    [Fact]
    public void 設定にない品種の回収で空く分も空きに数える()
    {
        // 容量 4：A が 3個（設定 2）、X が 1個、B が不足 2
        // X の回収で 1つ空き、さらに A を 1個回収すれば 2つ空く
        var result = Evaluate(
            Setting(4, (A, 2), (B, 2)),
            [New(A, "a1"), New(A, "a2"), New(A, "a3"), New(X, "x1")]);

        Assert.Equal(1, result.Recoveries.Count(r => r.Reason == RecoveryReason.Surplus));
        Assert.Equal(1, result.Recoveries.Count(r => r.Reason == RecoveryReason.NotInSetting));
    }

    [Fact]
    public void 中古品もスロットをふさいでいるものとして空きを数える()
    {
        // 容量 4：A が新品 3個（設定 2）、中古品 1個、B が不足 1 → 空き 0 なので、A を 1個回収する
        var result = Evaluate(
            Setting(4, (A, 2), (B, 1)),
            [New(A), New(A), New(A), Used(A)]);

        Assert.Equal([RecoveryReason.Surplus], result.Recoveries.Select(r => r.Reason));
    }

    [Fact]
    public void 届ける途中の消耗品も空きをふさぐものとして数える()
    {
        // 容量 4：A が 3個（設定 2）、B が不足 2 のうち 1個は届ける途中 → 空き 0、残りの不足 1 → A を 1個回収する
        var result = Evaluate(
            Setting(4, (A, 2), (B, 2)),
            [New(A), New(A), New(A)],
            inbound: new Dictionary<VarietyId, int> { [B] = 1 });

        Assert.Equal([RecoveryReason.Surplus], result.Recoveries.Select(r => r.Reason));
    }

    [Fact]
    public void 容量がわからない装置では多い分を回収しない()
    {
        // スロットのない装置などで容量がわからないときは、空きを判断できない
        var result = Evaluate(Setting(null, (A, 2), (B, 2)), [New(A), New(A), New(A), New(A)]);

        Assert.Empty(result.Recoveries);
    }

    [Fact]
    public void 設定の個数は0以上でなければならない()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Setting(4, (A, -1)));
    }

    [Fact]
    public void 容量は0以上でなければならない()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Setting(-1, (A, 1)));
    }
}
