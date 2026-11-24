using System.Reflection;
using FlowHub.BuildingBlocks;
using FlowHub.Traffic;
using FlowHub.Traffic.Application;

namespace FlowHub.ArchitectureTests;

/// <summary>
/// 層の間の依存の向きと、イベントのルール（CLAUDE.md「ルール」「コンテキストの間のやり取り」）を守らせるテスト。
/// </summary>
public class LayerAndEventRulesTests
{
    private enum Layer
    {
        None,
        Domain,
        Application,
        Infrastructure,
    }

    public static TheoryData<string> Contexts => new(ContextRules.ContextAssemblies.Select(a => a.GetName().Name!));

    private static Type[] TypesOf(string context) => Assembly.Load(context).GetTypes();

    private static IEnumerable<Type> AllContextTypes => ContextRules.ContextAssemblies.SelectMany(a => a.GetTypes());

    /// <summary>名前空間から層を決める（FlowHub.コンテキスト.層...）。</summary>
    private static Layer LayerOf(Type type)
    {
        var segments = (type.Namespace ?? string.Empty).Split('.');
        return segments is ["FlowHub", _, var layer, ..] && Enum.TryParse<Layer>(layer, out var parsed) ? parsed : Layer.None;
    }

    private static bool IsFlowHub(Type type) => type.Assembly.GetName().Name?.StartsWith("FlowHub.", StringComparison.Ordinal) == true;

    /// <summary>使ってはいけない層の型を使っている箇所（「使う側 → 使われる側」）。</summary>
    private static string[] Violations(string context, Layer user, params Layer[] forbidden) =>
        TypesOf(context)
            .Where(type => LayerOf(type) == user)
            .SelectMany(type => TypeReferences.UsedTypes(type)
                .Where(used => IsFlowHub(used) && forbidden.Contains(LayerOf(used)))
                .Select(used => $"{type.FullName} → {used.FullName}"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static Type? SubscribedEvent(Type type) =>
        type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventSubscriber<>))
            ?.GetGenericArguments()[0];

    // ---- 道具の確かめ ----

    [Fact]
    public void メソッドの中身でだけ使っている型も拾う()
    {
        // TrafficService は、AGV の走行（Domain の AgvTravel）を、メソッドの中身で作って使っている
        var travel = Type.GetType("FlowHub.Traffic.Domain.AgvTravel, FlowHub.Traffic", throwOnError: true)!;

        Assert.Contains(travel, TypeReferences.UsedTypes(typeof(TrafficService)));
    }

    [Fact]
    public void 公開しているイベントを拾う()
    {
        Assert.Contains(typeof(AgvArrived), PublishedEvents());
    }

    // ---- 層の間の依存 ----

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Domainの層はApplicationとInfrastructureの層を使わない(string context)
    {
        Assert.Empty(Violations(context, Layer.Domain, Layer.Application, Layer.Infrastructure));
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Applicationの層はInfrastructureの層を使わない(string context)
    {
        Assert.Empty(Violations(context, Layer.Application, Layer.Infrastructure));
    }

    // ---- イベント ----

    [Fact]
    public void 購読してよいのは依存してよい相手のイベントだけ()
    {
        var violations = AllContextTypes
            .Where(type => !type.IsAbstract && SubscribedEvent(type) is not null)
            .Select(type => (Subscriber: type, Event: SubscribedEvent(type)!))
            .Where(pair => !ContextRules.AllowedReferences[pair.Subscriber.Assembly.GetName().Name!].Contains(pair.Event.Assembly.GetName().Name))
            .Select(pair => $"{pair.Subscriber.FullName} → {pair.Event.FullName}")
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void イベントを公開できるのはApplicationの層だけ()
    {
        var violations = AllContextTypes
            .SelectMany(type => TypeReferences.CalledMethods(type)
                .Where(call => call.Called.DeclaringType == typeof(IEventPublisher) && LayerOf(type) != Layer.Application)
                .Select(call => $"{type.FullName}.{call.Caller.Name}"))
            .Distinct()
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void 公開されているのに誰も購読していないイベントがない()
    {
        // 組み立て（FlowHub.Api）の購読者も数える（画面やログに知らせるイベントは、組み立てが購読する）
        var subscribed = AllContextTypes
            .Concat(Assembly.Load("FlowHub.Api").GetTypes())
            .Select(SubscribedEvent)
            .OfType<Type>()
            .ToHashSet();

        var unsubscribed = PublishedEvents()
            .Where(published => !subscribed.Contains(published))
            .Select(published => published.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unsubscribed);
    }

    /// <summary>コンテキストのコードが作っている（公開している）イベント。</summary>
    private static HashSet<Type> PublishedEvents() =>
        AllContextTypes
            .SelectMany(type => TypeReferences.CalledMethods(type))
            .Select(call => call.Called)
            .OfType<ConstructorInfo>()
            .Select(constructor => constructor.DeclaringType!)
            .Where(type => typeof(IIntegrationEvent).IsAssignableFrom(type))
            .ToHashSet();
}
