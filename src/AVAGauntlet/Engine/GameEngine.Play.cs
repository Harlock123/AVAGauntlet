using AVAGauntlet.Models;

namespace AVAGauntlet.Engine;

public sealed partial class GameEngine
{
    public const double ReadySeconds = 1.5;
    public const double DyingSeconds = 2.5;
    public const double ExitSeconds = 1.6;
    public const int FoodHealth = 100;
    public const int TreasurePoints = 100;
    public const int KeyPoints = 100;
    public const int PotionPoints = 50;
    public const int LowHealth = 200;
    public const int MaxMonsters = 60;
    /// <summary>How far magic reaches, and how near the hero must be for a generator to work.</summary>
    public const double MagicRange = 11;
    public const double SpawnRange = 11;
    /// <summary>A generator stops making monsters while this many of its own are about.</summary>
    public const int PerGenerator = 5;
    public const int MaxHeroShots = 2;

    public int Level { get; private set; } = 1;
    public Dungeon Dungeon { get; private set; } = null!;
    /// <summary>The dungeon's squares as they are now (doors opened).</summary>
    public Cell[,] Cells { get; private set; } = new Cell[1, 1];
    public Hero Hero { get; } = new();
    public List<Monster> Monsters { get; } = [];
    public List<Generator> Generators { get; } = [];
    public List<Item> Items { get; } = [];
    public List<Shot> Shots { get; } = [];
    public string? LastDeath { get; private set; }

    private int[,] _flow = new int[1, 1];
    private double _flowClock, _drain, _fireClock, _hurtSound;
    private bool _magicPrev, _lowWarned, _calm;

    public static double Speed(MonsterKind k) => k switch
    {
        MonsterKind.Ghost => 2.4, MonsterKind.Grunt => 2.2, MonsterKind.Demon => 2.0, MonsterKind.Lobber => 1.9, MonsterKind.Sorcerer => 2.4, _ => 2.7,
    };

    public static int Points(MonsterKind k) => k switch
    {
        MonsterKind.Ghost => 10, MonsterKind.Grunt => 20, MonsterKind.Death => 500, _ => 30,
    };

    // ----- the squares ----------------------------------------------------------------------------

    public int Width => Dungeon.Width;
    public int Height => Dungeon.Height;

    /// <summary>Somewhere a body can be: floor or the exit, and no generator in the way.</summary>
    public bool Walkable(int x, int y) =>
        x >= 0 && y >= 0 && x < Width && y < Height && Cells[x, y] is Cell.Floor or Cell.Exit && !Generators.Any(g => !g.Dead && g.X == x && g.Y == y);

