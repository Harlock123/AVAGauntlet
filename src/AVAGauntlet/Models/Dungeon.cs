namespace AVAGauntlet.Models;

/// <summary>A 2D point or vector in tiles (y grows downwards).</summary>
public readonly record struct Vec(double X, double Y)
{
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator *(Vec a, double k) => new(a.X * k, a.Y * k);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public Vec Normal => Length < 1e-9 ? new Vec(0, 0) : this * (1 / Length);
    public override string ToString() => $"({X:0.##}, {Y:0.##})";
}

public static class Field
{
    public const double Step = 1.0 / 60;
}

public enum Cell : byte { Floor, Wall, Door, Exit }

public enum ItemKind { Food, Treasure, Key, Potion }

public enum MonsterKind
{
    /// <summary>Flies at you and hits once.</summary>
    Ghost,
    /// <summary>Clubs you when it gets close.</summary>
    Grunt,
    /// <summary>Keeps its distance and throws fire.</summary>
    Demon,
    /// <summary>Lobs rocks over the walls at you.</summary>
    Lobber,
    /// <summary>Fades in and out of sight; can't be hit while it's gone.</summary>
    Sorcerer,
    /// <summary>Drains your health. Only magic gets rid of it.</summary>
    Death,
}

public sealed record ItemSpot(ItemKind Kind, int X, int Y);
public sealed record GeneratorSpot(MonsterKind Kind, int X, int Y, int Level);

