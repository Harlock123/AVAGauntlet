using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AVAGauntlet.Engine;
using AVAGauntlet.Models;

namespace AVAGauntlet.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), IPen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAGauntlet/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
        {
            b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
            BrushCache[argb] = b;
        }
        return b;
    }

    private static IPen Pen(uint argb, double thickness)
    {
        if (!PenCache.TryGetValue((argb, thickness), out var p))
        {
            p = new ImmutablePen(argb, thickness);
            PenCache[(argb, thickness)] = p;
        }
        return p;
    }

    private static readonly IBrush White = Brush(0xFFF2F2F2);
    private static readonly IBrush Yellow = Brush(0xFFFFE14A);
    private static readonly IBrush Red = Brush(0xFFFF3D3D);
    private static readonly IBrush Orange = Brush(0xFFFF8A1E);
    private static readonly IBrush Cyan = Brush(0xFF4FD7FF);
    private static readonly IBrush Dim = Brush(0xFF8A8AA6);

    /// <summary>Each level's walls: face, light edge, dark edge.</summary>
    private static readonly (uint Face, uint Light, uint Dark)[] Stone =
    [
        (0xFF7A5A3A, 0xFFA8845A, 0xFF4A3420), (0xFF5A6A8A, 0xFF8A9AB8, 0xFF34405A), (0xFF6A3A5A, 0xFF9A5A84, 0xFF40203A),
        (0xFF3A6A4A, 0xFF5A9A6A, 0xFF204030), (0xFF7A6A3A, 0xFFA89A5A, 0xFF4A4020), (0xFF4A4A5A, 0xFF7A7A8A, 0xFF2A2A34),
    ];

    /// <summary>Where the view's top-left is, in tiles: on the hero, but not past the dungeon's edges.</summary>
    private Vec Camera()
    {
        var g = Engine;
        double half = ViewTiles / 2.0;
        double x = Math.Clamp(g.Hero.Pos.X - half, 0, Math.Max(0, g.Width - ViewTiles));
        double y = Math.Clamp(g.Hero.Pos.Y - half, 0, Math.Max(0, g.Height - ViewTiles));
        return new Vec(x, y);
    }

    private Vec _cam;

    /// <summary>Tiles to the screen.</summary>
    private Point S(double x, double y) => new((x - _cam.X) * TilePx, (y - _cam.Y) * TilePx);
    private Point S(Vec v) => S(v.X, v.Y);

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        _cam = g.State is GameState.Title or GameState.HighScores ? new Vec(Math.Sin(_clock * 0.1) * 6 + 6, 4) : Camera();
        var view = new Rect(0, 0, ViewSize, ViewSize);
        using (dc.PushClip(view))
        {
            dc.FillRectangle(Brush(0xFF000000), view);
            DrawDungeon(dc);
            switch (g.State)
            {
                case GameState.Title: DrawTitle(dc); break;
                case GameState.HighScores: DrawHighScores(dc); break;
                default: DrawPlay(dc); break;
            }
        }
        DrawPanel(dc);
    }

    // ----- the dungeon ----------------------------------------------------------------------------

    private void DrawDungeon(DrawingContext dc)
    {
        var g = Engine;
        var (face, light, dark) = Stone[(g.Level - 1) % Stone.Length];
        int x0 = (int)Math.Floor(_cam.X), y0 = (int)Math.Floor(_cam.Y);
        for (int x = x0; x <= x0 + ViewTiles; x++)
            for (int y = y0; y <= y0 + ViewTiles; y++)
            {
                if (x < 0 || y < 0 || x >= g.Width || y >= g.Height) continue;
                var p = S(x, y);
                var r = new Rect(p.X, p.Y, TilePx, TilePx);
                switch (g.Cells[x, y])
                {
                    case Cell.Wall:
                        // Only the walls next to the open: solid rock beyond is left dark.
                        if (!NearOpen(x, y)) break;
                        dc.FillRectangle(Brush(face), r);
                        dc.FillRectangle(Brush(light), new Rect(r.X, r.Y, TilePx, 4));
                        dc.FillRectangle(Brush(light), new Rect(r.X, r.Y, 4, TilePx));
                        dc.FillRectangle(Brush(dark), new Rect(r.X, r.Bottom - 4, TilePx, 4));
                        dc.FillRectangle(Brush(dark), new Rect(r.Right - 4, r.Y, 4, TilePx));
                        dc.FillRectangle(Brush(dark), new Rect(r.X + 4, r.Y + TilePx / 2 - 1, TilePx - 8, 2));
                        break;
                    case Cell.Door:
                        dc.FillRectangle(Brush(0xFF8A5A2A), r);
                        dc.FillRectangle(Brush(0xFF5A3A18), new Rect(r.X, r.Y + 6, TilePx, 4));
                        dc.FillRectangle(Brush(0xFF5A3A18), new Rect(r.X, r.Bottom - 10, TilePx, 4));
                        dc.FillRectangle(Brush(0xFFB0B4C0), new Rect(r.X + 13, r.Y + 13, 6, 6));
                        break;
                    case Cell.Exit:
                        DrawFloor(dc, r, x, y);
                        for (int i = 0; i < 4; i++) dc.FillRectangle(Brush(i % 2 == 0 ? 0xFF2A2A34u : 0xFF5A5A6Au), new Rect(r.X + 2, r.Y + 2 + i * 7, TilePx - 4, 7));
                        Text(dc, "EXIT", new Point(r.Center.X, r.Center.Y), 20, Yellow, center: true);
                        break;
                    default:
                        DrawFloor(dc, r, x, y);
                        break;
                }
            }
    }

    private bool NearOpen(int x, int y)
    {
        var g = Engine;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < g.Width && ny < g.Height && g.Cells[nx, ny] != Cell.Wall) return true;
            }
        return false;
    }

    private static void DrawFloor(DrawingContext dc, Rect r, int x, int y)
    {
        dc.FillRectangle(Brush(((x + y) & 1) == 0 ? 0xFF1A1A22u : 0xFF16161Eu), r);
        if (((x * 7 + y * 13) % 5) == 0) dc.FillRectangle(Brush(0xFF24242E), new Rect(r.X + 6, r.Y + 10, 8, 2));
    }

    private void DrawGenerator(DrawingContext dc, Generator gen)
    {
        var p = S(gen.X, gen.Y);
        var r = new Rect(p.X + 1, p.Y + 1, TilePx - 2, TilePx - 2);
        uint colour = gen.Level switch { >= 3 => 0xFFC83A2A, 2 => 0xFFC8A02A, _ => 0xFF8A8A9A };
        dc.FillRectangle(Brush(colour), r);
        dc.DrawRectangle(null, Pen(0xFF000000, 2), r);
        // Bones and the kind of thing it makes.
        dc.FillRectangle(Brush(0xFFE8E4D8), new Rect(r.X + 4, r.Y + 6, r.Width - 8, 3));
        dc.FillRectangle(Brush(0xFFE8E4D8), new Rect(r.X + 4, r.Bottom - 9, r.Width - 8, 3));
        DrawSprite(dc, Sprites.MonsterSprite((int)gen.Kind), new Point(r.X + 7, r.Y + 7), false, 1.1);
    }

    // ----- the play -------------------------------------------------------------------------------

    private void DrawPlay(DrawingContext dc)
    {
        var g = Engine;
        foreach (var gen in g.Generators) if (!gen.Dead) DrawGenerator(dc, gen);
        foreach (var it in g.Items) DrawSprite(dc, Sprites.ItemSprite((int)it.Kind), S(it.X, it.Y), false);

        foreach (var m in g.Monsters)
        {
            if (m.Hidden && (int)(_clock * 20) % 3 != 0) continue;
            var at = S(m.Pos - new Vec(0.5, 0.5));
            DrawSprite(dc, Sprites.MonsterSprite((int)m.Kind), at, m.Facing.X < -0.1);
        }

        foreach (var s in g.Shots)
        {
            var c = S(s.Pos);
            if (!s.Hostile)
            {
                uint colour = g.Hero.Stats.Colour;
                dc.DrawEllipse(Brush(colour), Pen(0xFFFFFFFF, 1.5), c, 6, 6);
            }
            else if (s.Lobbed)
            {
                // A rock in the air: its shadow on the ground, and higher in the middle of its flight.
                dc.DrawEllipse(Brush(0x60000000), null, c, 5, 3);
                dc.DrawEllipse(Brush(0xFF8A8A9A), null, c + new Point(0, -10), 5, 5);
            }
            else
            {
                dc.DrawEllipse(Brush((int)(_clock * 16) % 2 == 0 ? 0xFFFF6A1Au : 0xFFFFE14Au), null, c, 7, 7);
                dc.DrawEllipse(Brush(0xFFFFFFFF), null, c, 3, 3);
            }
        }

        var h = g.Hero;
        if (g.Phase != Phase.Dying || (int)(_clock * 8) % 2 == 0)
        {
            var at = S(h.Pos - new Vec(0.5, 0.5));
            bool step = (int)(h.Walk * 6) % 2 == 0;
            DrawSprite(dc, Sprites.HeroSprite((int)h.Class), at + new Point(0, step ? 0 : -1.5), h.Facing.X < -0.1);
        }

        foreach (var fx in g.Effects)
        {
            var c = S(fx.Pos);
            double t = fx.Age / fx.Life;
            switch (fx.Kind)
            {
                case EffectKind.Blast:
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4 + t * 2;
                        double r0 = 4 + t * 14, r1 = 8 + t * 26;
                        dc.DrawLine(Pen(i % 2 == 0 ? 0xFFFFE14Au : 0xFFFF6A1Au, 3), c + new Point(Math.Cos(a) * r0, Math.Sin(a) * r0), c + new Point(Math.Cos(a) * r1, Math.Sin(a) * r1));
                    }
                    break;
                case EffectKind.Magic:
                    dc.FillRectangle(Brush((uint)((1 - t) * 110) << 24 | 0xFFFFFF), new Rect(0, 0, ViewSize, ViewSize));
                    dc.DrawEllipse(null, Pen(0xFF4FD7FF, 6), c, t * GameEngine.MagicRange * TilePx, t * GameEngine.MagicRange * TilePx);
                    break;
                case EffectKind.Sparkle:
                    for (int i = 0; i < 6; i++)
                    {
                        double a = i * Math.PI / 3;
                        dc.FillRectangle(Brush(0xFFE8E8F0), new Rect(c.X + Math.Cos(a) * t * 16 - 2, c.Y + Math.Sin(a) * t * 16 - 2, 4, 4));
                    }
                    break;
                case EffectKind.Score:
                    Text(dc, fx.Text, c + new Point(0, -12 - fx.Age * 20), 30, Yellow, center: true);
                    break;
            }
        }

        // What the narrator says.
        if (g.Message is { } msg)
        {
            dc.FillRectangle(Brush(0xB0000000), new Rect(0, 16, ViewSize, 48));
            Text(dc, msg, new Point(ViewSize / 2, 40), 44, msg.Contains("FOOD") || msg.Contains("FALLEN") ? Red : Yellow, center: true);
        }

        switch (g.State)
        {
            case GameState.Paused: Banner(dc, "PAUSED", "ESC TO QUIT, ANY OTHER KEY TO RESUME"); break;
            case GameState.GameOver: Banner(dc, "GAME  OVER", null); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default:
                if (g.Phase == Phase.Ready) Banner(dc, $"LEVEL {g.Level}", g.State == GameState.Attract ? "DEMONSTRATION" : "FIND THE EXIT");
                else if (g.Phase == Phase.Exiting) Banner(dc, "EXIT!", $"ON TO LEVEL {g.Level + 1}");
                else if (g.State == GameState.Attract && (int)(_clock * 2) % 2 == 0)
                    Text(dc, "DEMONSTRATION", new Point(ViewSize / 2, ViewSize - 30), 36, Red, center: true);
                break;
        }
    }

    private static void Banner(DrawingContext dc, string title, string? sub)
    {
        dc.FillRectangle(Brush(0xC8000000), new Rect(80, ViewSize / 2 - 60, ViewSize - 160, sub is null ? 90 : 120));
        Text(dc, title, new Point(ViewSize / 2, ViewSize / 2 - 16), 72, Yellow, center: true);
        if (sub is not null) Text(dc, sub, new Point(ViewSize / 2, ViewSize / 2 + 36), 32, White, center: true);
    }

    /// <summary>A sprite with its top-left at a screen point, two screen pixels to its pixel unless told otherwise.</summary>
    private static void DrawSprite(DrawingContext dc, Sprites.Sprite s, Point at, bool flip, double px = 2)
    {
        for (int r = 0; r < s.Height; r++)
        {
            var row = s.Rows[r];
            for (int c = 0; c < s.Width; c++)
            {
                char ch = row[flip ? s.Width - 1 - c : c];
                if (ch == '.') continue;
                dc.FillRectangle(Brush(Sprites.Colours.GetValueOrDefault(ch, 0xFFFF00FF)), new Rect(at.X + c * px, at.Y + r * px, px + 0.2, px + 0.2));
            }
        }
    }

    private static void Text(DrawingContext dc, string text, Point at, double size, IBrush brush, bool center = false, bool right = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, brush);
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }
}
