# Mycorrhiza

A top-down atmospheric horror roguelite where you play as a sentient fungal network spreading through a dying forest.

**Engine:** Godot 4.3+ (.NET)  
**Language:** C#  
**Status:** Early prototype — world generation

## Setup

See [SETUP_GUIDE.md](SETUP_GUIDE.md) for full instructions.

### Quick Start

1. Install [Godot 4.3+ .NET](https://godotengine.org/download) and [.NET SDK 8.0+](https://dotnet.microsoft.com/download)
2. Clone this repo
3. Open `project.godot` in Godot
4. Click Build (Ctrl+B), then Run (F5)
5. WASD to fly, scroll to zoom

## Architecture

See [ARCHITECTURE.md](ARCHITECTURE.md) for the chunk-based world generation system design.

## Project Structure

```
scripts/
├── data/                  # Pure C# data (no Godot deps, thread-safe)
│   ├── TileType.cs        # Tile definitions + property flags
│   ├── BiomeType.cs       # Biome configs + depth ranges
│   └── ChunkData.cs       # Chunk data container
└── world/                 # Godot integration
	├── WorldConfig.cs     # Tunable constants
	├── NoiseGenerator.cs  # Noise layer configuration
	├── WorldGenerator.cs  # Procedural generation (thread-safe)
	├── ChunkRenderer.cs   # Per-chunk Godot rendering
	├── ChunkManager.cs    # Main orchestrator
	└── DebugCamera.cs     # Test camera (temporary)
```
