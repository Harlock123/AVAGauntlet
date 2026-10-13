using AVAGauntlet.Engine;
using AVAGauntlet.Models;

namespace AVAGauntlet.Tests;

public sealed class GameplayTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gauntlet-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    /// <summary>
    /// A level under way with nothing in it, and a clear arena 15 squares across carved round the
    /// start; returns the arena's middle square.
    /// </summary>
    private (GameEngine G, int X, int Y) Arena(HeroClass hero = HeroClass.Warrior, int seed = 1)
    {
        var g = new GameEngine(seed, new HighScoreTable(_dir));
        g.StartGame(hero);
        g.JumpTo(1);
        g.Calm();
        int cx = Math.Clamp(g.Dungeon.Start.X, 9, g.Width - 10), cy = Math.Clamp(g.Dungeon.Start.Y, 9, g.Height - 10);
        for (int x = cx - 7; x <= cx + 7; x++)
            for (int y = cy - 7; y <= cy + 7; y++) g.SetCell(x, y, Cell.Floor);
        g.PutHero(C(cx, cy));
        return (g, cx, cy);
    }

    private static Vec C(int x, int y) => new(x + 0.5, y + 0.5);

    private static void Steps(GameEngine g, int n, Controls c = default)
    {
        for (int i = 0; i < n; i++) g.Tick(c);
    }

    [Fact]
    public void Every_level_can_be_got_through_and_its_locked_rooms_opened()
    {
        for (int level = 1; level <= 12; level++)
        {
            var d = Dungeon.Build(level);
            var dist = d.Distances(d.Start.X, d.Start.Y);
            Assert.True(dist[d.Exit.X, d.Exit.Y] > 10, $"level {level}: the exit is reachable, and not next door");
            Assert.Equal(Cell.Exit, d.Cells[d.Exit.X, d.Exit.Y]);
            // Keys lie where they can be reached without one, and there's one for every locked room.
            var keys = d.Items.Where(i => i.Kind == ItemKind.Key).ToList();
            Assert.True(keys.Count >= d.LockedRooms, $"level {level}");
            Assert.All(keys, k => Assert.True(dist[k.X, k.Y] >= 0, $"level {level}: key at {k.X},{k.Y}"));
            // With the doors open, everything can be reached.
            var all = d.Distances(d.Start.X, d.Start.Y, throughDoors: true);
            Assert.All(d.Items, i => Assert.True(all[i.X, i.Y] >= 0, $"level {level}: {i.Kind} at {i.X},{i.Y}"));
            Assert.All(d.Generators, gen => Assert.True(all[gen.X, gen.Y] >= 0));
            Assert.True(d.Generators.Count >= 2);
            Assert.Contains(d.Items, i => i.Kind == ItemKind.Food);
        }
        Assert.Contains(Enumerable.Range(1, 12), l => Dungeon.Build(l).LockedRooms > 0);
    }

    [Fact]
    public void Each_hero_has_its_own_strengths()
    {
        Assert.True(ClassStats.Of(HeroClass.Elf).Speed > ClassStats.Of(HeroClass.Warrior).Speed);
        Assert.True(ClassStats.Of(HeroClass.Warrior).ShotDamage > ClassStats.Of(HeroClass.Elf).ShotDamage);
        Assert.True(ClassStats.Of(HeroClass.Valkyrie).Armour > ClassStats.Of(HeroClass.Wizard).Armour);
        Assert.True(ClassStats.Of(HeroClass.Wizard).Magic > ClassStats.Of(HeroClass.Warrior).Magic);
    }

    [Fact]
    public void Health_runs_down_and_food_puts_it_back()
    {
        var (g, x, y) = Arena();
        int h = g.Hero.Health;
        Steps(g, 60 * 3);
        Assert.Equal(h - 3, g.Hero.Health);
        g.PutItem(ItemKind.Food, x + 2, y);
        Steps(g, 60, new Controls(1, 0));
        Assert.Equal(h - 4 + GameEngine.FoodHealth, g.Hero.Health);
    }

    [Fact]
    public void Shooting_the_food_destroys_it()
    {
        var (g, x, y) = Arena(HeroClass.Elf);
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.PutItem(ItemKind.Food, x + 4, y);
        Steps(g, 30, new Controls(1, 0, Fire: true));
        Assert.Empty(g.Items);
        Assert.Equal("ELF SHOT THE FOOD!", g.Message);
        Assert.Contains(Sfx.ShotFood, heard);
    }

    [Fact]
    public void Walls_stop_the_hero_and_its_shots()
    {
        var (g, x, y) = Arena();
        g.SetCell(x + 2, y, Cell.Wall);
        Steps(g, 60, new Controls(1, 0));
        Assert.True(g.Hero.Pos.X < x + 2 - Hero.Radius + 0.01);
        var m = g.PutMonster(MonsterKind.Grunt, C(x + 4, y), strength: 3);
        m.Reload = 999;
        Steps(g, 30, new Controls(1, 0, Fire: true));
        Assert.Equal(3, m.Health);
    }

    [Fact]
    public void Shots_kill_monsters_by_the_hero_s_strength()
    {
        var (g, x, y) = Arena(HeroClass.Warrior);
        var grunt = g.PutMonster(MonsterKind.Grunt, C(x + 5, y), strength: 3);
        grunt.Reload = 999;
        for (int i = 0; i < 30 && !grunt.Dead; i++) { grunt.Pos = C(x + 5, y); g.Tick(new Controls(1, 0, Fire: true)); }
        Assert.True(grunt.Dead); // one warrior's shot (3) for a strength-3 grunt
        Assert.Equal(GameEngine.Points(MonsterKind.Grunt), g.Score);

        (g, x, y) = Arena(HeroClass.Elf);
        grunt = g.PutMonster(MonsterKind.Grunt, C(x + 5, y), strength: 3);
        grunt.Reload = 999;
        int shots = 0;
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        for (int i = 0; i < 120 && !grunt.Dead; i++) { grunt.Pos = C(x + 5, y); g.Tick(new Controls(1, 0, Fire: true)); }
        shots = heard.Count(s => s == Sfx.Shoot);
        Assert.True(grunt.Dead);
        Assert.True(shots >= 3, "an elf's shots are weaker");
    }

    [Fact]
    public void A_ghost_hits_once_and_vanishes_and_armour_takes_some_of_it()
    {
        var (g, x, y) = Arena(HeroClass.Wizard);
        int h = g.Hero.Health;
        var ghost = g.PutMonster(MonsterKind.Ghost, C(x + 3, y));
        for (int i = 0; i < 120 && !ghost.Dead; i++) g.Tick(default);
        Assert.True(ghost.Dead);
        Assert.InRange(h - g.Hero.Health, 10, 13); // the wizard has no armour

        (g, x, y) = Arena(HeroClass.Valkyrie);
        h = g.Hero.Health;
        ghost = g.PutMonster(MonsterKind.Ghost, C(x + 3, y));
        for (int i = 0; i < 120 && !ghost.Dead; i++) g.Tick(default);
        Assert.InRange(h - g.Hero.Health, 5, 9); // the valkyrie's armour takes nearly half
    }

    [Fact]
    public void Monsters_find_their_way_round_walls_to_the_hero()
    {
        var (g, x, y) = Arena();
        // A wall between them with a gap at one end.
        for (int yy = y - 6; yy <= y + 4; yy++) g.SetCell(x + 2, yy, Cell.Wall);
        var grunt = g.PutMonster(MonsterKind.Grunt, C(x + 5, y));
        for (int i = 0; i < 60 * 12 && (grunt.Pos - g.Hero.Pos).Length > 1.2; i++) { g.Tick(default); g.Hero.Health = 1000; }
        Assert.True((grunt.Pos - g.Hero.Pos).Length <= 1.2, $"grunt at {grunt.Pos}, hero at {g.Hero.Pos}");
    }

    [Fact]
    public void Grunts_club_demons_throw_fire_and_lobbers_throw_rocks_over_walls()
    {
        var (g, x, y) = Arena();
        int h = g.Hero.Health;
        g.PutMonster(MonsterKind.Grunt, C(x + 1, y), strength: 2);
        Steps(g, 60 * 2);
        Assert.True(h - g.Hero.Health > 10);

        (g, x, y) = Arena();
        var demon = g.PutMonster(MonsterKind.Demon, C(x + 4, y));
        demon.Reload = 0;
        g.Tick(default);
        Assert.Contains(g.Shots, s => s.Hostile && !s.Lobbed);

        (g, x, y) = Arena();
        for (int yy = y - 7; yy <= y + 7; yy++) g.SetCell(x + 2, yy, Cell.Wall);
        var lobber = g.PutMonster(MonsterKind.Lobber, C(x + 5, y));
        lobber.Reload = 0;
        h = g.Hero.Health;
        Steps(g, 90);
        Assert.True(h - g.Hero.Health > 2 + 1, "a rock came over the wall");
    }

    [Fact]
    public void Death_drains_health_and_only_magic_gets_rid_of_it()
    {
        var (g, x, y) = Arena(HeroClass.Warrior);
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        var death = g.PutMonster(MonsterKind.Death, C(x + 5, y));
        // Shots don't touch it.
        Steps(g, 40, new Controls(1, 0, Fire: true));
        Assert.False(death.Dead);
        int h = g.Hero.Health;
        for (int i = 0; i < 60 * 4 && !death.Dead; i++) g.Tick(default);
        Assert.True(death.Dead, "it took its fill and went");
        Assert.InRange(h - g.Hero.Health, 200, 215);
        Assert.Contains(Sfx.DeathTouch, heard);

        (g, x, y) = Arena();
        death = g.PutMonster(MonsterKind.Death, C(x + 4, y));
        g.Hero.Potions = 1;
        g.Tick(new Controls(0, 0, Magic: true));
        Assert.True(death.Dead || !g.Monsters.Contains(death));
        Assert.Equal(GameEngine.Points(MonsterKind.Death), g.Score);
    }

    [Fact]
    public void Generators_make_monsters_until_shot_down_level_by_level()
    {
        var (g, x, y) = Arena(HeroClass.Warrior);
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        var gen = g.PutGenerator(MonsterKind.Ghost, x + 4, y + 4, level: 3);
        Steps(g, 30);
        Assert.NotEmpty(g.Monsters);
        g.Monsters.Clear();
        // Stand in line with it and shoot it down.
        g.PutHero(C(x - 2, y + 4));
        for (int i = 0; i < 60 * 6 && !gen.Dead; i++) { g.Tick(new Controls(1, 0, Fire: true)); g.Monsters.Clear(); }
        Assert.True(gen.Dead);
        Assert.Contains(Sfx.GeneratorDown, heard);
        Assert.True(g.Score >= 3 * 10 + 50);
        Steps(g, 60 * 4);
        Assert.Empty(g.Monsters);
    }

    [Fact]
    public void A_potion_destroys_everything_near_and_knocks_generators_down()
    {
        var (g, x, y) = Arena(HeroClass.Wizard);
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        for (int i = 0; i < 6; i++) g.PutMonster(MonsterKind.Grunt, C(x + 3, y - 3 + i), strength: 3);
        var gen = g.PutGenerator(MonsterKind.Grunt, x - 4, y + 4, level: 3);
        g.PutItem(ItemKind.Potion, x + 1, y);
        Steps(g, 20, new Controls(1, 0));
        Assert.Equal(1, g.Hero.Potions);
        g.Tick(new Controls(0, 0, Magic: true));
        Assert.DoesNotContain(g.Monsters, m => !m.Dead);
        Assert.True(gen.Dead, "the wizard's magic flattens a strength-3 generator");
        Assert.Equal(0, g.Hero.Potions);
        Assert.Contains(Sfx.Magic, heard);
    }

    [Fact]
    public void A_key_opens_a_whole_door()
    {
        var (g, x, y) = Arena();
        for (int yy = y - 7; yy <= y + 7; yy++) g.SetCell(x + 3, yy, Math.Abs(yy - y) <= 1 ? Cell.Door : Cell.Wall);
        Steps(g, 60, new Controls(1, 0));
        Assert.Equal(Cell.Door, g.Cells[x + 3, y]);
        Assert.True(g.Hero.Pos.X < x + 3);
        g.PutItem(ItemKind.Key, x - 2, y);
        Steps(g, 90, new Controls(-1, 0));
        Assert.Equal(1, g.Hero.Keys);
        Steps(g, 120, new Controls(1, 0));
        Assert.Equal(Cell.Floor, g.Cells[x + 3, y]);
        Assert.Equal(Cell.Floor, g.Cells[x + 3, y + 1]);
        Assert.Equal(0, g.Hero.Keys);
        Assert.True(g.Hero.Pos.X > x + 3);
    }

    [Fact]
    public void Treasure_scores_and_the_exit_leads_on()
    {
        var (g, x, y) = Arena();
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.PutItem(ItemKind.Treasure, x + 1, y);
        g.SetCell(x + 3, y, Cell.Exit);
        Steps(g, 60, new Controls(1, 0));
        Assert.Equal(GameEngine.TreasurePoints, g.Score);
        Assert.Equal(Phase.Exiting, g.Phase);
        Assert.Contains(Sfx.Exit, heard);
        Steps(g, (int)(60 * GameEngine.ExitSeconds) + 2);
        Assert.Equal(2, g.Level);
    }

    [Fact]
    public void Running_low_brings_the_warning_and_running_out_ends_the_game()
    {
        var (g, _, _) = Arena(HeroClass.Valkyrie);
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.Hero.Health = GameEngine.LowHealth + 1;
        Steps(g, 60 * 2);
        Assert.Equal("VALKYRIE NEEDS FOOD, BADLY!", g.Message);
        Assert.Contains(Sfx.LowHealth, heard);
        g.Hero.Health = 1;
        Steps(g, 70);
        Assert.Equal(Phase.Dying, g.Phase);
        Steps(g, (int)(60 * GameEngine.DyingSeconds) + 2);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void The_demonstration_pilot_gets_through_a_level_and_the_game_ends_in_the_table()
    {
        var g = new GameEngine(1, new HighScoreTable(_dir));
        g.StartGame(HeroClass.Valkyrie);
        var pilot = new AttractPilot(1);
        for (int i = 0; i < 60 * 60 * 5 && g.State == GameState.Playing && g.Level < 2; i++) g.Tick(pilot.Decide(g));
        Assert.True(g.Level >= 2, $"health {g.Hero.Health}, at {g.Hero.Pos}, exit {g.Dungeon.Exit}, monsters {g.Monsters.Count}");
        g.Hero.Health = 3;
        for (int i = 0; i < 60 * 30 && g.State == GameState.Playing; i++) g.Tick(default);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(60 * 4);
        if (g.HighScores.Qualifies(g.Score))
        {
            Assert.Equal(GameState.EnterInitials, g.State);
            g.Initials!.Finish();
            g.Idle(2);
        }
        Assert.Equal(GameState.HighScores, g.State);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Run()
        {
            var g = new GameEngine(21, new HighScoreTable(_dir));
            g.StartGame(HeroClass.Elf);
            var pilot = new AttractPilot(5);
            for (int i = 0; i < 60 * 60; i++) g.Tick(pilot.Decide(g));
            return $"{g.Level} {g.Score} {g.Hero.Health} {g.Hero.Pos} {g.Monsters.Count} {g.Generators.Count(x => !x.Dead)}";
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void The_title_plays_its_tune_and_the_demonstration_has_sound()
    {
        var g = new GameEngine(5, new HighScoreTable(_dir));
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.Idle(1);
        Assert.Equal([Sfx.TitleTune], heard);
        g.ChooseNext(1);
        Assert.Equal(HeroClass.Valkyrie, g.Chosen);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + 20)));
        Assert.Equal(GameState.Attract, g.State);
        Assert.Contains(Sfx.StartTune, heard);
        Assert.Contains(Sfx.Music, heard);
        Assert.Contains(Sfx.Shoot, heard);
        for (int i = 0; i < 60 * GameEngine.AttractSeconds && g.State == GameState.Attract; i++) g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(Sfx.MusicStop, heard.Last(s => s is Sfx.Music or Sfx.MusicStop));
        g.Idle((int)(60 * (GameEngine.HighScoreSeconds + 1)));
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void Pausing_holds_everything()
    {
        var (g, _, _) = Arena();
        var at = g.Hero.Pos;
        int h = g.Hero.Health;
        g.Pause();
        Steps(g, 90, new Controls(1, 0));
        Assert.Equal(at, g.Hero.Pos);
        Assert.Equal(h, g.Hero.Health);
        g.Resume();
        Steps(g, 30, new Controls(1, 0));
        Assert.True(g.Hero.Pos.X > at.X);
    }

    [Fact]
    public void Every_sound_and_sprite_is_there()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Sounds");
        Assert.All(Enum.GetValues<Sfx>().Where(s => s != Sfx.MusicStop).Select(s => s.ToString()), f => Assert.True(File.Exists(Path.Combine(folder, f + ".wav")), f));
        for (int i = 0; i < 6; i++)
            Assert.All(Sprites.MonsterSprite(i).Rows.SelectMany(r => r).Where(ch => ch != '.'), ch => Assert.True(Sprites.Colours.ContainsKey(ch), $"colour {ch}"));
        for (int i = 0; i < 4; i++)
        {
            Assert.All(Sprites.HeroSprite(i).Rows.SelectMany(r => r).Where(ch => ch != '.'), ch => Assert.True(Sprites.Colours.ContainsKey(ch)));
            Assert.All(Sprites.ItemSprite(i).Rows.SelectMany(r => r).Where(ch => ch != '.'), ch => Assert.True(Sprites.Colours.ContainsKey(ch)));
        }
    }
}
