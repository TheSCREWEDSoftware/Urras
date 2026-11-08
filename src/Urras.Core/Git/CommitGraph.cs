namespace Urras.Core.Git;

// A line inside one history row, in lane units. Y is 0 at the top of the row, 0.5 at the commit dot, 1 at the
// bottom. Colour is a lane number, so a branch keeps its colour as long as it stays in its lane.
public readonly record struct GraphLine(int FromLane, double FromY, int ToLane, double ToY, int Colour);

// What to draw beside one commit: its dot, and the lines through the row
public sealed class GraphRow
{
    public GraphRow(int lane, int width, IReadOnlyList<GraphLine> lines, bool isMerge)
    {
        Lane = lane;
        Width = width;
        Lines = lines;
        IsMerge = isMerge;
    }

    public int Lane { get; }

    // Lanes in use in this row, for sizing the graph column
    public int Width { get; }
    public IReadOnlyList<GraphLine> Lines { get; }
    public bool IsMerge { get; }
}

// Lays out branch lanes for a history listed children before parents (git log --date-order)
public static class CommitGraph
{
    // Lanes beyond this are not drawn separately; very wide graphs are unreadable anyway
    public const int MAX_LANES = 24;

    public static void Assign(IReadOnlyList<CommitInfo> commits)
    {
        // Each lane holds the hash of the commit it is waiting for, or null when free
        List<string?> lanes = new List<string?>();

        foreach (CommitInfo commit in commits)
        {
            List<string?> top = new List<string?>(lanes);
            int lane = lanes.IndexOf(commit.Hash);
            if (lane < 0)
            {
                // A branch tip nobody was waiting for starts in the first free lane
                lane = lanes.IndexOf(null);
                if (lane < 0)
                {
                    lane = lanes.Count;
                    lanes.Add(null);
                    top.Add(null);
                }
            }

            // Other lanes waiting for this commit end here: branches that split off from it
            for (int i = 0; i < lanes.Count; ++i)
            {
                if (lanes[i] == commit.Hash)
                    lanes[i] = null;
            }

            // The first parent always continues in this lane, so a branch stays straight until the commit where
            // it split off (where the lanes meet again). Merged parents join a lane already waiting for them, or
            // take a free one.
            List<int> parentLanes = new List<int>();
            for (int p = 0; p < commit.Parents.Count; ++p)
            {
                string parent = commit.Parents[p];
                int existing = lanes.IndexOf(parent);
                if (p == 0)
                {
                    lanes[lane] = parent;
                    parentLanes.Add(lane);
                    continue;
                }

                if (existing >= 0)
                {
                    parentLanes.Add(existing);
                    continue;
                }

                int free = FreeLaneAfter(lanes, lane);
                lanes[free] = parent;
                parentLanes.Add(free);
            }

            while (lanes.Count > 0 && lanes[^1] == null)
                lanes.RemoveAt(lanes.Count - 1);

            commit.Graph = BuildRow(commit, top, lanes, lane, parentLanes);
        }
    }

    public static void Clear(IEnumerable<CommitInfo> commits)
    {
        foreach (CommitInfo commit in commits)
            commit.Graph = null;
    }

    private static int FreeLaneAfter(List<string?> lanes, int start)
    {
        for (int i = start + 1; i < lanes.Count; ++i)
        {
            if (lanes[i] == null)
                return i;
        }

        lanes.Add(null);
        return lanes.Count - 1;
    }

    private static GraphRow BuildRow(CommitInfo commit, List<string?> top, List<string?> bottom, int lane,
                                     List<int> parentLanes)
    {
        List<GraphLine> lines = new List<GraphLine>();
        for (int i = 0; i < top.Count; ++i)
        {
            string? waiting = top[i];
            if (waiting == null)
                continue;

            // Lines arriving at the dot from children above; anything else passes straight through
            if (waiting == commit.Hash)
                lines.Add(new GraphLine(i, 0, lane, 0.5, i));
            else if (i < bottom.Count && bottom[i] == waiting)
                lines.Add(new GraphLine(i, 0, i, 1, i));
        }

        // Lines leaving the dot towards the parents below; a new lane takes the colour of where it goes
        foreach (int parentLane in parentLanes)
            lines.Add(new GraphLine(lane, 0.5, parentLane, 1, parentLane == lane ? lane : parentLane));

        int width = Math.Max(Math.Max(top.Count, bottom.Count), lane + 1);
        return new GraphRow(Math.Min(lane, MAX_LANES - 1), Math.Min(width, MAX_LANES), Clip(lines), commit.IsMerge);
    }

    private static List<GraphLine> Clip(List<GraphLine> lines)
    {
        int last = MAX_LANES - 1;
        return lines.Select(line => line with
        {
            FromLane = Math.Min(line.FromLane, last),
            ToLane = Math.Min(line.ToLane, last)
        }).ToList();
    }
}
