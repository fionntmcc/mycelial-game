namespace Mycorrhiza.World;

using Godot;
using Mycorrhiza.Data;

/// <summary>
/// Tile-based fog of war overlay with cave line-of-sight behavior.
///
/// - By default, only a central vision circle around the tendril is visible.
/// - When the tendril touches an air pocket, line-of-sight visibility extends
///   beyond that circle until blocked by solid terrain.
/// </summary>
public partial class FogOfWar : Node2D
{
    [Export] public NodePath TendrilControllerPath { get; set; }
    [Export] public NodePath CameraPath { get; set; }
    [Export] public NodePath ChunkManagerPath { get; set; }

    [Export(PropertyHint.Range, "1,48,1")] public int VisionRadiusTiles = 9;
    [Export(PropertyHint.Range, "1,96,1")] public int AirPocketSightRadiusTiles = 28;
    [Export(PropertyHint.Range, "1,3,1")] public int AirPocketContactRadius = 1;
    [Export(PropertyHint.Range, "0,1,0.01")] public float UnexploredAlpha = 0.9f;
    [Export] public Color FogColor = new Color(0f, 0f, 0f, 1f);

    private TendrilController _tendril;
    private Camera2D _camera;
    private ChunkManager _chunkManager;

    public override void _Ready()
    {
        if (TendrilControllerPath != null)
            _tendril = GetNode<TendrilController>(TendrilControllerPath);

        if (CameraPath != null)
            _camera = GetNode<Camera2D>(CameraPath);

        if (ChunkManagerPath != null)
            _chunkManager = GetNode<ChunkManager>(ChunkManagerPath);

        if (_tendril == null || _camera == null || _chunkManager == null)
        {
            GD.PrintErr("FogOfWar: Missing TendrilController, Camera2D, or ChunkManager path.");
            SetProcess(false);
            return;
        }

        ZAsRelative = false;
        ZIndex = 5000;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        // Redraw each frame so camera movement updates the overlay immediately.
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_tendril == null || _camera == null || _chunkManager == null)
            return;

        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        float zoom = Mathf.Max(0.0001f, _camera.Zoom.X);
        Vector2 cameraPos = _camera.GlobalPosition;

        float halfWidth = (viewportSize.X / zoom) * 0.5f;
        float halfHeight = (viewportSize.Y / zoom) * 0.5f;

        int minX = Mathf.FloorToInt((cameraPos.X - halfWidth) / WorldConfig.TileSize) - 1;
        int minY = Mathf.FloorToInt((cameraPos.Y - halfHeight) / WorldConfig.TileSize) - 1;
        int maxX = Mathf.CeilToInt((cameraPos.X + halfWidth) / WorldConfig.TileSize) + 1;
        int maxY = Mathf.CeilToInt((cameraPos.Y + halfHeight) / WorldConfig.TileSize) + 1;

        int headX = _tendril.HeadX;
        int headY = _tendril.HeadY;
        int visionSq = VisionRadiusTiles * VisionRadiusTiles;
        int airPocketSightSq = AirPocketSightRadiusTiles * AirPocketSightRadiusTiles;

        bool hasAirPocketSight = IsInAirPocket(headX, headY);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int dx = x - headX;
                int dy = y - headY;
                int distSq = dx * dx + dy * dy;

                // Always reveal the central circular vision area around the tendril.
                if (distSq <= visionSq)
                    continue;

                bool visibleByAirPocketSight = false;
                if (hasAirPocketSight && distSq <= airPocketSightSq)
                    visibleByAirPocketSight = HasLineOfSight(headX, headY, x, y);

                if (visibleByAirPocketSight)
                    continue;

                Color drawColor = new Color(FogColor.R, FogColor.G, FogColor.B, Mathf.Clamp(UnexploredAlpha, 0f, 1f));
                Rect2 tileRect = new Rect2(
                    x * WorldConfig.TileSize,
                    y * WorldConfig.TileSize,
                    WorldConfig.TileSize,
                    WorldConfig.TileSize
                );

                DrawRect(tileRect, drawColor, true);
            }
        }
    }

    private bool IsInAirPocket(int centerX, int centerY)
    {
        for (int dy = -AirPocketContactRadius; dy <= AirPocketContactRadius; dy++)
        {
            for (int dx = -AirPocketContactRadius; dx <= AirPocketContactRadius; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                TileType tile = _chunkManager.GetTileAt(centerX + dx, centerY + dy);
                if (tile == TileType.Air)
                    return true;
            }
        }

        return _chunkManager.GetTileAt(centerX, centerY) == TileType.Air;
    }

    private bool HasLineOfSight(int startX, int startY, int targetX, int targetY)
    {
        int x = startX;
        int y = startY;

        int dx = Mathf.Abs(targetX - startX);
        int sx = startX < targetX ? 1 : -1;
        int dy = Mathf.Abs(targetY - startY);
        int sy = startY < targetY ? 1 : -1;

        int err = dx - dy;

        while (x != targetX || y != targetY)
        {
            int e2 = err * 2;

            if (e2 > -dy)
            {
                err -= dy;
                x += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y += sy;
            }

            // Target tile itself can be visible; blockers are only between head and target.
            if (x == targetX && y == targetY)
                return true;

            if (IsSightBlockingTile(x, y))
                return false;
        }

        return true;
    }

    private bool IsSightBlockingTile(int x, int y)
    {
        TileType tile = _chunkManager.GetTileAt(x, y);
        return TileProperties.Is(tile, TileFlags.Solid);
    }
}
