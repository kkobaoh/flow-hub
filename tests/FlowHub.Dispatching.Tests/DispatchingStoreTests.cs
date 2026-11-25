using FlowHub.Dispatching.Infrastructure;
using FlowHub.Traffic;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FlowHub.Dispatching.Tests;

/// <summary>
/// 配車の DB（AGV のキャリアの種類、充電の設定値）のテスト。メモリ上の SQLite を使う。
/// </summary>
public sealed class DispatchingStoreTests : IDisposable
{
    private static readonly AgvId Agv = new("AGV-1");
    private static readonly CarrierTypeId Carrier = new("X");

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public DispatchingStoreTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>同じ接続を使うので、開き直すと「次に起動したとき」と同じになる。</summary>
    private Task<DispatchingStore> OpenAsync() =>
        DispatchingDatabase.OpenAsync(
            builder => builder.UseSqlite(_connection),
            (context, cancellationToken) => context.Database.EnsureCreatedAsync(cancellationToken),
            Token);

    private async Task<DispatchingAgvConfiguration> LoadSingleAsync() =>
        Assert.Single(await (await OpenAsync()).LoadAgvsAsync(Token));

    // ---- AGV の種類の一覧、変える・消す ----

    [Fact]
    public async Task 種類の充電の値を読み変えられない種類は断る()
    {
        var store = await OpenAsync();
        await store.AddAgvTypeAsync("small", 30, null, Token);

        Assert.Equal(AgvTypeChangeResult.Changed, await store.UpdateAgvTypeAsync("small", 40, 90, Token));
        Assert.Equal(AgvTypeChangeResult.NotFound, await store.UpdateAgvTypeAsync("none", 40, 90, Token));
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpdateAgvTypeAsync("small", 90, 40, Token)); // 戻す値が向かう値より小さい

