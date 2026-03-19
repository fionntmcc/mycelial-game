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

    /// <summary>Seconds between passive spread ticks.</summary>
    [Export] public float SpreadInterval = 3.0f;

    /// <summary>Max tiles to spread per tick.</summary>
    [Export] public int SpreadsPerTick = 1;

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
    }

    public override void _Process(double delta)
    {
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

            // Check 4 neighbors
            TryAddToFrontier(x - 1, y);
            TryAddToFrontier(x + 1, y);
            TryAddToFrontier(x, y - 1);
            TryAddToFrontier(x, y + 1);
        }
    }

    private void TryAddToFrontier(int x, int y)
    {
        long key = TendrilController.PackCoords(x, y);

        // Skip if already in frontier or already claimed
        if (_frontierSet.Contains(key)) return;
        if (_tendril.ClaimedTiles.Contains(key)) return;

        TileType tile = _chunkManager.GetTileAt(x, y);

        // Only spread into organic/soft tiles
        if (TileProperties.Is(tile, TileFlags.Organic) || tile == TileType.Dirt
            || tile == TileType.Topsoil || tile == TileType.Sand)
        {
            _frontierSet.Add(key);
            _frontier.Add((x, y));
        }
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
            if (tile == TileType.Mycelium || tile == TileType.MyceliumDense
                || tile == TileType.MyceliumDark || tile == TileType.MyceliumCore)
                continue;
            if (tile == TileType.Air) continue;
            if (!TileProperties.Is(tile, TileFlags.Organic) && tile != TileType.Dirt
                && tile != TileType.Topsoil && tile != TileType.Sand)
                continue;

            // Corrupt it
            _chunkManager.SetTileAt(x, y, TileType.Mycelium);
            _tendril.ClaimedTiles.Add(TendrilController.PackCoords(x, y));

            // Add its neighbors to frontier
            TryAddToFrontier(x - 1, y);
            TryAddToFrontier(x + 1, y);
            TryAddToFrontier(x, y - 1);
            TryAddToFrontier(x, y + 1);
        }
    }
}
