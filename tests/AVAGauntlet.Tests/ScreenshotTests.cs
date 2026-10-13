using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAGauntlet.Engine;
using AVAGauntlet.Models;
using AVAGauntlet.Views;
using Controls = AVAGauntlet.Engine.Controls;

namespace AVAGauntlet.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set GAUNTLET_SHOTS to a
/// folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gauntlet-ui-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(_dir));
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("GAUNTLET_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAGauntlet-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    [AvaloniaFact]
    public void The_title_and_a_level_under_way()
    {
        var (w, c, g) = Open();
        g.ChooseNext(2);
        g.Idle(60 * 2);
        Shot(w, c, "title");
        g.StartGame(HeroClass.Valkyrie);
        g.Go();
        var pilot = new AttractPilot(2);
        for (int i = 0; i < 60 * 90 && g.State == GameState.Playing; i++)
        {
            g.Tick(pilot.Decide(g));
            if (i > 60 * 10 && g.Phase == Phase.Playing && g.Monsters.Count >= 6 && g.Shots.Any(s => !s.Hostile)) break;
        }
        Shot(w, c, "play");
        w.Close();
    }

    [AvaloniaFact]
    public void A_horde_and_a_potion()
    {
        var (w, c, g) = Open(seed: 3);
        g.StartGame(HeroClass.Wizard);
        g.JumpTo(5);
        // Bring the hero to a generator's room and let the monsters gather.
        var gen = g.Generators.First(x => x.Kind is MonsterKind.Grunt or MonsterKind.Ghost or MonsterKind.Demon);
        var spot = new Vec(gen.X + 0.5, gen.Y + 0.5);
        var dist = g.Dungeon.Distances(gen.X, gen.Y);
        var near = Enumerable.Range(0, g.Width).SelectMany(x => Enumerable.Range(0, g.Height).Select(y => (x, y)))
            .Where(p => dist[p.x, p.y] is >= 4 and <= 5 && g.Walkable(p.x, p.y)).First();
        g.PutHero(new Vec(near.x + 0.5, near.y + 0.5));
        // A crowd closing in.
        var kinds = new[] { MonsterKind.Grunt, MonsterKind.Ghost, MonsterKind.Demon, MonsterKind.Grunt, MonsterKind.Lobber, MonsterKind.Ghost, MonsterKind.Sorcerer, MonsterKind.Grunt, MonsterKind.Demon, MonsterKind.Ghost };
        int k = 0;
        foreach (var p in Enumerable.Range(0, g.Width).SelectMany(x => Enumerable.Range(0, g.Height).Select(y => (x, y)))
            .Where(p => g.Walkable(p.x, p.y) && Math.Abs(p.x + 0.5 - g.Hero.Pos.X) + Math.Abs(p.y + 0.5 - g.Hero.Pos.Y) is > 2.5 and < 6)
            .OrderBy(p => (p.x * 7 + p.y * 3) % 11).Take(kinds.Length))
            g.PutMonster(kinds[k++], new Vec(p.x + 0.5, p.y + 0.5), strength: 2);
        for (int i = 0; i < 40; i++) { g.Tick(new Controls(1, 0, Fire: i > 20)); g.Hero.Health = 1200; }
        Shot(w, c, "horde");
        g.Hero.Potions = 2;
        g.Tick(new Controls(0, 0, Magic: true));
        for (int i = 0; i < 16; i++) g.Tick(default);
        Shot(w, c, "magic");
        w.Close();
    }

    [AvaloniaFact]
    public void Needs_food_badly()
    {
        var (w, c, g) = Open(seed: 4);
        g.StartGame(HeroClass.Elf);
        g.JumpTo(3);
        var pilot = new AttractPilot(4);
        for (int i = 0; i < 60 * 30 && g.State == GameState.Playing; i++) { g.Tick(pilot.Decide(g)); g.Hero.Health = Math.Max(g.Hero.Health, 400); }
        g.Hero.Health = GameEngine.LowHealth - 5;
        for (int i = 0; i < 20; i++) g.Tick(pilot.Decide(g));
        Shot(w, c, "food");
        w.Close();
    }

    [AvaloniaFact]
    public void Game_over_and_the_high_scores()
    {
        var (w, c, g) = Open(seed: 6);
        g.StartGame(HeroClass.Warrior);
        g.Go();
        g.Hero.Health = 2;
        while (g.State == GameState.Playing) g.Tick(default);
        g.Idle(60 * 4);
        if (g.State == GameState.EnterInitials) { g.Initials!.Finish(); g.Idle(5); }
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle(20);
        Shot(w, c, "high-scores");
        w.Close();
    }
}
