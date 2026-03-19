namespace Mycorrhiza.World;

using Godot;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mycorrhiza.Data;

/// <summary>
/// Main orchestrator for the chunk-based world system.
/// Attach this node to your scene. It manages:
///   - Tracking the camera/viewport position
///   - Deciding which chunks need to be loaded or unloaded
///   - Dispatching chunk generation to background threads
///   - Creating/destroying ChunkRenderer nodes on the main thread
///
/// SETUP:
///   1. Create a Node2D in your scene, attach this script
///   2. Assign a Camera2D node to the CameraPath export
///   3. Create a TileSet resource with your tile atlas, assign to TileSetResource
///   4. Hit Play — the world generates around the camera
///
/// The ChunkManager runs its logic every frame in _Process().
/// </summary>
public partial class ChunkManager : Node2D
{
	[Export] public NodePath CameraPath { get; set; }
	[Export] public TileSet TileSetResource { get; set; }

	// --- State ---
	private Camera2D _camera;
	private NoiseGenerator _noise;
	private WorldGenerator _generator;

	// Loaded chunks: key = packed chunk coords (ChunkX, ChunkY)
	private readonly Dictionary<long, ChunkRenderer> _loadedChunks = new();

	// Chunk data cache: keeps generated data even after renderer is freed
	private readonly Dictionary<long, ChunkData> _chunkCache = new();

	// Thread-safe queues for communication between main thread and background
	private readonly ConcurrentQueue<ChunkData> _readyQueue = new();
	private readonly HashSet<long> _pendingGeneration = new();

	// Cancellation for cleanup
	private CancellationTokenSource _cts;

	// --- Lifecycle ---

	public override void _Ready()
	{
		// Get camera reference
		if (CameraPath != null)
			_camera = GetNode<Camera2D>(CameraPath);

		if (_camera == null)
		{
			GD.PrintErr("ChunkManager: No camera assigned! Assign CameraPath in the inspector.");
			return;
		}

		if (TileSetResource == null)
		{
			GD.PrintErr("ChunkManager: No TileSet assigned! Create and assign a TileSet resource.");
			return;
		}

		// Initialize generation systems
		_noise = new NoiseGenerator(WorldConfig.TerrainSeed);
		_generator = new WorldGenerator(_noise);
		_cts = new CancellationTokenSource();

		GD.Print($"ChunkManager initialized. Seed: {_noise.Seed}");
		GD.Print($"World size: {WorldConfig.WorldWidthTiles}x{WorldConfig.WorldDepthTiles} tiles " +
				 $"({WorldConfig.WorldWidthChunks}x{WorldConfig.WorldDepthChunks} chunks)");
	}

	public override void _Process(double delta)
	{
		if (_camera == null || TileSetResource == null) return;

		// 1. Determine which chunks should be loaded based on camera
		var visibleRange = GetVisibleChunkRange();

		// 2. Request generation for chunks that aren't loaded or pending
		RequestMissingChunks(visibleRange);

		// 3. Process completed chunks from the background thread
		ProcessReadyChunks();

		// 4. Unload chunks that are too far from the camera
		UnloadDistantChunks(visibleRange);

		// 5. Update dirty chunks (runtime tile changes like mycelium spread)
		UpdateDirtyChunks();
	}

	public override void _ExitTree()
	{
		_cts?.Cancel();
		_cts?.Dispose();
	}

	// --- Core Logic ---

