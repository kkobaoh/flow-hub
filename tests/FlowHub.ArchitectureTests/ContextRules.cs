using System.Reflection;

namespace FlowHub.ArchitectureTests;

/// <summary>
/// アーキテクチャテストで使う、コンテキストの決まり。
/// </summary>
internal static class ContextRules
{
    /// <summary>
    /// コンテキストごとに、参照してよい相手。CLAUDE.md の「依存の向き」の表と一致させること。
    /// </summary>
    public static readonly Dictionary<string, string[]> AllowedReferences = new()
    {
        ["FlowHub.BuildingBlocks"] = [],
        ["FlowHub.Map"] = ["FlowHub.BuildingBlocks"],
        ["FlowHub.Routing"] = [],
        ["FlowHub.PathPlanning"] = [],
        ["FlowHub.Notifications"] = [],
        ["FlowHub.Identity"] = [],
        ["FlowHub.Traffic"] = ["FlowHub.BuildingBlocks", "FlowHub.Routing", "FlowHub.PathPlanning", "FlowHub.Map"],
        ["FlowHub.Dispatching"] = ["FlowHub.BuildingBlocks", "FlowHub.Traffic", "FlowHub.Routing", "FlowHub.Map"],
        ["FlowHub.Consumables"] = ["FlowHub.BuildingBlocks", "FlowHub.Dispatching", "FlowHub.Map"],
        ["FlowHub.ToolPlanning"] = ["FlowHub.BuildingBlocks", "FlowHub.Consumables", "FlowHub.Dispatching", "FlowHub.Map"],
    };

    /// <summary>コンテキストのアセンブリ（BuildingBlocks は、コンテキストの間のやり取りの部品なので入れない）。</summary>
    public static IReadOnlyList<Assembly> ContextAssemblies { get; } =
        AllowedReferences.Keys.Where(name => name != "FlowHub.BuildingBlocks").Select(Assembly.Load).ToArray();
}
