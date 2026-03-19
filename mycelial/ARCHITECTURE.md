# Mycorrhiza — World Generation Architecture

## Overview

The world is a 2D tile-based grid rendered as a side-on cross-section (Terraria-style).
It is divided into **chunks** — fixed-size rectangular groups of tiles that are loaded and
unloaded dynamically based on the camera viewport.

## Key Constants (Tunable)

| Parameter | Default | Notes |
|-----------|---------|-------|
| Tile Size | 16×16 px | Size of one tile in pixels |
| Chunk Size | 32×32 tiles | 512×512 px per chunk |
| World Width | 2048 tiles | 64 chunks wide (~32,768 px) |
| World Depth | 1280 tiles | 40 chunks deep (~20,480 px) |
| Load Radius | 3 chunks | Chunks loaded beyond viewport edge |
| Unload Radius | 5 chunks | Chunks unloaded beyond this distance |

## Architecture Layers

```
┌─────────────────────────────────────────────┐
│  ChunkManager (Node2D)                      │
│  - Tracks camera position                   │
│  - Decides which chunks to load/unload      │
│  - Manages chunk node lifecycle             │
├─────────────────────────────────────────────┤
│  WorldGenerator (static/threaded)           │
│  - Noise-based terrain generation           │
│  - Biome selection by depth + noise         │
│  - Cave carving (cellular automata)         │
│  - Ore/resource placement                   │
├─────────────────────────────────────────────┤
│  ChunkData (pure data, no Godot deps)       │
│  - 32×32 array of TileType                  │
│  - Generated on background thread           │
│  - Serializable for save/load               │
├─────────────────────────────────────────────┤
│  ChunkRenderer (Node2D per chunk)           │
│  - Reads ChunkData                          │
│  - Sets TileMapLayer cells                  │
│  - Created/destroyed on main thread         │
├─────────────────────────────────────────────┤
│  TileRegistry (static resource)             │
│  - Maps TileType enum → TileSet atlas IDs  │
│  - Biome definitions and depth ranges       │
│  - Tile properties (solid, liquid, etc.)    │
└─────────────────────────────────────────────┘
```

## Threading Model

```
Main Thread                    Background Thread(s)
───────────                    ────────────────────
ChunkManager._Process()
  │
  ├─ Calculate visible chunks
  ├─ Queue chunks needing generation ──→  WorldGenerator.GenerateChunk()
  │                                         │
  │                                         ├─ Sample noise for terrain
  │                                         ├─ Determine biomes
  │                                         ├─ Carve caves
  │                                         ├─ Place resources
  │                                         └─ Return ChunkData ──→ ready queue
  │
  ├─ Poll ready queue (lock-free)
  ├─ Create ChunkRenderer nodes for ready chunks
  ├─ Unload distant chunks (free nodes, keep data in cache)
  └─ Done
```

**Critical rule:** Godot nodes can only be created/modified on the main thread.
ChunkData is pure C# data with no Godot dependencies, so it can be generated
on any thread. The ChunkRenderer nodes are created on main thread only.

## Chunk Lifecycle

```
UNLOADED ──→ GENERATING ──→ READY ──→ LOADED ──→ CACHED ──→ UNLOADED
                (bg thread)    (data ready,    (node active,   (node freed,
                               no node yet)    rendering)      data in memory)
```

## File Map

```
scripts/
├── data/
│   ├── TileType.cs          — Enum of all tile types
│   ├── BiomeType.cs          — Enum + depth range definitions
│   └── ChunkData.cs          — Pure data container for one chunk
├── world/
│   ├── WorldConfig.cs        — All tunable constants
│   ├── WorldGenerator.cs     — Procedural generation (thread-safe)
│   ├── NoiseGenerator.cs     — Wrapper around FastNoiseLite
│   ├── CaveCarver.cs         — Cellular automata cave generation
│   ├── ChunkRenderer.cs      — Godot node that renders a chunk
│   └── ChunkManager.cs       — Main orchestrator (attach to scene)
```
