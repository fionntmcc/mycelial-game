namespace Mycorrhiza.World;

using Godot;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// Handles slow passive spreading of mycelium from tiles the player has already claimed.
/// This is the "territory hold" — corrupted land expands on its own, but much slower
/// than the player's active tendril movement.
///
/// Algorithm:
///   - Maintains a frontier of tiles adjacent to claimed territory
///   - Every tick, converts a small number of frontier tiles to mycelium
///   - Only spreads into organic tiles (not stone, water, etc.)
///   - Spread rate is configurable and deliberately slow
///
/// SETUP:
///   - Add as child of World node
///   - Assign ChunkManagerPath and TendrilControllerPath
/// </summary>
public partial class PassiveCorruption : Node2D
{
    [Export] public NodePath ChunkManagerPath { get; set; }
    [Export] public NodePath TendrilControllerPath { get; set; }
    [Export] public bool EnablePassiveSpread = true;

    /// <summary>Seconds between passive spread ticks.</summary>
    [Export] public float SpreadInterval = 0.2f;

    /// <summary>Max tiles to spread per tick.</summary>
    [Export] public int SpreadsPerTick = 8;

    private ChunkManager _chunkManager;
    private TendrilController _tendril;
    private float _timer;
    private readonly System.Random _rng = new();

    // Frontier for passive spread
    private readonly List<(int X, int Y)> _frontier = new();
    private readonly HashSet<long> _frontierSet = new();

    // Track what passive corruption has already processed
    private int _lastKnownClaimedCount;

    public override void _Ready()
    {
        if (ChunkManagerPath != null)
            _chunkManager = GetNode<ChunkManager>(ChunkManagerPath);
        if (TendrilControllerPath != null)
            _tendril = GetNode<TendrilController>(TendrilControllerPath);

        if (_chunkManager == null || _tendril == null)
        {
            GD.PrintErr("PassiveCorruption: Missing ChunkManager or TendrilController!");
        }

        if (!EnablePassiveSpread)
        {
            GD.Print("PassiveCorruption: Passive spread disabled. Tendril movement is the only spread source.");
        }
    }

    public override void _Process(double delta)
    {
        if (!EnablePassiveSpread) return;
        if (_chunkManager == null || _tendril == null) return;

        // Refresh frontier when player claims new tiles
        if (_tendril.ClaimedTileCount != _lastKnownClaimedCount)
        {
            RefreshFrontier();
            _lastKnownClaimedCount = _tendril.ClaimedTileCount;
        }

        _timer += (float)delta;
        if (_timer < SpreadInterval) return;
        _timer -= SpreadInterval;

        SpreadTick();
    }

    /// <summary>
    /// Scan all claimed tiles and find organic neighbors that could be corrupted.
    /// This is called when the claimed set changes (player moves).
    /// </summary>
    private void RefreshFrontier()
    {
        // We don't rebuild the whole frontier every time — just check new claimed tiles
        // For efficiency, we scan a subset each frame
        // Full rebuild is fine for now at this scale

        _frontier.Clear();
        _frontierSet.Clear();

        foreach (long key in _tendril.ClaimedTiles)
        {
            // Unpack coordinates
            int x = (int)((key >> 20) - 65536);
            int y = (int)((key & 0xFFFFF) - 65536);

            AddAdjacentToFrontier(x, y);
        }
    }

    private void AddAdjacentToFrontier(int x, int y)
    {
        // Cardinals
        TryAddToFrontier(x - 1, y);
        TryAddToFrontier(x + 1, y);
        TryAddToFrontier(x, y - 1);
        TryAddToFrontier(x, y + 1);

        // Diagonals so corruption flows across corner-connected grass too
        TryAddToFrontier(x - 1, y - 1);
        TryAddToFrontier(x + 1, y - 1);
        TryAddToFrontier(x - 1, y + 1);
        TryAddToFrontier(x + 1, y + 1);
    }

    private void TryAddToFrontier(int x, int y)
    {
        long key = TendrilController.PackCoords(x, y);

        // Skip if already in frontier or already claimed
        if (_frontierSet.Contains(key)) return;
        if (_tendril.ClaimedTiles.Contains(key)) return;

        TileType tile = _chunkManager.GetTileAt(x, y);

        // Skip if already infected
        if (TileProperties.IsMycelium(tile)) return;

        // Grass-only conversion rule: only normal grass can be added.
        if (!TileProperties.IsGrass(tile)) return;

        // Candidate grass must touch existing corruption (mycelium or infected grass).
        if (!HasAdjacentCorruption(x, y)) return;

        _frontierSet.Add(key);
        _frontier.Add((x, y));
    }

    private bool HasAdjacentCorruption(int x, int y)
    {
        return TileProperties.IsMycelium(_chunkManager.GetTileAt(x - 1, y))
            || TileProperties.IsMycelium(_chunkManager.GetTileAt(x + 1, y))
            || TileProperties.IsMycelium(_chunkManager.GetTileAt(x, y - 1))
            || TileProperties.IsMycelium(_chunkManager.GetTileAt(x, y + 1))
            || TileProperties.IsMycelium(_chunkManager.GetTileAt(x - 1, y - 1))
            || TileProperties.IsMycelium(_chunkManager.GetTileAt(x + 1, y - 1))
            || TileProperties.IsMycelium(_chunkManager.GetTileAt(x - 1, y + 1))
            || TileProperties.IsMycelium(_chunkManager.GetTileAt(x + 1, y + 1));
    }

    private void SpreadTick()
    {
        if (_frontier.Count == 0) return;

        int count = System.Math.Min(SpreadsPerTick, _frontier.Count);

        for (int i = 0; i < count; i++)
        {
            if (_frontier.Count == 0) break;

            // Pick a random frontier tile
            int idx = _rng.Next(_frontier.Count);
            var (x, y) = _frontier[idx];

            // Swap-remove
            int lastIdx = _frontier.Count - 1;
            _frontier[idx] = _frontier[lastIdx];
            _frontier.RemoveAt(lastIdx);
            _frontierSet.Remove(TendrilController.PackCoords(x, y));

            // Verify tile is still valid
            TileType tile = _chunkManager.GetTileAt(x, y);
            if (TileProperties.IsMycelium(tile)) continue;
            if (!TileProperties.IsGrass(tile)) continue;
            if (!HasAdjacentCorruption(x, y)) continue;

            // Terrain-aware corruption: grass becomes infected grass, dirt becomes infected dirt
            TileType infectedTile = TileProperties.GetInfectedVariant(tile);
            _chunkManager.SetTileAt(x, y, infectedTile);
            _tendril.ClaimedTiles.Add(TendrilController.PackCoords(x, y));

            // Add its neighbors to frontier
            AddAdjacentToFrontier(x, y);
        }
    }
}
