namespace Mycorrhiza.Data;

using Godot;

/// <summary>
/// Defines the physical shape of a creature on the sub-grid.
///
/// A body is a small set of sub-cell offsets relative to the creature's origin.
/// This is both the visual footprint AND the collision hitbox — they are the same
/// thing, just like the tendril's blob.
///
/// At 4px per sub-cell, a 5-cell-wide earthworm is 20px across. Small but visible,
/// and pixel-perfect collisions with the tendril come for free.
///
/// Bodies are immutable once created. Animation works by swapping between
/// different CreatureBody instances (see CreatureBodySet).
/// </summary>
public class CreatureBody
{
    /// <summary>
    /// Offsets from the creature's origin (SubX, SubY).
    /// (0,0) is the center cell. Negative offsets go up/left.
    /// </summary>
    public readonly (int DX, int DY)[] Cells;

    /// <summary>
    /// Color per cell, same length as Cells.
    /// Index-matched: Colors[i] is the color for Cells[i].
    /// </summary>
    public readonly Color[] Colors;

    /// <summary>
    /// Bounding radius in sub-cells for broad-phase collision.
    /// Should be >= the farthest cell from origin.
    /// </summary>
    public readonly int Radius;

    /// <summary>
    /// Number of cells in this body. Cached for convenience.
    /// </summary>
    public int CellCount => Cells.Length;

    public CreatureBody((int DX, int DY)[] cells, Color[] colors, int radius)
    {
        Cells = cells;
        Colors = colors;
        Radius = radius;
    }
}

/// <summary>
/// A set of body frames for creature animation.
///
/// Idle creatures display frame 0. Moving creatures cycle through all frames.
/// A worm's walk cycle might be 3 frames where cells shift slightly,
/// creating a wiggle. A beetle might alternate leg positions.
/// </summary>
public class CreatureBodySet
{
    public readonly CreatureBody[] Frames;

    /// <summary>Seconds per animation frame.</summary>
    public readonly float FrameDuration;

    public CreatureBodySet(CreatureBody[] frames, float frameDuration = 0.15f)
    {
        Frames = frames;
        FrameDuration = frameDuration;
    }

    /// <summary>Single-frame body (no animation).</summary>
    public CreatureBodySet(CreatureBody singleFrame)
    {
        Frames = new[] { singleFrame };
        FrameDuration = 1f;
    }

    public CreatureBody GetFrame(float animTime)
    {
        if (Frames.Length == 1) return Frames[0];
        int idx = ((int)(animTime / FrameDuration)) % Frames.Length;
        return Frames[idx];
    }

    /// <summary>Get the idle (first) frame.</summary>
    public CreatureBody Idle => Frames[0];
}
