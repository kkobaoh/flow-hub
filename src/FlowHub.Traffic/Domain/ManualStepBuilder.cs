using FlowHub.Map;
using FlowHub.Routing;

namespace FlowHub.Traffic.Domain;

/// <summary>
/// マニュアルの1つずつの動作を、マップの決まりを確かめて、1つの区間にする。
/// </summary>
/// <remarks>
/// 受け渡しパネルと待機パネルは行き止まり：前進でのみ入れて、後退でのみ出られる。中では回転できない。
/// 前進と後退は、ラインの向き（0・90・180・270度）を向いているときだけ。回転（ラインを読み取る）も、その4つの角度へだけ。
/// </remarks>
internal static class ManualStepBuilder
{
    /// <summary>区間にする。マップの決まりを守れなければ null。</summary>
    /// <param name="start">動作を始める姿勢（並べた動作があれば、それを終えたあと）。</param>
    public static TravelSection? Build(MapSnapshot map, Pose start, ManualStep step)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(step);

        var here = new PanelId(start.NodeId);
        var panel = map.Panels.Single(p => p.Id == here);
        switch (step)
        {
            case ManualRotate rotate when IsLineHeading(rotate.Heading) && !panel.IsDeadEnd:
                return new TravelSection([new RotateCommand(rotate.Heading)], [here], new Pose(start.NodeId, rotate.Heading));

            case ManualFreeRotate free when free.Heading is >= 0 and < 360 && !panel.IsDeadEnd:
                return new TravelSection([new FreeRotateCommand(free.Heading)], [here], new Pose(start.NodeId, free.Heading));

            case ManualForward when IsLineHeading(start.Heading) && !panel.IsDeadEnd:
                return Traverse(map, here, start.Heading) is { } ahead
                    ? new TravelSection([new ForwardCommand(ahead.DistanceMm, ahead.To)], [here, ahead.To], new Pose(ahead.To.Value, start.Heading))
                    : null;

            case ManualBackward when IsLineHeading(start.Heading):
                return Traverse(map, here, (start.Heading + 180) % 360) is { } behind && !map.Panels.Single(p => p.Id == behind.To).IsDeadEnd
                    ? new TravelSection([new BackwardCommand(behind.DistanceMm, behind.To)], [here, behind.To], new Pose(behind.To.Value, start.Heading))
                    : null;

            default:
                return null;
        }
    }

    private static bool IsLineHeading(int heading) => heading is 0 or 90 or 180 or 270;

    /// <summary>パネルから、その方向（進む方向の絶対角度）へ進める隣のパネル。一方通行の逆には進めない。</summary>
    private static (PanelId To, int DistanceMm)? Traverse(MapSnapshot map, PanelId from, int direction)
    {
        foreach (var connection in map.Connections)
        {
            if (connection.From == from && connection.Heading.Degrees == direction)
            {
                return (connection.To, connection.DistanceMm);
            }

            if (!connection.OneWay && connection.To == from && (connection.Heading.Degrees + 180) % 360 == direction)
            {
                return (connection.From, connection.DistanceMm);
            }
        }

        return null;
    }
}
