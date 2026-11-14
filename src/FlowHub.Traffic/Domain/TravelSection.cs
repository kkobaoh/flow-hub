using FlowHub.Map;
using FlowHub.Routing;

namespace FlowHub.Traffic.Domain;

/// <summary>
/// AGV にまとめて出す走行の指示の単位（区間）。同じ向き（前進か後退）に進む動作の並びと、最後の回転からなる。
/// 出した指示はキャンセルできないため、区間で通るパネルは、指示を出す前にすべて予約する。
/// </summary>
/// <param name="Commands">走行の指示の並び。</param>
/// <param name="Panels">区間で AGV がいる可能性のあるパネル（出発のパネルから順に）。</param>
/// <param name="End">区間を終えたあとの姿勢。</param>
internal sealed record TravelSection(IReadOnlyList<TravelCommand> Commands, IReadOnlyList<PanelId> Panels, Pose End);

/// <summary>
/// 経路（動作の並び）を区間に分ける。区間は、回転と、前進・後退の切り替えで区切る。
/// </summary>
internal static class TravelSectionBuilder
{
    public static IReadOnlyList<TravelSection> Build(Pose start, IReadOnlyList<RouteStep> steps)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(steps);

        var builder = new SectionsInProgress(start);

        foreach (var step in steps)
        {
            switch (step)
            {
                case MoveStep move:
                    // 前進と後退は別の指示なので、切り替わるところで区切る
                    if (builder.LastDirection is { } last && last != move.Direction)
                    {
                        builder.Close();
                    }

                    builder.AddMove(move);
                    break;

                case RotateStep rotate:
                    // 回転は区間の最後に入れて区切る。次の区間は、回転を終えた姿勢から始まる。
                    builder.AddRotate(rotate);
                    builder.Close();
                    break;

                default:
                    throw new ArgumentException($"未知の動作です：{step.GetType().Name}", nameof(steps));
            }
        }

        builder.Close();
        return builder.Sections;
    }

    /// <summary>
    /// 組み立て中の区間。
    /// </summary>
    private sealed class SectionsInProgress(Pose start)
    {
        private readonly List<TravelCommand> _commands = [];
        private List<PanelId> _panels = [new(start.NodeId)];
        private Pose _current = start;

        public List<TravelSection> Sections { get; } = [];

        /// <summary>組み立て中の区間で最後に進んだ向き。まだ進んでいなければ null。</summary>
        public MoveDirection? LastDirection { get; private set; }

        public void AddMove(MoveStep move)
        {
            _commands.Add(move.Direction == MoveDirection.Forward
                ? new ForwardCommand(move.DistanceMm)
                : new BackwardCommand(move.DistanceMm));
            _panels.Add(new PanelId(move.To));
            _current = move.After;
            LastDirection = move.Direction;
        }

        public void AddRotate(RotateStep rotate)
        {
            _commands.Add(new RotateCommand(rotate.ToHeading));
            _current = rotate.After;
        }

        /// <summary>
        /// 組み立て中の区間を確定し、今の姿勢から次の区間を始める。指示がなければ何もしない。
        /// </summary>
        public void Close()
        {
            if (_commands.Count == 0)
            {
                return;
            }

            Sections.Add(new TravelSection(_commands.ToArray(), _panels.ToArray(), _current));
            _commands.Clear();
            _panels = [new PanelId(_current.NodeId)];
            LastDirection = null;
        }
    }
}
