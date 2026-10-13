using Avalonia;
using Avalonia.Media;
using AVAGauntlet.Engine;
using AVAGauntlet.Models;

namespace AVAGauntlet.Views;

public sealed partial class GameCanvas
{
    /// <summary>The panel on the right: the hero, health, score, level, keys and potions, and the keys to press.</summary>
    private void DrawPanel(DrawingContext dc)
    {
        var g = Engine;
        double x0 = ViewSize, cx = ViewSize + (ScreenWidth - ViewSize) / 2;
        dc.FillRectangle(Brush(0xFF0C0C14), new Rect(x0, 0, ScreenWidth - x0, ScreenHeight));
        dc.FillRectangle(Brush(0xFF3A3A5A), new Rect(x0, 0, 3, ScreenHeight));
        Text(dc, "AVA GAUNTLET", new Point(cx, 26), 36, Orange, center: true);
        bool playing = g.State is GameState.Playing or GameState.Paused or GameState.GameOver or GameState.EnterInitials or GameState.Attract;
        var hero = playing ? g.Hero.Class : g.Chosen;
        var stats = ClassStats.Of(hero);
        DrawSprite(dc, Sprites.HeroSprite((int)hero), new Point(cx - 32, 56), false, 4);
        Text(dc, stats.Name, new Point(cx, 138), 38, Brush(stats.Colour), center: true);
        if (playing)
        {
            Text(dc, "HEALTH", new Point(cx, 182), 28, Dim, center: true);
            bool low = g.Hero.Health < GameEngine.LowHealth;
            Text(dc, Math.Max(0, g.Hero.Health).ToString(), new Point(cx, 216), 54, low && (int)(_clock * 4) % 2 == 0 ? Red : White, center: true);
            Text(dc, "SCORE", new Point(cx, 262), 28, Dim, center: true);
            Text(dc, g.Score.ToString(), new Point(cx, 292), 40, Yellow, center: true);
            Text(dc, "LEVEL", new Point(cx, 334), 28, Dim, center: true);
            Text(dc, g.Level.ToString(), new Point(cx, 364), 40, White, center: true);
            Text(dc, "KEYS", new Point(cx, 406), 28, Dim, center: true);
            for (int i = 0; i < Math.Min(g.Hero.Keys, 6); i++) DrawSprite(dc, Sprites.Key, new Point(x0 + 26 + i * 34, 420), false);
            Text(dc, "POTIONS", new Point(cx, 474), 28, Dim, center: true);
            for (int i = 0; i < Math.Min(g.Hero.Potions, 6); i++) DrawSprite(dc, Sprites.Potion, new Point(x0 + 26 + i * 34, 488), false);
        }
        else
        {
            Text(dc, "HI-SCORE", new Point(cx, 200), 28, Dim, center: true);
            Text(dc, g.HighScores.TopScore.ToString(), new Point(cx, 232), 40, Yellow, center: true);
        }
        string[] help = ["ARROWS: MOVE (8 WAYS)", "Z / SPACE: FIRE", "  (STAND AND SHOOT)", "X / SHIFT: MAGIC POTION", "WALK INTO DOORS WITH A KEY", "P PAUSE   M MUTE", "CTRL+S SCREENSHOT"];
        for (int i = 0; i < help.Length; i++) Text(dc, help[i], new Point(cx, 580 + i * 24), 21, Dim, center: true);
    }

    private static void Shade(DrawingContext dc) => dc.FillRectangle(Brush(0xD0000008), new Rect(28, 28, ViewSize - 56, ViewSize - 56));