	/// <summary>
	/// Calculate the range of chunks that should be loaded, based on camera viewport.
	/// Returns (minChunkX, minChunkY, maxChunkX, maxChunkY) inclusive.
	/// </summary>
	private (int minX, int minY, int maxX, int maxY) GetVisibleChunkRange()
	{
		// Get the viewport size in pixels
		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		Vector2 cameraPos = _camera.GlobalPosition;
		float zoom = _camera.Zoom.X; // Assuming uniform zoom

		// Calculate visible world area in pixels
		float halfWidth = (viewportSize.X / zoom) / 2.0f;
		float halfHeight = (viewportSize.Y / zoom) / 2.0f;

		// Convert to chunk coordinates with load radius padding
		int minCX = WorldToChunkX(cameraPos.X - halfWidth) - WorldConfig.LoadRadiusChunks;
		int minCY = WorldToChunkY(cameraPos.Y - halfHeight) - WorldConfig.LoadRadiusChunks;
		int maxCX = WorldToChunkX(cameraPos.X + halfWidth) + WorldConfig.LoadRadiusChunks;
		int maxCY = WorldToChunkY(cameraPos.Y + halfHeight) + WorldConfig.LoadRadiusChunks;

		// Clamp to world bounds
		minCX = System.Math.Max(0, minCX);
		minCY = System.Math.Max(-1, minCY); // Allow one row above surface for sky
		maxCX = System.Math.Min(WorldConfig.WorldWidthChunks - 1, maxCX);
		maxCY = System.Math.Min(WorldConfig.WorldDepthChunks - 1, maxCY);

		return (minCX, minCY, maxCX, maxCY);
	}

	/// <summary>
	/// For each chunk in the visible range that isn't loaded or pending, dispatch generation.
	/// </summary>
	private void RequestMissingChunks((int minX, int minY, int maxX, int maxY) range)
	{
		for (int cy = range.minY; cy <= range.maxY; cy++)
		{
			for (int cx = range.minX; cx <= range.maxX; cx++)
			{
				long key = PackCoords(cx, cy);

				// Skip if already loaded or generation is pending
				if (_loadedChunks.ContainsKey(key) || _pendingGeneration.Contains(key))
					continue;

				// Check if we have cached data (chunk was loaded before, then unloaded)
				if (_chunkCache.TryGetValue(key, out ChunkData cached))
				{
					// Re-create the renderer from cached data (no regeneration needed)
					_readyQueue.Enqueue(cached);
					_pendingGeneration.Add(key);
					continue;
				}

				// Dispatch generation to background thread
				_pendingGeneration.Add(key);
				int capturedCx = cx;
				int capturedCy = cy;
				var token = _cts.Token;

				Task.Run(() =>
				{
					if (token.IsCancellationRequested) return;
					ChunkData data = _generator.GenerateChunk(capturedCx, capturedCy);
					if (!token.IsCancellationRequested)
						_readyQueue.Enqueue(data);
				}, token);
			}
		}
	}

	/// <summary>
	/// Poll the ready queue and create ChunkRenderer nodes for completed chunks.
	/// Limited to MaxChunksPerFrame to prevent frame spikes.
	/// </summary>
	private void ProcessReadyChunks()
	{
		int processed = 0;

		while (processed < WorldConfig.MaxChunksPerFrame && _readyQueue.TryDequeue(out ChunkData data))
		{
			long key = PackCoords(data.ChunkX, data.ChunkY);
			_pendingGeneration.Remove(key);

			// Don't create renderer if chunk is already loaded (race condition guard)
			if (_loadedChunks.ContainsKey(key))
				continue;

			// Cache the data
			_chunkCache[key] = data;

			// Create renderer node
			var renderer = new ChunkRenderer();
			renderer.Name = $"Chunk_{data.ChunkX}_{data.ChunkY}";
			AddChild(renderer);
			renderer.Initialize(data, TileSetResource);

			_loadedChunks[key] = renderer;
			processed++;
		}
	}