    /// <summary>Shots and sight pass: anything but wall and closed door.</summary>
    public bool SeeThrough(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && Cells[x, y] is Cell.Floor or Cell.Exit;

    /// <summary>A circle of this radius at p clear of walls, doors and generators.</summary>
    private bool Free(Vec p, double r)
    {
        int x0 = (int)Math.Floor(p.X - r), x1 = (int)Math.Floor(p.X + r), y0 = (int)Math.Floor(p.Y - r), y1 = (int)Math.Floor(p.Y + r);
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                if (!Walkable(x, y)) return false;
        return true;
    }

    /// <summary>A straight line with nothing solid across it.</summary>
    public bool InSight(Vec a, Vec b)
    {
        var d = b - a;
        int steps = (int)Math.Ceiling(d.Length / 0.25);
        for (int i = 1; i < steps; i++)
        {
            var p = a + d * ((double)i / steps);
            if (!SeeThrough((int)Math.Floor(p.X), (int)Math.Floor(p.Y))) return false;
        }
        return true;
    }

    // ----- levels ---------------------------------------------------------------------------------

    private void StartLevel(int level)
    {
        Level = level;
        Dungeon = Dungeon.Build(level);
        Cells = (Cell[,])Dungeon.Cells.Clone();
        Monsters.Clear();
        Shots.Clear();
        Generators.Clear();
        Items.Clear();
        if (!_calm)
        {
            foreach (var g in Dungeon.Generators) Generators.Add(new Generator { Kind = g.Kind, X = g.X, Y = g.Y, Level = g.Level, Clock = 1 + _rng.NextDouble() * 2 });
            foreach (var i in Dungeon.Items) Items.Add(new Item { Kind = i.Kind, X = i.X, Y = i.Y });
        }
        Hero.Pos = new Vec(Dungeon.Start.X + 0.5, Dungeon.Start.Y + 0.5);
        Hero.Facing = new Vec(0, 1);
        UpdateFlow();
        SetPhase(Phase.Ready);
        Music(false);
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.Ready:
                if (PhaseTime < ReadySeconds) return;
                SetPhase(Phase.Playing);
                Music(true);
                return;
            case Phase.Dying:
                if (PhaseTime >= DyingSeconds) GameOver();
                return;
            case Phase.Exiting:
                if (PhaseTime < ExitSeconds) return;
                StartLevel(Level + 1);
                Say($"LEVEL {Level}");
                return;
        }

        // Health runs down all the time.
        _drain += dt;
        while (_drain >= 1) { _drain -= 1; Hero.Health--; }
        if (_hurtSound > 0) _hurtSound -= dt;
        MoveHero(dt, c);
        if (Phase != Phase.Playing) return;
        Magic(c);
        MoveShots(dt);
        RunGenerators(dt);
        _flowClock -= dt;
        if (_flowClock <= 0) { _flowClock = 0.25; UpdateFlow(); }
        MoveMonsters(dt);
        if (Hero.Health < LowHealth && !_lowWarned)
        {
            _lowWarned = true;
            Say($"{Hero.Stats.Name} NEEDS FOOD, BADLY!", 4);
            Play(Sfx.LowHealth);
        }
        if (Hero.Health > LowHealth + 100) _lowWarned = false;
        if (Hero.Health <= 0) Die();
    }

    private void Die()
    {
        Hero.Health = 0;
        LastDeath = $"level {Level}";
        Effects.Add(new Effect { Kind = EffectKind.Blast, Pos = Hero.Pos, Life = DyingSeconds });
        Music(false);
        Play(Sfx.Dead);
        Say($"{Hero.Stats.Name} HAS FALLEN", 5);
        SetPhase(Phase.Dying);
    }

    private void Hurt(int amount)
    {
        int taken = Math.Max(1, (int)Math.Round(amount * (1 - Hero.Stats.Armour)));
        Hero.Health -= taken;
        if (_hurtSound <= 0) { Play(Sfx.Hurt); _hurtSound = 0.25; }
    }

    // ----- the hero -------------------------------------------------------------------------------