/// <summary>
/// A dungeon level: rooms joined by wide corridors, all one connected network, worked out from the
/// level number so it's the same every time. The start is in the first room and the exit as far from
/// it as can be. Dead-end rooms away from both may be locked behind doors, with treasure inside and a
/// key to be found elsewhere, so the way to the exit never needs a key.
/// </summary>
public sealed class Dungeon
{
    public required int Level { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required Cell[,] Cells { get; init; }
    public (int X, int Y) Start { get; private set; }
    public (int X, int Y) Exit { get; private set; }
    public List<ItemSpot> Items { get; } = [];
    public List<GeneratorSpot> Generators { get; } = [];
    public int LockedRooms { get; private set; }

    public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>Walkable: floor or the exit (a door isn't, until it's opened).</summary>
    public bool Open(int x, int y) => Inside(x, y) && Cells[x, y] is Cell.Floor or Cell.Exit;

    /// <summary>Steps from a square to every other, over open squares (doors shut).</summary>
    public int[,] Distances(int sx, int sy, bool throughDoors = false)
    {
        var d = new int[Width, Height];
        for (int x = 0; x < Width; x++) for (int y = 0; y < Height; y++) d[x, y] = -1;
        var q = new Queue<(int, int)>();
        d[sx, sy] = 0;
        q.Enqueue((sx, sy));
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy;
                if (!Inside(nx, ny) || d[nx, ny] >= 0) continue;
                if (!Open(nx, ny) && !(throughDoors && Cells[nx, ny] == Cell.Door)) continue;
                d[nx, ny] = d[x, y] + 1;
                q.Enqueue((nx, ny));
            }
        }
        return d;
    }

    private sealed record Room(int X, int Y, int W, int H)
    {
        public (int X, int Y) Centre => (X + W / 2, Y + H / 2);
        public bool Contains(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;
    }

    public static Dungeon Build(int level)
    {
        var rng = new Random(level * 7349 + 11);
        int size = 36 + Math.Min(6, level) * 2;
        var cells = new Cell[size, size];
        for (int x = 0; x < size; x++) for (int y = 0; y < size; y++) cells[x, y] = Cell.Wall;
        var d = new Dungeon { Level = level, Width = size, Height = size, Cells = cells };

        // Rooms that don't touch.
        var rooms = new List<Room>();
        int want = 9 + Math.Min(6, level / 2);
        for (int tries = 0; tries < 400 && rooms.Count < want; tries++)
        {
            int w = 4 + rng.Next(5), h = 4 + rng.Next(5);
            int x = 2 + rng.Next(size - w - 4), y = 2 + rng.Next(size - h - 4);
            var r = new Room(x, y, w, h);
            if (rooms.Any(o => x < o.X + o.W + 2 && o.X < x + w + 2 && y < o.Y + o.H + 2 && o.Y < y + h + 2)) continue;
            rooms.Add(r);
        }
        foreach (var r in rooms)
            for (int x = r.X; x < r.X + r.W; x++) for (int y = r.Y; y < r.Y + r.H; y++) cells[x, y] = Cell.Floor;

        // Corridors two wide: each room to the nearest one already joined, and a few more for loops.
        void Carve(int x, int y)
        {
            for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++)
                if (x + dx > 0 && y + dy > 0 && x + dx < size - 1 && y + dy < size - 1) cells[x + dx, y + dy] = Cell.Floor;
        }
        void Join(Room a, Room b)
        {
            var (ax, ay) = a.Centre;
            var (bx, by) = b.Centre;
            // An L: across then down, or down then across.
            if (rng.Next(2) == 0)
            {
                for (int x = Math.Min(ax, bx); x <= Math.Max(ax, bx); x++) Carve(x, ay);
                for (int y = Math.Min(ay, by); y <= Math.Max(ay, by); y++) Carve(bx, y);
            }
            else
            {
                for (int y = Math.Min(ay, by); y <= Math.Max(ay, by); y++) Carve(ax, y);
                for (int x = Math.Min(ax, bx); x <= Math.Max(ax, bx); x++) Carve(x, by);
            }
        }
        for (int i = 1; i < rooms.Count; i++)
        {
            var nearest = rooms.Take(i).OrderBy(o => Math.Abs(o.Centre.X - rooms[i].Centre.X) + Math.Abs(o.Centre.Y - rooms[i].Centre.Y)).First();
            Join(rooms[i], nearest);
        }
        for (int i = 0; i < 2 + level / 3; i++)
        {
            var a = rooms[rng.Next(rooms.Count)];
            var b = rooms[rng.Next(rooms.Count)];
            if (a != b) Join(a, b);
        }

        // The start and the exit.
        d.Start = rooms[0].Centre;
        var dist = d.Distances(d.Start.X, d.Start.Y);
        var far = rooms.Skip(1).OrderByDescending(r => dist[r.Centre.X, r.Centre.Y]).First();
        d.Exit = far.Centre;

        // Dead-end rooms away from the start and the exit: lock them.
        var locked = new List<Room>();
        foreach (var r in rooms.Skip(1))
        {
            if (r == far || locked.Count >= 1 + level / 3) continue;
            var gaps = new List<(int X, int Y)>();
            for (int x = r.X - 1; x <= r.X + r.W; x++)
                for (int y = r.Y - 1; y <= r.Y + r.H; y++)
                {
                    bool edge = x == r.X - 1 || x == r.X + r.W || y == r.Y - 1 || y == r.Y + r.H;
                    bool corner = (x == r.X - 1 || x == r.X + r.W) && (y == r.Y - 1 || y == r.Y + r.H);
                    if (edge && !corner && d.Inside(x, y) && cells[x, y] == Cell.Floor) gaps.Add((x, y));
                }
            // One opening (two squares, side by side) and nothing else.
            if (gaps.Count is < 1 or > 2) continue;
            if (gaps.Count == 2 && Math.Abs(gaps[0].X - gaps[1].X) + Math.Abs(gaps[0].Y - gaps[1].Y) != 1) continue;
            foreach (var (gx, gy) in gaps) cells[gx, gy] = Cell.Door;
            // Still all reachable outside it?
            var check = d.Distances(d.Start.X, d.Start.Y);
            if (check[d.Exit.X, d.Exit.Y] < 0 || rooms.Where(o => o != r && !locked.Contains(o)).Any(o => check[o.Centre.X, o.Centre.Y] < 0))
            {
                foreach (var (gx, gy) in gaps) cells[gx, gy] = Cell.Floor;
                continue;
            }
            locked.Add(r);
        }
        d.LockedRooms = locked.Count;
        cells[d.Exit.X, d.Exit.Y] = Cell.Exit;

        // What's in the rooms.
        var used = new HashSet<(int, int)> { d.Start, d.Exit };
        (int X, int Y)? Spot(Room r)
        {
            for (int t = 0; t < 30; t++)
            {
                int x = r.X + rng.Next(r.W), y = r.Y + rng.Next(r.H);
                if (cells[x, y] == Cell.Floor && used.Add((x, y))) return (x, y);
            }
            return null;
        }
        var open = rooms.Where(r => !locked.Contains(r)).ToList();
        foreach (var r in locked)
            for (int i = 0; i < 3; i++)
                if (Spot(r) is { } s) d.Items.Add(new ItemSpot(i == 2 && rng.Next(2) == 0 ? ItemKind.Potion : ItemKind.Treasure, s.X, s.Y));
        for (int i = 0; i < locked.Count + 1; i++)
            if (Spot(open[1 + rng.Next(open.Count - 1)]) is { } s) d.Items.Add(new ItemSpot(ItemKind.Key, s.X, s.Y));
        foreach (var (kind, count) in new[] { (ItemKind.Food, 3 + rng.Next(3)), (ItemKind.Treasure, 5 + rng.Next(5)), (ItemKind.Potion, 1 + rng.Next(2)) })
            for (int i = 0; i < count; i++)
                if (Spot(open[rng.Next(open.Count)]) is { } s) d.Items.Add(new ItemSpot(kind, s.X, s.Y));

        // Monster generators, more and tougher as the levels go on; none in the first room.
        int gens = Math.Min(11, 2 + level);
        for (int i = 0; i < gens; i++)
        {
            var r = open[1 + rng.Next(open.Count - 1)];
            if (Spot(r) is not { } s) continue;
            double roll = rng.NextDouble();
            var kind = level >= 5 && roll < 0.08 ? MonsterKind.Death
                : level >= 4 && roll < 0.2 ? MonsterKind.Sorcerer
                : level >= 3 && roll < 0.34 ? MonsterKind.Lobber
                : level >= 2 && roll < 0.5 ? MonsterKind.Demon
                : roll < 0.75 ? MonsterKind.Grunt
                : MonsterKind.Ghost;
            int lv = Math.Clamp(1 + rng.Next(Math.Min(3, 1 + level / 2)), 1, 3);
            d.Generators.Add(new GeneratorSpot(kind, s.X, s.Y, lv));
        }
        return d;
    }
}
