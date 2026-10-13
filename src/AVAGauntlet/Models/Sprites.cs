namespace AVAGauntlet.Models;

/// <summary>
/// The game's pixel art, this game's own drawings, painted from ellipses and boxes into a pixel grid. Each letter is a colour (see <see cref="Colours"/>); '.' is clear.
/// </summary>
public static class Sprites
{
    public sealed record Sprite(string[] Rows)
    {
        public int Width => Rows[0].Length;
        public int Height => Rows.Length;
    }

    private static Sprite S(params string[] rows)
    {
        if (rows.Any(r => r.Length != rows[0].Length))
            throw new ArgumentException("Sprite rows must be the same width: " + string.Join("|", rows));
        return new Sprite(rows);
    }

    /// <summary>A pixel grid to paint shapes into.</summary>
    private sealed class Canvas(int width, int height)
    {
        private readonly char[,] _px = Fill(width, height);

        private static char[,] Fill(int w, int h)
        {
            var g = new char[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++) g[x, y] = '.';
            return g;
        }

        public Canvas Ellipse(double cx, double cy, double rx, double ry, char c)
        {
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    double dx = (x + 0.5 - cx) / rx, dy = (y + 0.5 - cy) / ry;
                    if (dx * dx + dy * dy <= 1) _px[x, y] = c;
                }
            return this;
        }

        public Canvas Box(int x0, int y0, int x1, int y1, char c)
        {
            for (int x = Math.Max(0, x0); x <= Math.Min(width - 1, x1); x++)
                for (int y = Math.Max(0, y0); y <= Math.Min(height - 1, y1); y++) _px[x, y] = c;
            return this;
        }

        public Canvas Dot(int x, int y, char c) => Box(x, y, x, y, c);

