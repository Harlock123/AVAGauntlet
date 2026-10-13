using AVAGauntlet.Models;

namespace AVAGauntlet.Engine;

/// <summary>
/// The computer player for attract mode and the tests. Anything in sight and close it stands and shoots
/// at; a crowd, or Death coming near, gets a potion. Otherwise it goes the shortest way to whatever
/// it wants most: food when it's getting weak, then the generators (to shoot them down), anything
/// worth having lying close by, and at last the exit.
/// </summary>
public sealed class AttractPilot(int seed)
{
    private readonly Random _rng = new(seed);
    private bool _magic;
    private Vec _lastPos;
    private double _stuck, _wanderTime;
    private (int, int) _wander = (1, 0);
    private int _lastScore;
    private double _firing, _holdFire;

    public Controls Decide(GameEngine g)
    {
        if (g.Phase != Phase.Playing) return default;
        var h = g.Hero;
        _stuck = (h.Pos - _lastPos).Length < 0.01 ? _stuck + Field.Step : 0;
        _lastPos = h.Pos;

        // Magic: a crowd close by, or Death.
        int near = g.Monsters.Count(m => (m.Pos - h.Pos).Length < 4);
        bool deathNear = g.Monsters.Any(m => m.Kind == MonsterKind.Death && (m.Pos - h.Pos).Length < 4);
        bool magic = h.Potions > 0 && (near >= 6 || deathNear);
        _magic = magic && !_magic;
        if (_magic) return new Controls(0, 0, Magic: true);

        // Something to shoot at. Shots only go eight ways, so only what's close or lined up: a monster
        // right on top of us first, then a generator in line (else it never stops making more), then a
        // monster in line.
        // A shot the nearest of the eight ways would pass within reach of it.
        bool Lined(Vec d)
        {
            var (dx, dy) = Eight(d);
            var q = new Vec(dx, dy).Normal;
            double along = q.X * d.X + q.Y * d.Y;
            // ...and the shot's own path is clear (it can clip a corner the straight line misses).
            return q.Length > 0 && along > 0 && Math.Abs(q.X * d.Y - q.Y * d.X) < 0.42 && g.InSight(h.Pos, h.Pos + q * along);
        }
        var visible = g.Monsters.Where(m => !m.Hidden && m.Kind != MonsterKind.Death && (m.Pos - h.Pos).Length < 7 && g.InSight(h.Pos, m.Pos))
            .OrderBy(m => (m.Pos - h.Pos).Length).ToList();
        var gen = g.Generators.Where(x => !x.Dead && (x.Centre - h.Pos).Length < 7 && Lined(x.Centre - h.Pos) && g.InSight(h.Pos, x.Centre))
            .OrderBy(x => (x.Centre - h.Pos).Length).FirstOrDefault();
        Vec? mark = visible.FirstOrDefault(m => (m.Pos - h.Pos).Length < 2.2)?.Pos
            ?? gen?.Centre
            ?? visible.FirstOrDefault(m => Lined(m.Pos - h.Pos))?.Pos;
        // Shooting a while and hitting nothing: stop and move on for a moment.
        if (g.Score != _lastScore) { _lastScore = g.Score; _firing = 0; }
        if (_holdFire > 0) { _holdFire -= Field.Step; mark = null; }
        if (mark is not null && _firing > 3) { _holdFire = 1.5; _firing = 0; mark = null; }
        if (mark is { } at && !(deathNear && h.Potions == 0))
        {
            _firing += Field.Step;
            var (dx, dy) = Eight(at - h.Pos);
            return new Controls(dx, dy, Fire: true);
        }

        // Death and nothing to stop it with: run.
        var death = g.Monsters.FirstOrDefault(m => m.Kind == MonsterKind.Death && (m.Pos - h.Pos).Length < 5);
        if (death is not null && h.Potions == 0)
        {
            var away = h.Pos - death.Pos;
            var (ax, ay) = Eight(away);
            var probe = h.Pos + new Vec(ax, ay).Normal * 0.6;
            if (g.Walkable((int)Math.Floor(probe.X), (int)Math.Floor(probe.Y))) return new Controls(ax, ay);
            var side = Eight(new Vec(-away.Y, away.X));
            return new Controls(side.Item1, side.Item2);
        }

        if (_stuck > 1.0 || _wanderTime > 0)
        {
            if (_wanderTime <= 0) { _wanderTime = 0.5; _wander = (_rng.Next(3) - 1, _rng.Next(3) - 1); _stuck = 0; }
            _wanderTime -= Field.Step;
            return new Controls(_wander.Item1, _wander.Item2);
        }

        // Somewhere to go.
        var step = Route(g);
        if (step is { } s)
        {
            var (dx, dy) = Eight(s - h.Pos, 0.12);
            return new Controls(dx, dy);
        }
        return default;
    }