	/// <summary>
	/// Free ChunkRenderer nodes for chunks that are outside the unload radius.
	/// The ChunkData is kept in cache for quick reload.
	/// </summary>
	private void UnloadDistantChunks((int minX, int minY, int maxX, int maxY) visibleRange)
	{
		// Expand visible range by unload buffer to get the keep-alive zone
		int keepMinX = visibleRange.minX - WorldConfig.UnloadRadiusChunks;
		int keepMinY = visibleRange.minY - WorldConfig.UnloadRadiusChunks;
		int keepMaxX = visibleRange.maxX + WorldConfig.UnloadRadiusChunks;
		int keepMaxY = visibleRange.maxY + WorldConfig.UnloadRadiusChunks;

		// Collect keys to remove (can't modify dict during iteration)
		var toRemove = new List<long>();

		foreach (var (key, renderer) in _loadedChunks)
		{
			int cx = renderer.ChunkX;
			int cy = renderer.ChunkY;

			if (cx < keepMinX || cx > keepMaxX || cy < keepMinY || cy > keepMaxY)
			{
				toRemove.Add(key);
			}
		}

		foreach (long key in toRemove)
		{
			if (_loadedChunks.TryGetValue(key, out ChunkRenderer renderer))
			{
				renderer.QueueFree(); // Godot will free the node next frame
				_loadedChunks.Remove(key);
				// NOTE: Data stays in _chunkCache for fast reload
			}
		}

		// Optional: Evict very old cache entries to save memory
		// For now, we keep everything. For a large world you'd want an LRU cache.
	}

	/// <summary>
	/// Re-render chunks whose data has been modified at runtime.
	/// This handles mycelium growth, mining, infection spreading, etc.
	/// </summary>
	private void UpdateDirtyChunks()
	{
		foreach (var (_, renderer) in _loadedChunks)
		{
			renderer.ApplyDirtyTiles();
		}
	}

	// --- Public API (for other systems to interact with the world) ---

	/// <summary>
	/// Get the tile at a world tile coordinate. Returns Air if chunk not loaded.
	/// </summary>
	public TileType GetTileAt(int worldTileX, int worldTileY)
	{
		var (cx, cy, lx, ly) = ChunkData.WorldToLocal(worldTileX, worldTileY);
		long key = PackCoords(cx, cy);

		if (_chunkCache.TryGetValue(key, out ChunkData data))
			return data.GetTile(lx, ly);

		return TileType.Air; // Unloaded chunks treated as air
	}

	/// <summary>
	/// Set a tile at a world tile coordinate.
	/// If the chunk is loaded (has a renderer), the visual updates immediately.
	/// If the chunk is only cached, the data updates and will render when reloaded.
	/// </summary>
	public void SetTileAt(int worldTileX, int worldTileY, TileType type)
	{
		var (cx, cy, lx, ly) = ChunkData.WorldToLocal(worldTileX, worldTileY);
		long key = PackCoords(cx, cy);

		// Update renderer directly if chunk is visible
		if (_loadedChunks.TryGetValue(key, out ChunkRenderer renderer))
		{
			renderer.SetTileAt(lx, ly, type);
			return;
		}

		// Otherwise update cached data
		if (_chunkCache.TryGetValue(key, out ChunkData data))
		{
			data.SetTile(lx, ly, type);
		}
	}

	/// <summary>
	/// Get the biome at a world tile coordinate.
	/// </summary>
	public BiomeType GetBiomeAt(int worldTileX, int worldTileY)
	{
		if (worldTileY < 0) return BiomeType.Sky;
		return _noise.GetBiomeAt(worldTileX, worldTileY).Type;
	}

	/// <summary>
	/// Get the world seed (useful for saving/loading).
	/// </summary>
	public int GetWorldSeed() => _noise?.Seed ?? 0;

	/// <summary>
	/// Get the origin tree's root tip positions (where mycelium starts).
	/// </summary>
	public System.Collections.Generic.List<(int X, int Y)> GetRootTipPositions()
		=> _generator?.OriginTree?.RootTipPositions ?? new();

	// --- Utility ---

	private static int WorldToChunkX(float worldPixelX)
		=> (int)Mathf.Floor(worldPixelX / WorldConfig.ChunkPixelSize);

	private static int WorldToChunkY(float worldPixelY)
		=> (int)Mathf.Floor(worldPixelY / WorldConfig.ChunkPixelSize);

	/// <summary>
	/// Pack two chunk coordinates into a single long for use as dictionary key.
	/// Supports negative coordinates (for chunks above surface).
	/// </summary>
	private static long PackCoords(int cx, int cy)
		=> ((long)(cx + 32768) << 16) | (long)(cy + 32768);
}