        public Sprite Done()
        {
            var rows = new string[height];
            for (int y = 0; y < height; y++)
            {
                var chars = new char[width];
                for (int x = 0; x < width; x++) chars[x] = _px[x, y];
                rows[y] = new string(chars);
            }
            return new Sprite(rows);
        }
    }

    public static readonly IReadOnlyDictionary<char, uint> Colours = new Dictionary<char, uint>
    {
        ['w'] = 0xFFF4F4F8, // white
        ['k'] = 0xFF14141C, // black
        ['s'] = 0xFFFFD0A0, // skin
        ['m'] = 0xFFA8ACB8, // steel
        ['d'] = 0xFF5C6070, // dark grey
        ['r'] = 0xFFE83A3A, // red
        ['R'] = 0xFF8A1E1E, // dark red
        ['b'] = 0xFF3A7AE8, // blue
        ['B'] = 0xFF1A3A8A, // dark blue
        ['y'] = 0xFFFFE14A, // yellow
        ['Y'] = 0xFFB08A10, // dark yellow
        ['g'] = 0xFF3CD84A, // green
        ['G'] = 0xFF1E7A2A, // dark green
        ['n'] = 0xFF9A6A30, // brown
        ['N'] = 0xFF5A3A18, // dark brown
        ['v'] = 0xFFB050F0, // violet
        ['V'] = 0xFF5A2A8A, // dark violet
        ['o'] = 0xFFFF8A1E, // orange
        ['c'] = 0xFF4FD7FF, // cyan
    };

    // ----- heroes: seen from above and in front, all 16 pixels square -----------------------------------

    /// <summary>The warrior: horned helm, bare arms, an axe.</summary>
    public static readonly Sprite Warrior = new Canvas(16, 16)
        .Ellipse(8, 9, 5, 5, 'R').Ellipse(8, 9, 3.6, 4, 'r')
        .Ellipse(8, 5, 3, 3, 'm').Dot(4, 2, 'w').Dot(4, 3, 'w').Dot(11, 2, 'w').Dot(11, 3, 'w').Box(6, 5, 9, 6, 's').Dot(7, 5, 'k').Dot(9, 5, 'k')
        .Box(2, 8, 3, 11, 's').Box(12, 8, 13, 11, 's').Box(13, 4, 13, 12, 'n').Box(13, 3, 15, 5, 'm')
        .Box(5, 14, 6, 15, 'N').Box(9, 14, 10, 15, 'N').Done();

    /// <summary>The valkyrie: winged helm, a round shield, a sword.</summary>
    public static readonly Sprite Valkyrie = new Canvas(16, 16)
        .Ellipse(8, 9, 5, 5, 'B').Ellipse(8, 9, 3.6, 4, 'b')
        .Ellipse(8, 5, 3, 3, 'm').Dot(3, 3, 'w').Dot(4, 4, 'w').Dot(12, 3, 'w').Dot(11, 4, 'w').Box(6, 5, 9, 6, 's').Dot(7, 5, 'k').Dot(9, 5, 'k')
        .Box(5, 1, 10, 2, 'y')
        .Ellipse(3, 10, 2.6, 3, 'y').Dot(3, 10, 'Y').Box(13, 3, 13, 11, 'w').Box(12, 9, 14, 9, 'Y')
        .Box(5, 14, 6, 15, 'B').Box(9, 14, 10, 15, 'B').Done();

    /// <summary>The wizard: pointed hat, long robe, a staff.</summary>
    public static readonly Sprite Wizard = new Canvas(16, 16)
        .Ellipse(8, 10, 5, 5, 'Y').Ellipse(8, 10, 3.6, 4.4, 'y')
        .Box(7, 0, 8, 1, 'v').Box(6, 2, 9, 3, 'v').Box(5, 4, 10, 4, 'V')
        .Box(6, 5, 9, 7, 's').Dot(7, 5, 'k').Dot(9, 5, 'k').Box(6, 7, 9, 8, 'w')
        .Box(2, 2, 2, 15, 'n').Ellipse(2, 2, 1.6, 1.6, 'c')
        .Box(5, 15, 10, 15, 'Y').Done();

    /// <summary>The elf: green hood, pointed ears, a bow.</summary>
    public static readonly Sprite Elf = new Canvas(16, 16)
        .Ellipse(8, 9, 4.6, 5, 'G').Ellipse(8, 9, 3.2, 4, 'g')
        .Ellipse(8, 5, 3, 3, 'G').Box(6, 5, 9, 6, 's').Dot(7, 5, 'k').Dot(9, 5, 'k').Dot(4, 4, 's').Dot(11, 4, 's')
        .Box(13, 3, 13, 13, 'n').Dot(12, 2, 'n').Dot(12, 14, 'n').Box(14, 3, 14, 13, 'w')
        .Box(5, 14, 6, 15, 'N').Box(9, 14, 10, 15, 'N').Done();

    public static Sprite HeroSprite(int c) => c switch { 0 => Warrior, 1 => Valkyrie, 2 => Wizard, _ => Elf };

    // ----- monsters -----------------------------------------------------------------------------------

    public static readonly Sprite Ghost = new Canvas(16, 16).Ellipse(8, 7, 6, 6, 'w').Box(2, 7, 13, 13, 'w')
        .Dot(3, 14, 'w').Dot(6, 14, 'w').Dot(9, 14, 'w').Dot(12, 14, 'w').Ellipse(6, 7, 1.4, 1.8, 'k').Ellipse(10, 7, 1.4, 1.8, 'k').Box(7, 10, 9, 11, 'k').Done();
    public static readonly Sprite Grunt = new Canvas(16, 16).Ellipse(8, 9, 6, 6, 'n').Ellipse(8, 5, 4, 3.4, 'N')
        .Dot(6, 5, 'y').Dot(10, 5, 'y').Box(7, 8, 9, 8, 'k').Box(13, 3, 14, 10, 'N').Ellipse(13.5, 3, 2, 2, 'N')
        .Box(4, 14, 6, 15, 'N').Box(10, 14, 12, 15, 'N').Done();
    public static readonly Sprite Demon = new Canvas(16, 16).Ellipse(8, 9, 5.6, 5.6, 'r').Box(3, 1, 4, 4, 'y').Box(11, 1, 12, 4, 'y')
        .Ellipse(6, 7, 1.4, 1.4, 'y').Ellipse(10, 7, 1.4, 1.4, 'y').Dot(6, 7, 'k').Dot(10, 7, 'k').Box(6, 11, 10, 11, 'k').Dot(7, 12, 'w').Dot(9, 12, 'w')
        .Box(1, 7, 2, 12, 'R').Box(13, 7, 14, 12, 'R').Done();
    public static readonly Sprite Lobber = new Canvas(16, 16).Ellipse(8, 10, 4.6, 4.6, 'g').Ellipse(8, 6, 3.4, 3, 'G')
        .Dot(7, 6, 'y').Dot(9, 6, 'y').Ellipse(13, 4, 2, 2, 'd').Box(12, 6, 12, 9, 'g').Box(5, 14, 6, 15, 'G').Box(10, 14, 11, 15, 'G').Done();
    public static readonly Sprite Sorcerer = new Canvas(16, 16).Ellipse(8, 10, 5, 5, 'V').Ellipse(8, 10, 3.6, 4.2, 'v')
        .Ellipse(8, 5, 3.2, 3.2, 'V').Box(6, 5, 9, 6, 'k').Dot(7, 5, 'c').Dot(9, 5, 'c').Box(1, 2, 1, 14, 'n').Ellipse(1, 2, 1.4, 1.4, 'r').Done();
    public static readonly Sprite Death = new Canvas(16, 16).Ellipse(8, 8, 6, 7, 'k').Box(2, 9, 13, 15, 'k')
        .Ellipse(8, 7, 3.4, 3.6, 'w').Ellipse(6.6, 6.6, 1, 1.2, 'k').Ellipse(9.4, 6.6, 1, 1.2, 'k').Box(7, 9, 9, 9, 'k')
        .Box(14, 1, 14, 15, 'n').Box(9, 1, 14, 2, 'm').Done();

    public static Sprite MonsterSprite(int k) => k switch { 0 => Ghost, 1 => Grunt, 2 => Demon, 3 => Lobber, 4 => Sorcerer, _ => Death };

    // ----- things lying about -------------------------------------------------------------------------

    /// <summary>Food: a roast on a plate.</summary>
    public static readonly Sprite Food = new Canvas(16, 16).Ellipse(8, 10, 7, 4, 'w').Ellipse(8, 10, 5.4, 2.8, 'm')
        .Ellipse(8, 8, 4.6, 3.4, 'n').Ellipse(7, 7, 2.6, 1.6, 'o').Box(11, 4, 13, 5, 'w').Done();
    public static readonly Sprite Treasure = new Canvas(16, 16).Box(2, 6, 13, 14, 'n').Box(2, 6, 13, 7, 'N').Box(2, 3, 13, 5, 'N')
        .Box(2, 10, 13, 10, 'Y').Box(7, 9, 8, 11, 'y').Dot(4, 4, 'y').Dot(8, 3, 'y').Dot(11, 4, 'y').Done();
    public static readonly Sprite Key = new Canvas(16, 16).Ellipse(4.5, 8, 3.4, 3.4, 'y').Ellipse(4.5, 8, 1.6, 1.6, '.')
        .Box(7, 7, 14, 8, 'y').Box(12, 9, 12, 10, 'y').Box(14, 9, 14, 11, 'y').Done();
    public static readonly Sprite Potion = new Canvas(16, 16).Ellipse(8, 10, 5, 5, 'b').Ellipse(8, 11, 3.4, 3, 'c').Box(6, 2, 9, 5, 'b').Box(6, 1, 9, 1, 'n')
        .Dot(6, 8, 'w').Done();

    public static Sprite ItemSprite(int k) => k switch { 0 => Food, 1 => Treasure, 2 => Key, _ => Potion };
}