    /// <summary>A direction as one of the eight the stick can give.</summary>
    private static (int, int) Eight(Vec d, double dead = 0.38)
    {
        var n = d.Normal;
        return (Math.Abs(n.X) > dead ? Math.Sign(n.X) : 0, Math.Abs(n.Y) > dead ? Math.Sign(n.Y) : 0);
    }

    /// <summary>The middle of the next square on the shortest way to the best goal, or null.</summary>
    private Vec? Route(GameEngine g)
    {
        var h = g.Hero;
        int sx = (int)Math.Floor(h.Pos.X), sy = (int)Math.Floor(h.Pos.Y);
        int w = g.Width, ht = g.Height;
        var dist = new int[w, ht];
        var from = new (int, int)[w, ht];
        for (int x = 0; x < w; x++) for (int y = 0; y < ht; y++) dist[x, y] = -1;
        var q = new Queue<(int X, int Y)>();
        dist[sx, sy] = 0;
        q.Enqueue((sx, sy));
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= ht || dist[nx, ny] >= 0 || !g.Walkable(nx, ny)) continue;
                dist[nx, ny] = dist[x, y] + 1;
                from[nx, ny] = (x, y);
                q.Enqueue((nx, ny));
            }
        }
        int D(int x, int y) => x >= 0 && y >= 0 && x < w && y < ht ? dist[x, y] : -1;

        // The goals, best first.
        (int X, int Y)? goal = null;
        if (h.Health < 500)
            goal = g.Items.Where(i => i.Kind == ItemKind.Food && D(i.X, i.Y) >= 0).OrderBy(i => D(i.X, i.Y)).Select(i => ((int, int)?)(i.X, i.Y)).FirstOrDefault();
        goal ??= g.Items.Where(i => D(i.X, i.Y) is >= 0 and < 12 && (i.Kind != ItemKind.Food || h.Health < 1400))
            .OrderBy(i => D(i.X, i.Y)).Select(i => ((int, int)?)(i.X, i.Y)).FirstOrDefault();
        if (goal is null)
        {
            // Next to a generator, to shoot it.
            int best = int.MaxValue;
            foreach (var gen in g.Generators.Where(x => !x.Dead))
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (2, 0), (-2, 0), (0, 2), (0, -2) })
                {
                    int d = D(gen.X + dx, gen.Y + dy);
                    if (d >= 0 && d < best) { best = d; goal = (gen.X + dx, gen.Y + dy); }
                }
        }
        goal ??= g.Dungeon.Exit;
        var (tx, ty) = goal.Value;
        if (D(tx, ty) <= 0) return D(tx, ty) == 0 ? new Vec(tx + 0.5, ty + 0.5) : null;
        // Back along the way to the first step.
        var at = (tx, ty);
        while (dist[at.Item1, at.Item2] > 1) at = from[at.Item1, at.Item2];
        return new Vec(at.Item1 + 0.5, at.Item2 + 0.5);
    }
}