    private void MoveHero(double dt, Controls c)
    {
        var h = Hero;
        var stick = new Vec(Math.Sign(c.Dx), Math.Sign(c.Dy));
        if (stick.Length > 0) h.Facing = stick.Normal;
        _fireClock -= dt;
        if (c.Fire)
        {
            // Standing still and shooting the way it faces.
            if (_fireClock <= 0 && Shots.Count(s => !s.Hostile) < MaxHeroShots)
            {
                _fireClock = h.Stats.FireEvery;
                Shots.Add(new Shot { Pos = h.Pos + h.Facing * 0.4, Vel = h.Facing * h.Stats.ShotSpeed, Damage = h.Stats.ShotDamage });
                Play(Sfx.Shoot);
            }
        }
        else if (stick.Length > 0)
        {
            var step = h.Facing * (h.Stats.Speed * dt);
            var nx = h.Pos with { X = h.Pos.X + step.X };
            if (Free(nx, Hero.Radius)) h.Pos = nx;
            else TryDoor(nx);
            var ny = h.Pos with { Y = h.Pos.Y + step.Y };
            if (Free(ny, Hero.Radius)) h.Pos = ny;
            else TryDoor(ny);
            h.Walk += dt;
        }
        // What's underfoot.
        foreach (var item in Items)
        {
            if (item.Taken || (item.Centre - h.Pos).Length > 0.6) continue;
            item.Taken = true;
            switch (item.Kind)
            {
                case ItemKind.Food:
                    h.Health += FoodHealth;
                    Popup(item.Centre, $"+{FoodHealth}");
                    Play(Sfx.Food);
                    break;
                case ItemKind.Treasure:
                    AddScore(TreasurePoints);
                    Popup(item.Centre, TreasurePoints.ToString());
                    Play(Sfx.Treasure);
                    break;
                case ItemKind.Key:
                    h.Keys++;
                    AddScore(KeyPoints);
                    Play(Sfx.Key);
                    break;
                default:
                    h.Potions++;
                    AddScore(PotionPoints);
                    Play(Sfx.Potion);
                    break;
            }
        }
        Items.RemoveAll(i => i.Taken);
        int tx = (int)Math.Floor(h.Pos.X), ty = (int)Math.Floor(h.Pos.Y);
        if (Cells[tx, ty] == Cell.Exit)
        {
            Play(Sfx.Exit);
            Music(false);
            SetPhase(Phase.Exiting);
        }
    }