    /// <summary>The title: choose a hero, and what's in the dungeon.</summary>
    private void DrawTitle(DrawingContext dc)
    {
        var g = Engine;
        double cx = ViewSize / 2;
        Shade(dc);
        Text(dc, "AVA GAUNTLET", new Point(cx, 86), 96, Orange, center: true);
        Text(dc, "AFTER THE 1985 ARCADE CLASSIC", new Point(cx, 140), 30, Dim, center: true);
        Text(dc, "CHOOSE YOUR HERO:  1  2  3  4  (OR LEFT, RIGHT AND ENTER)", new Point(cx, 182), 28, White, center: true);
        for (int i = 0; i < 4; i++)
        {
            var c = (HeroClass)i;
            var st = ClassStats.Of(c);
            double x = 60 + i * 168;
            bool chosen = c == g.Chosen;
            var box = new Rect(x, 206, 152, 196);
            dc.FillRectangle(Brush(chosen ? 0xFF2A2A10u : 0xFF12121Au), box);
            dc.DrawRectangle(null, Pen(chosen ? 0xFFFFE14Au : 0xFF3A3A5Au, chosen ? 3 : 1), box);
            DrawSprite(dc, Sprites.HeroSprite(i), new Point(x + 44, 216), false, 4);
            Text(dc, $"{i + 1}. {st.Name}", new Point(x + 76, 296), 30, Brush(st.Colour), center: true);
            string[] lines = c switch
            {
                HeroClass.Warrior => ["HARDEST HITTER", "SOME ARMOUR"],
                HeroClass.Valkyrie => ["BEST ARMOUR", "GOOD MAGIC"],
                HeroClass.Wizard => ["STRONGEST MAGIC", "NO ARMOUR"],
                _ => ["FASTEST ON FOOT", "AND WITH A BOW"],
            };
            for (int l = 0; l < lines.Length; l++) Text(dc, lines[l], new Point(x + 76, 328 + l * 24), 22, White, center: true);
        }

        double y = 438;
        void Row(Sprites.Sprite art, string text)
        {
            DrawSprite(dc, art, new Point(96, y - 16), false);
            Text(dc, text, new Point(146, y), 26, White);
            y += 38;
        }
        Row(Sprites.Ghost, "GHOSTS AND GRUNTS POUR FROM THE GENERATORS: SHOOT THEM DOWN");
        Row(Sprites.Demon, "DEMONS THROW FIRE; LOBBERS THROW ROCKS OVER THE WALLS");
        Row(Sprites.Death, "DEATH DRAINS YOUR HEALTH. ONLY MAGIC GETS RID OF IT");
        Row(Sprites.Food, "YOUR HEALTH RUNS DOWN: FIND FOOD (AND DON'T SHOOT IT!)");
        Row(Sprites.Key, "KEYS OPEN DOORS TO TREASURE.  POTIONS: MAGIC");
        if (_clock % 1 < 0.65) Text(dc, "PRESS ENTER OR SPACE TO START", new Point(cx, 680), 36, Yellow, center: true);
        Text(dc, "© 2026 LONNIE WATSON", new Point(cx, 716), 22, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        double cx = ViewSize / 2;
        Shade(dc);
        Text(dc, "TOP ADVENTURERS", new Point(cx, 100), 72, Orange, center: true);
        Text(dc, "RANK      SCORE   NAME  LEVEL", new Point(cx, 160), 30, Dim, center: true);
        var table = Engine.HighScores.Entries;
        for (int i = 0; i < table.Count; i++)
        {
            var e = table[i];
            bool fresh = i == Engine.NewEntryIndex;
            if (fresh && _clock % 0.5 > 0.35) continue;
            var brush = fresh ? Yellow : i == 0 ? Orange : White;
            Text(dc, $"{i + 1,2}.  {e.Score,9}   {e.Initials}   {e.Stage,3}", new Point(cx, 206 + i * 42), 38, brush, center: true);
        }
        if (_clock % 1 < 0.65) Text(dc, "PRESS ENTER OR SPACE TO PLAY", new Point(cx, 660), 34, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var entry = Engine.Initials;
        if (entry is null) return;
        double cx = ViewSize / 2;
        dc.FillRectangle(Brush(0xE8000000), new Rect(cx - 300, 220, 600, 330));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 262), 56, Yellow, center: true);
        Text(dc, $"{Engine.Score}", new Point(cx, 316), 48, White, center: true);
        for (int i = 0; i < HighScoreTable.InitialsLength; i++)
        {
            var at = new Point(cx - 64 + i * 64, 410);
            bool cursor = i == entry.Cursor;
            Text(dc, entry.Initials[i].ToString(), at, 90, cursor ? Yellow : White, center: true);
            if (cursor && _clock % 0.6 < 0.4) dc.FillRectangle(Yellow, new Rect(at.X - 20, at.Y + 40, 40, 6));
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE.  ENTER WHEN DONE", new Point(cx, 516), 26, Dim, center: true);
    }
}
