# Mycorrhiza — Godot Project Setup Guide

## Prerequisites

1. **Godot Engine 4.3+ (.NET version)**
   - Download from: https://godotengine.org/download
   - IMPORTANT: Get the **.NET** version (not standard). It has "Mono" or ".NET" in the filename
   - The .NET version is required for C# support

2. **.NET SDK 8.0+**
   - Download from: https://dotnet.microsoft.com/download
   - Verify: run `dotnet --version` in terminal

## Project Setup

### Step 1: Create the Godot Project

1. Open Godot, click "New Project"
2. Name it "Mycorrhiza", choose a folder
3. Renderer: **Forward+** (or Compatibility for lower-end testing)
4. Click "Create & Edit"

### Step 2: Enable C#

1. Go to **Project → Project Settings → General**
2. Search for "dotnet" — it should already be available in the .NET version
3. Create an initial C# script to trigger .csproj generation:
   - Right-click in FileSystem → "New Script"
   - Language: C#, name it anything (e.g., "Init.cs")
   - This generates the .csproj file. You can delete Init.cs after

### Step 3: Copy Script Files

Copy the entire `scripts/` folder from this package into your Godot project's `res://` directory:

```
res://
├── scripts/
│   ├── data/
│   │   ├── TileType.cs
│   │   ├── BiomeType.cs
│   │   └── ChunkData.cs
│   └── world/
│       ├── WorldConfig.cs
│       ├── NoiseGenerator.cs
│       ├── WorldGenerator.cs
│       ├── ChunkRenderer.cs
│       ├── ChunkManager.cs
│       └── DebugCamera.cs
```

### Step 4: Create a Placeholder TileSet

Until you have real art, create a simple debug TileSet:

1. Create a **16×16 pixel PNG** with colored squares in a grid
   - Each square represents a tile type
   - Use different colors: brown for dirt, gray for stone, blue for water, etc.
   - Make it at least 16 columns wide and 16 rows tall (256 tile slots)

2. In Godot:
   - Import the PNG into your project
   - Create a new **TileSet** resource (right-click in FileSystem → New Resource → TileSet)
   - Set tile size to **16×16**
   - Add your PNG as an **Atlas Source** (click "+" in TileSet editor → "Atlas")
   - Save the TileSet as `res://tileset.tres`

**Quick Debug Tileset Alternative:**
If you want to skip art entirely for now, you can modify `ChunkRenderer.cs` to use
colored rectangles instead of atlas tiles. But the TileMapLayer approach is what
you'll use in production, so it's worth setting up.

### Step 5: Create the Scene

1. Create a new **2D Scene** (Node2D as root)
2. Rename root to "World"

3. Add children:
   ```
   World (Node2D)
   ├── Camera2D                    (attach DebugCamera.cs)
   └── ChunkManager (Node2D)       (attach ChunkManager.cs)
   ```

4. Configure **ChunkManager** in the Inspector:
   - **Camera Path**: drag the Camera2D node into this field
   - **Tile Set Resource**: drag your `tileset.tres` into this field

5. Configure **Camera2D**:
   - Enable "Current" (make it the active camera)
   - The DebugCamera script handles position and zoom

6. Save scene as `res://scenes/world.tscn`
7. Set it as the main scene: **Project → Project Settings → General → Run → Main Scene**

### Step 6: Build and Run

1. Click the **Build** button (hammer icon) or press Ctrl+B
2. Fix any compilation errors (usually namespace issues or missing references)
3. Press **F5** to run

### What You Should See

- The camera starts at the world surface, center of the map
- Chunks generate around the camera as colored tile blocks
- **WASD** to fly around, **scroll** to zoom
- Moving down reveals deeper biomes with different tile colors
- Chunks load ahead of you and unload behind you
- The console prints the world seed and size on startup

## Troubleshooting

**"No camera assigned" error:**
- Make sure the CameraPath property on ChunkManager points to your Camera2D node

**"No TileSet assigned" error:**
- Create a TileSet resource and assign it in the ChunkManager inspector

**Nothing renders / black screen:**
- Check that Camera2D has "Current" enabled
- Check that ChunkManager is actually creating child nodes (look in the Scene tree during play)
- Make sure your TileSet has at least one atlas source with valid tile IDs

**Compilation errors:**
- Ensure you're using the .NET version of Godot
- Ensure .NET SDK 8.0+ is installed
- Check that namespace declarations match the folder structure

**Performance issues:**
- If chunks stutter when loading, reduce `MaxChunksPerFrame` in WorldConfig
- If too many chunks are loaded, reduce `LoadRadiusChunks`
- In production, you'll want to profile and optimize the generation passes

## Architecture Notes for Development

### Adding New Tile Types
1. Add the enum value in `TileType.cs`
2. Set its properties in `TileProperties` static constructor
3. Add it to the appropriate biome in `BiomeType.cs`
4. Reference it in `WorldGenerator.cs` generation passes
5. Add the tile art to your TileSet atlas at the matching position

### Adding New Biomes
1. Add the enum value in `BiomeType.cs`
2. Create a `BiomeConfig` entry in `BiomeRegistry.Biomes`
3. Add depth boundaries in `WorldConfig.cs`
4. Add biome-specific generation logic in `WorldGenerator.cs` accent pass

### Mycelium Spreading (Next System to Build)
The world is ready for runtime tile modification. Call:
```csharp
chunkManager.SetTileAt(worldX, worldY, TileType.Mycelium);
```
This updates both the data and the visual immediately. Your mycelium growth
system will use this API to spread through organic tiles each cycle.

### Save/Load
ChunkData is a simple flat array. To save:
1. Iterate all entries in `_chunkCache`
2. Serialize each ChunkData's tile array + coordinates
3. Save the world seed

To load:
1. Restore the seed to `NoiseGenerator`
2. Pre-populate `_chunkCache` with saved chunk data
3. ChunkManager will use cached data instead of regenerating