    /// <summary>Walking into a door with a key: the key's used and the whole door opens.</summary>
    private void TryDoor(Vec p)
    {
        if (Hero.Keys <= 0) return;
        int x0 = (int)Math.Floor(p.X - Hero.Radius), x1 = (int)Math.Floor(p.X + Hero.Radius), y0 = (int)Math.Floor(p.Y - Hero.Radius), y1 = (int)Math.Floor(p.Y + Hero.Radius);
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height || Cells[x, y] != Cell.Door) continue;
                Hero.Keys--;
                var q = new Queue<(int, int)>();
                q.Enqueue((x, y));
                Cells[x, y] = Cell.Floor;
                while (q.Count > 0)
                {
                    var (cx, cy) = q.Dequeue();
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= Width || ny >= Height || Cells[nx, ny] != Cell.Door) continue;
                        Cells[nx, ny] = Cell.Floor;
                        q.Enqueue((nx, ny));
                    }
                }
                Play(Sfx.Door);
                UpdateFlow();
                return;
            }
    }

    /// <summary>A potion: everything close by is destroyed, and generators near are knocked down.</summary>
    private void Magic(Controls c)
    {
        bool pressed = c.Magic && !_magicPrev;
        _magicPrev = c.Magic;
        if (!pressed || Hero.Potions <= 0) return;
        Hero.Potions--;
        Play(Sfx.Magic);
        Effects.Add(new Effect { Kind = EffectKind.Magic, Pos = Hero.Pos, Life = 0.8 });
        foreach (var m in Monsters.Where(m => !m.Dead && (m.Pos - Hero.Pos).Length <= MagicRange)) Kill(m);
        foreach (var g in Generators.Where(g => !g.Dead && (g.Centre - Hero.Pos).Length <= MagicRange))
        {
            g.Level -= Hero.Stats.Magic;
            if (g.Dead) GeneratorDown(g);
        }
        Monsters.RemoveAll(m => m.Dead);
    }

    // ----- shots ----------------------------------------------------------------------------------

    private void MoveShots(double dt)
    {
        foreach (var s in Shots)
        {
            s.Age += dt;
            if (s.Lobbed)
            {
                // A rock over the walls: down where it was aimed.
                var to = s.Target - s.Pos;
                double stepLen = s.Vel.Length * dt;
                if (to.Length <= stepLen)
                {
                    s.Dead = true;
                    Effects.Add(new Effect { Kind = EffectKind.Sparkle, Pos = s.Target, Life = 0.3 });
                    if ((Hero.Pos - s.Target).Length < 0.75) Hurt(s.Damage);
                }
                else s.Pos += to.Normal * stepLen;
                continue;
            }
            s.Pos += s.Vel * dt;
            int tx = (int)Math.Floor(s.Pos.X), ty = (int)Math.Floor(s.Pos.Y);
            if (!SeeThrough(tx, ty) || s.Age > 2.5) { s.Dead = true; continue; }
            if (s.Hostile)
            {
                if ((Hero.Pos - s.Pos).Length < 0.5) { s.Dead = true; Hurt(s.Damage); }
                continue;
            }
            // The hero's shot: monsters, generators, and (careless!) the food.
            var gen = Generators.FirstOrDefault(g => !g.Dead && g.X == tx && g.Y == ty);
            if (gen is not null)
            {
                s.Dead = true;
                gen.Level--;
                AddScore(10);
                Play(Sfx.GeneratorHit);
                if (gen.Dead) GeneratorDown(gen);
                continue;
            }
            foreach (var m in Monsters)
            {
                if (m.Dead || m.Hidden || (m.Pos - s.Pos).Length > Monster.Radius + 0.15) continue;
                s.Dead = true;
                if (m.Kind == MonsterKind.Death) break;
                m.Health -= s.Damage;
                if (m.Health <= 0) Kill(m);
                else Play(Sfx.Hit);
                break;
            }
            if (s.Dead) continue;
            var food = Items.FirstOrDefault(i => i.Kind == ItemKind.Food && !i.Taken && (i.Centre - s.Pos).Length < 0.4);
            if (food is not null)
            {
                food.Taken = true;
                s.Dead = true;
                Say($"{Hero.Stats.Name} SHOT THE FOOD!");
                Play(Sfx.ShotFood);
            }
        }
        Shots.RemoveAll(s => s.Dead);
        Items.RemoveAll(i => i.Taken);
        Monsters.RemoveAll(m => m.Dead);
    }

    private void Kill(Monster m)
    {
        if (m.Dead) return;
        m.Dead = true;
        AddScore(Points(m.Kind));
        Effects.Add(new Effect { Kind = EffectKind.Blast, Pos = m.Pos, Life = 0.4 });
        Play(Sfx.Kill);
    }

    private void GeneratorDown(Generator g)
    {
        g.Level = 0;
        AddScore(50);
        Popup(g.Centre, "50");
        Effects.Add(new Effect { Kind = EffectKind.Blast, Pos = g.Centre, Life = 0.6 });
        Play(Sfx.GeneratorDown);
        UpdateFlow();
    }

    // ----- generators and monsters ----------------------------------------------------------------

    private void RunGenerators(double dt)
    {
        foreach (var g in Generators)
        {
            if (g.Dead) continue;
            g.Clock -= dt;
            if (g.Clock > 0) continue;
            g.Clock = Math.Max(1.6, 3.2 - 0.1 * Level) * (0.8 + _rng.NextDouble() * 0.4);
            // Death comes one at a time, and not often.
            if (g.Kind == MonsterKind.Death) g.Clock *= 3;
            int cap = g.Kind == MonsterKind.Death ? 1 : PerGenerator;
            if (Monsters.Count >= MaxMonsters || (g.Centre - Hero.Pos).Length > SpawnRange || Monsters.Count(m => m.From == g) >= cap) continue;
            // Out into a clear square next to it.
            var free = new List<Vec>();
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var p = new Vec(g.X + dx + 0.5, g.Y + dy + 0.5);
                    if ((dx != 0 || dy != 0) && Walkable(g.X + dx, g.Y + dy) && !Monsters.Any(m => (m.Pos - p).Length < 0.7) && (p - Hero.Pos).Length > 1) free.Add(p);
                }
            if (free.Count == 0) continue;
            int health = g.Kind switch { MonsterKind.Ghost => 1, MonsterKind.Death => 9999, _ => g.Level };
            Monsters.Add(new Monster { Kind = g.Kind, Pos = free[_rng.Next(free.Count)], Health = health, Strength = g.Level, Reload = 0.5 + _rng.NextDouble(), From = g });
        }
    }

    /// <summary>Steps to the hero from every square: the monsters follow it downhill.</summary>
    private void UpdateFlow()
    {
        int w = Width, ht = Height;
        if (_flow.GetLength(0) != w || _flow.GetLength(1) != ht) _flow = new int[w, ht];
        for (int x = 0; x < w; x++) for (int y = 0; y < ht; y++) _flow[x, y] = -1;
        int hx = (int)Math.Floor(Hero.Pos.X), hy = (int)Math.Floor(Hero.Pos.Y);
        var q = new Queue<(int, int)>();
        _flow[hx, hy] = 0;
        q.Enqueue((hx, hy));
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= ht || _flow[nx, ny] >= 0 || !Walkable(nx, ny)) continue;
                _flow[nx, ny] = _flow[x, y] + 1;
                q.Enqueue((nx, ny));
            }
        }
    }

    public int FlowAt(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height ? _flow[x, y] : -1;

    /// <summary>Which way to step to get nearer the hero (or further away, for those that keep their distance).</summary>
    private Vec Downhill(Monster m, bool away)
    {
        int tx = (int)Math.Floor(m.Pos.X), ty = (int)Math.Floor(m.Pos.Y);
        int here = FlowAt(tx, ty);
        if (here < 0) return new Vec(0, 0);
        Vec best = new(0, 0);
        int bestFlow = here;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int f = FlowAt(tx + dx, ty + dy);
                if (f < 0) continue;
                // Diagonals only where both sides are clear.
                if (dx != 0 && dy != 0 && (!Walkable(tx + dx, ty) || !Walkable(tx, ty + dy))) continue;
                if (away ? f > bestFlow : f < bestFlow)
                {
                    bestFlow = f;
                    best = new Vec(tx + dx + 0.5, ty + dy + 0.5) - m.Pos;
                }
            }
        // Already in the hero's square: straight at the hero.
        if (here == 0 && !away) best = Hero.Pos - m.Pos;
        if (best.Length < 1e-6 && !away) best = new Vec(tx + 0.5, ty + 0.5) - m.Pos;
        return best.Normal;
    }

    private void MoveMonsters(double dt)
    {
        var h = Hero;
        foreach (var m in Monsters)
        {
            if (m.Dead) continue;
            m.Age += dt;
            m.Reload -= dt;
            var to = h.Pos - m.Pos;
            double dist = to.Length;
            bool touching = dist < Monster.Radius + Hero.Radius + 0.05;
            Vec dir;
            switch (m.Kind)
            {
                case MonsterKind.Ghost:
                    if (touching) { Hurt(10); m.Dead = true; continue; }
                    dir = Downhill(m, false);
                    break;
                case MonsterKind.Demon when dist < 5.5 && dist > 1.4 && InSight(m.Pos, h.Pos):
                    // Keeps its distance and throws fire.
                    dir = new Vec(0, 0);
                    if (m.Reload <= 0)
                    {
                        m.Reload = 1.8;
                        Shots.Add(new Shot { Pos = m.Pos, Vel = to.Normal * 6, Hostile = true, Damage = 7 + m.Strength });
                    }
                    break;
                case MonsterKind.Lobber when dist < 6.5:
                    // Throws rocks over the walls, and backs off if the hero gets close.
                    dir = dist < 3 ? Downhill(m, true) : new Vec(0, 0);
                    if (m.Reload <= 0)
                    {
                        m.Reload = 2.2;
                        Shots.Add(new Shot { Pos = m.Pos, Vel = new Vec(4.5, 0), Hostile = true, Lobbed = true, Target = h.Pos, Damage = 5 + m.Strength });
                    }
                    break;
                case MonsterKind.Death:
                    if (touching)
                    {
                        dir = new Vec(0, 0);
                        if (m.Reload <= 0)
                        {
                            m.Reload = 0.1;
                            h.Health -= 10;
                            m.Drained += 10;
                            if (_hurtSound <= 0) { Play(Sfx.DeathTouch); _hurtSound = 0.4; }
                            // Death takes its fill and goes.
                            if (m.Drained >= 200) { m.Dead = true; Effects.Add(new Effect { Kind = EffectKind.Sparkle, Pos = m.Pos, Life = 0.6 }); }
                        }
                    }
                    else dir = Downhill(m, false);
                    break;
                default:
                    if (m.Kind == MonsterKind.Sorcerer) m.Hidden = m.Age % 3 < 1;
                    if (touching)
                    {
                        dir = new Vec(0, 0);
                        if (m.Reload <= 0)
                        {
                            m.Reload = 0.7;
                            Hurt((m.Kind == MonsterKind.Sorcerer ? 6 : 3) + 2 * m.Strength);
                        }
                    }
                    else dir = Downhill(m, false);
                    break;
            }
            if (dir.Length > 0)
            {
                m.Facing = dir;
                var step = dir * (Speed(m.Kind) * (1 + 0.04 * Math.Min(10, Level - 1)) * dt);
                var nx = m.Pos with { X = m.Pos.X + step.X };
                if (Free(nx, Monster.Radius) && (nx - h.Pos).Length >= Monster.Radius + Hero.Radius) m.Pos = nx;
                var ny = m.Pos with { Y = m.Pos.Y + step.Y };
                if (Free(ny, Monster.Radius) && (ny - h.Pos).Length >= Monster.Radius + Hero.Radius) m.Pos = ny;
            }
        }
        // They jostle rather than pile up.
        for (int i = 0; i < Monsters.Count; i++)
            for (int j = i + 1; j < Monsters.Count; j++)
            {
                var a = Monsters[i];
                var b = Monsters[j];
                var d = b.Pos - a.Pos;
                double len = d.Length;
                if (len >= Monster.Radius * 2 || len < 1e-6) continue;
                var push = d.Normal * ((Monster.Radius * 2 - len) * 0.5);
                if (Free(a.Pos - push, Monster.Radius)) a.Pos -= push;
                if (Free(b.Pos + push, Monster.Radius)) b.Pos += push;
            }
        Monsters.RemoveAll(m => m.Dead);
    }

    // ----- for the tests and screenshots ----------------------------------------------------------

    internal void Idle(int steps)
    {
        for (int i = 0; i < steps; i++) Tick(default);
    }

    internal void Go()
    {
        if (Phase != Phase.Ready) return;
        SetPhase(Phase.Playing);
        Music(true);
    }

    internal void JumpTo(int level)
    {
        StartLevel(level);
        Go();
    }

    /// <summary>No generators, monsters or things lying about, unless put there.</summary>
    internal void Calm()
    {
        _calm = true;
        Generators.Clear();
        Monsters.Clear();
        Items.Clear();
        UpdateFlow();
    }

    internal void PutHero(Vec at)
    {
        Hero.Pos = at;
        UpdateFlow();
    }

    internal Monster PutMonster(MonsterKind kind, Vec at, int strength = 1)
    {
        var m = new Monster { Kind = kind, Pos = at, Health = kind == MonsterKind.Ghost ? 1 : kind == MonsterKind.Death ? 9999 : strength, Strength = strength };
        Monsters.Add(m);
        return m;
    }

    internal Generator PutGenerator(MonsterKind kind, int x, int y, int level = 3)
    {
        var g = new Generator { Kind = kind, X = x, Y = y, Level = level, Clock = 0.1 };
        Generators.Add(g);
        UpdateFlow();
        return g;
    }

    internal Item PutItem(ItemKind kind, int x, int y)
    {
        var i = new Item { Kind = kind, X = x, Y = y };
        Items.Add(i);
        return i;
    }

    internal void SetCell(int x, int y, Cell c)
    {
        Cells[x, y] = c;
        UpdateFlow();
    }
}