        Assert.Equal([new DispatchingAgvTypeSettings("small", 40, 90)], await (await OpenAsync()).LoadAgvTypesAsync(Token));
    }

    [Fact]
    public async Task 使うAGVがない種類だけを消せる()
    {
        var store = await OpenAsync();
        await store.AddAgvTypeAsync("small", null, null, Token);
        await store.AddAgvTypeAsync("large", null, null, Token);
        await store.AddAgvAsync(Agv, "small", Carrier, Token);

        Assert.Equal(AgvTypeChangeResult.InUse, await store.DeleteAgvTypeAsync("small", Token));
        Assert.Equal(AgvTypeChangeResult.Changed, await store.DeleteAgvTypeAsync("large", Token));
        Assert.Equal(AgvTypeChangeResult.NotFound, await store.DeleteAgvTypeAsync("large", Token));
        Assert.Equal([Agv], await store.AgvsOfTypeAsync("small", Token));
    }

    [Fact]
    public async Task 充電の既定値を読む設定していなければ20と80()
    {
        var store = await OpenAsync();
        Assert.Equal((20, 80), await store.ChargingDefaultsAsync(Token));

        await store.SetChargingDefaultsAsync(25, 85, Token);
        Assert.Equal((25, 85), await store.ChargingDefaultsAsync(Token));
    }

    [Fact]
    public async Task 登録したAGVをキャリアの種類と充電の設定値と一緒に読む()
    {
        var store = await OpenAsync();
        await store.AddAgvTypeAsync("small", goChargeBelowPercent: 30, resumeAtPercent: 90, Token);
        await store.AddAgvAsync(Agv, "small", Carrier, Token);

        Assert.Equal(new DispatchingAgvConfiguration(Agv, Carrier, 30, 90), await LoadSingleAsync());
    }

    [Fact]
    public async Task 種類の充電の設定値を省略して既定値も設定していなければ20と80を使う()
    {
        var store = await OpenAsync();
        await store.AddAgvTypeAsync("small", goChargeBelowPercent: null, resumeAtPercent: null, Token);
        await store.AddAgvAsync(Agv, "small", Carrier, Token);

        Assert.Equal(new DispatchingAgvConfiguration(Agv, Carrier, 20, 80), await LoadSingleAsync());
    }

    [Fact]
    public async Task 既定値を設定したら種類の値を省略したときに使う()
    {
        var store = await OpenAsync();
        await store.SetChargingDefaultsAsync(goChargeBelowPercent: 25, resumeAtPercent: 85, Token);
        await store.AddAgvTypeAsync("small", goChargeBelowPercent: null, resumeAtPercent: null, Token);
        await store.AddAgvAsync(Agv, "small", Carrier, Token);

        Assert.Equal(new DispatchingAgvConfiguration(Agv, Carrier, 25, 85), await LoadSingleAsync());
    }

    [Fact]
    public async Task 既定値を変えても登録済みの種類の値は変わらず以降に登録する種類から効く()
    {
        // 既定値は、種類を登録するときに埋めて保存する
        var store = await OpenAsync();
        await store.SetChargingDefaultsAsync(goChargeBelowPercent: 25, resumeAtPercent: 85, Token);
        await store.AddAgvTypeAsync("old", goChargeBelowPercent: null, resumeAtPercent: null, Token);
        await store.SetChargingDefaultsAsync(goChargeBelowPercent: 15, resumeAtPercent: 70, Token);
        await store.AddAgvTypeAsync("new", goChargeBelowPercent: null, resumeAtPercent: null, Token);
        await store.AddAgvAsync(new AgvId("AGV-OLD"), "old", Carrier, Token);
        await store.AddAgvAsync(new AgvId("AGV-NEW"), "new", Carrier, Token);

        var agvs = await (await OpenAsync()).LoadAgvsAsync(Token);

        Assert.Equal(
            [new DispatchingAgvConfiguration(new AgvId("AGV-NEW"), Carrier, 15, 70), new DispatchingAgvConfiguration(new AgvId("AGV-OLD"), Carrier, 25, 85)],
            agvs);
    }

    [Fact]
    public async Task 既定値で埋めた結果で割り当てに戻す値が充電に向かう値より小さければ種類を登録しない()
    {
        // 充電に向かう値だけ 90% にすると、割り当てに戻す値は既定値の 80% になり、決まりを破る
        var store = await OpenAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAgvTypeAsync("large", goChargeBelowPercent: 90, resumeAtPercent: null, Token));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAgvTypeAsync("bad", goChargeBelowPercent: 50, resumeAtPercent: 40, Token));
        Assert.True(await store.IsEmptyAsync(Token));
    }

    [Fact]
    public async Task 割り当てに戻す値が充電に向かう値と同じならよい()
    {
        var store = await OpenAsync();
        await store.AddAgvTypeAsync("same", goChargeBelowPercent: 50, resumeAtPercent: 50, Token);
        await store.AddAgvAsync(Agv, "same", Carrier, Token);

        Assert.Equal(new DispatchingAgvConfiguration(Agv, Carrier, 50, 50), await LoadSingleAsync());
    }

    [Fact]
    public async Task 既定値も割り当てに戻す値が充電に向かう値より小さければ設定しない()
    {
        var store = await OpenAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => store.SetChargingDefaultsAsync(goChargeBelowPercent: 60, resumeAtPercent: 50, Token));
    }

    [Theory]
    [InlineData(-1, 80)]
    [InlineData(20, 101)]
    public async Task 充電の設定値は0から100まで(int goBelow, int resumeAt)
    {
        var store = await OpenAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.AddAgvTypeAsync("small", goBelow, resumeAt, Token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.SetChargingDefaultsAsync(goBelow, resumeAt, Token));
    }

    [Fact]
    public async Task 登録されていない種類のAGVは登録できない()
    {
        var store = await OpenAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAgvAsync(Agv, "unknown", Carrier, Token));
    }

    [Fact]
    public async Task 何も登録していなければ空()
    {
        var store = await OpenAsync();

        Assert.True(await store.IsEmptyAsync(Token));
        Assert.Empty(await store.LoadAgvsAsync(Token));
    }
}
