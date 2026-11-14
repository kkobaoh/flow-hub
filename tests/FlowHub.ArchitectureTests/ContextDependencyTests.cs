using System.Reflection;
using System.Xml.Linq;

namespace FlowHub.ArchitectureTests;

/// <summary>
/// コンテキストの間の依存の向き（CLAUDE.md「依存の向き」の表）を守らせるテスト。
/// </summary>
public class ContextDependencyTests
{
    /// <summary>
    /// コンテキストごとに、参照してよい相手。CLAUDE.md の表と一致させること。
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedReferences = new()
    {
        ["FlowHub.BuildingBlocks"] = [],
        ["FlowHub.Map"] = ["FlowHub.BuildingBlocks"],
        ["FlowHub.Routing"] = [],
        ["FlowHub.Traffic"] = ["FlowHub.BuildingBlocks", "FlowHub.Routing", "FlowHub.Map"],
        ["FlowHub.Dispatching"] = ["FlowHub.BuildingBlocks", "FlowHub.Traffic", "FlowHub.Routing", "FlowHub.Map"],
        ["FlowHub.Consumables"] = ["FlowHub.BuildingBlocks", "FlowHub.Dispatching", "FlowHub.Map"],
        ["FlowHub.ToolPlanning"] = ["FlowHub.BuildingBlocks", "FlowHub.Consumables", "FlowHub.Dispatching", "FlowHub.Map"],
    };

    /// <summary>
    /// コンテキストではなく、組み立てを担当するプロジェクト。どのコンテキストも参照してよい。
    /// </summary>
    private const string HostProject = "FlowHub.Api";

    public static TheoryData<string> Contexts => new(AllowedReferences.Keys);

    [Fact]
    public void すべてのコンテキストが依存のルールの表に載っている()
    {
        var projects = Directory.GetDirectories(Path.Combine(RepositoryRoot, "src"))
            .Select(Path.GetFileName)
            .Where(name => name != HostProject)
            .Order()
            .ToArray();

        Assert.Equal(AllowedReferences.Keys.Order().ToArray(), projects);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void プロジェクト参照は参照してよい相手だけ(string context)
    {
        var csproj = Path.Combine(RepositoryRoot, "src", context, $"{context}.csproj");
        var referenced = XDocument.Load(csproj)
            .Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value));

        Assert.Empty(referenced.Except(AllowedReferences[context]));
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void アセンブリが使っているのは参照してよい相手だけ(string context)
    {
        var used = Assembly.Load(context)
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("FlowHub.", StringComparison.Ordinal));

        Assert.Empty(used.Except(AllowedReferences[context]));
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FlowHub.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("FlowHub.slnx が見つかりません。");
        }
    }
}
