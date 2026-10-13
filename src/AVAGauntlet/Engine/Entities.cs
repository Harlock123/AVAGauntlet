using AVAGauntlet.Models;

namespace AVAGauntlet.Engine;

public enum HeroClass { Warrior, Valkyrie, Wizard, Elf }

/// <summary>What each kind of hero is good at.</summary>
public sealed record ClassStats(string Name, double Speed, int ShotDamage, double ShotSpeed, double FireEvery, double Armour, int Magic, uint Colour)
{
    public static ClassStats Of(HeroClass c) => c switch
    {
        // Strong shots, a little armour.
        HeroClass.Warrior => new("WARRIOR", 3.0, 3, 9, 0.34, 0.2, 1, 0xFFE83A3A),
        // The best armour.
        HeroClass.Valkyrie => new("VALKYRIE", 3.2, 2, 9, 0.3, 0.45, 2, 0xFF3A7AE8),
        // The strongest magic; no armour.
        HeroClass.Wizard => new("WIZARD", 3.2, 2, 10, 0.28, 0.0, 3, 0xFFFFE14A),
        // The fastest on his feet and with his bow.
        _ => new("ELF", 4.2, 1, 12, 0.2, 0.1, 2, 0xFF3CD84A),
    };
}

public sealed class Hero
{
    public HeroClass Class { get; set; }
    public ClassStats Stats => ClassStats.Of(Class);
    public Vec Pos { get; set; }
    /// <summary>Which way it faces (and shoots): one of eight directions.</summary>
    public Vec Facing { get; set; } = new(0, 1);
    public int Health { get; set; }
    public int Keys { get; set; }
    public int Potions { get; set; }
    public double Walk { get; set; }
    public const double Radius = 0.38;
}

public sealed class Monster
{
    public required MonsterKind Kind { get; init; }
    public Vec Pos { get; set; }
    public int Health { get; set; }
    /// <summary>How strong it is (from the generator that made it): its damage goes up with it.</summary>
    public int Strength { get; init; } = 1;
    public double Reload { get; set; }
    public double Age { get; set; }
    /// <summary>A sorcerer, faded from sight (and from harm).</summary>
    public bool Hidden { get; set; }
    /// <summary>How much health Death has drained so far.</summary>
    public int Drained { get; set; }
    public bool Dead { get; set; }
    public Vec Facing { get; set; } = new(0, 1);
    /// <summary>The generator that made it (each keeps only so many about at once).</summary>
    public Generator? From { get; init; }
    public const double Radius = 0.36;
}

public sealed class Generator
{
    public required MonsterKind Kind { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public int Level { get; set; }
    public double Clock { get; set; }
    public bool Dead => Level <= 0;
    public Vec Centre => new(X + 0.5, Y + 0.5);
}

public sealed class Item
{
    public required ItemKind Kind { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public bool Taken { get; set; }
    public Vec Centre => new(X + 0.5, Y + 0.5);
}

/// <summary>A shot: the hero's, or a monster's (a demon's fire, a lobber's rock).</summary>
public sealed class Shot
{
    public Vec Pos { get; set; }
    public Vec Vel { get; set; }
    public bool Hostile { get; init; }
    /// <summary>A lobbed rock goes over the walls, landing where it was aimed.</summary>
    public bool Lobbed { get; init; }
    public Vec Target { get; init; }
    public int Damage { get; init; }
    public double Age { get; set; }
    public bool Dead { get; set; }
}

public enum EffectKind { Score, Blast, Magic, Sparkle }

public sealed class Effect
{
    public required EffectKind Kind { get; init; }
    public Vec Pos { get; set; }
    public double Age { get; set; }
    public double Life { get; init; } = 1;
    public string Text { get; init; } = "";
    public bool Done => Age >= Life;
}

/// <summary>
/// The controls for one step: the stick (each axis −1, 0 or 1), fire (the hero stands and shoots the
/// way the stick points, or the way it faces), and magic (a potion).
/// </summary>
public record struct Controls(int Dx, int Dy, bool Fire = false, bool Magic = false);
