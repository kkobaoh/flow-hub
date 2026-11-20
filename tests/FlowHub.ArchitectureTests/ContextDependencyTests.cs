using System.Reflection;
using System.Xml.Linq;

namespace FlowHub.ArchitectureTests;

/// <summary>
/// コンテキストの間の依存の向き（CLAUDE.md「依存の向き」の表）を守らせるテスト。
/// </summary>
public class ContextDependencyTests
{
    private static Dictionary<string, string[]> AllowedReferences => ContextRules.AllowedReferences;

    /// <summary>
    /// コンテキストではなく、組み立てを担当するプロジェクト。どのコンテキストも参照してよい。
    /// </summary>
    private const string HostProject = "FlowHub.Api";

    /// <summary>
    /// コンテキストではなく、装置・ステーション・上位システムのポートを仮に実装するシミュレーター。組み立てだけが参照する。
    /// </summary>
    private const string SimulatorProject = "FlowHub.Simulator";

    /// <summary>シミュレーターが参照してよい相手（ポートを持つ消耗品の管理と、その公開インターフェースが使う配車）。</summary>
    private static readonly string[] SimulatorAllowedReferences = ["FlowHub.Consumables", "FlowHub.Dispatching"];

    public static TheoryData<string> Contexts => new(AllowedReferences.Keys);

    [Fact]
    public void すべてのコンテキストが依存のルールの表に載っている()
    {
        var projects = Directory.GetDirectories(Path.Combine(RepositoryRoot, "src"))
            .Select(Path.GetFileName)
            .Where(name => name is not (HostProject or SimulatorProject))
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

    [Fact]
    public void シミュレーターのプロジェクト参照は消耗品の管理と配車だけ()
    {
        var csproj = Path.Combine(RepositoryRoot, "src", SimulatorProject, $"{SimulatorProject}.csproj");
        var referenced = XDocument.Load(csproj)
            .Descendants("ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value));

        Assert.Empty(referenced.Except(SimulatorAllowedReferences));
    }

    [Fact]
    public void シミュレーターのアセンブリが使っているのは消耗品の管理と配車だけ()
    {
        var used = Assembly.Load(SimulatorProject)
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("FlowHub.", StringComparison.Ordinal));

        Assert.Empty(used.Except(SimulatorAllowedReferences));
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
