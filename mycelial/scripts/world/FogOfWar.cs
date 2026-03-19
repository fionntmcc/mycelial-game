namespace Mycorrhiza.World;

using Godot;
using System.Collections.Generic;
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
    [Export(PropertyHint.Range, "0,1,0.01")] public float HiddenFogAlpha = 1.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float GradientInnerAlpha = 0.22f;
    [Export(PropertyHint.Range, "0,12,1")] public int GradientWidthTiles = 3;
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
        var visibleTiles = new HashSet<long>();

        // Pass 1: cache visibility for every tile in view.
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (IsTileVisible(x, y, headX, headY, visionSq, airPocketSightSq, hasAirPocketSight))
                    visibleTiles.Add(PackCoords(x, y));
            }
        }

        float hiddenAlpha = Mathf.Clamp(HiddenFogAlpha, 0f, 1f);
        float edgeAlpha = Mathf.Clamp(GradientInnerAlpha, 0f, hiddenAlpha);
        int gradientWidth = System.Math.Max(0, GradientWidthTiles);

        // Pass 2: draw fog where tiles are not visible.
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (visibleTiles.Contains(PackCoords(x, y)))
                    continue;

                float alpha = hiddenAlpha;
                if (gradientWidth > 0)
                {
                    int nearestVisibleDist = DistanceToNearestVisibleTile(
                        x,
                        y,
                        minX,
                        minY,
                        maxX,
                        maxY,
                        gradientWidth,
                        visibleTiles
                    );

                    if (nearestVisibleDist <= gradientWidth)
                    {
                        float t = nearestVisibleDist / (float)gradientWidth;
                        alpha = Mathf.Lerp(edgeAlpha, hiddenAlpha, t);
                    }
                }

                Color drawColor = new Color(FogColor.R, FogColor.G, FogColor.B, alpha);
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

    private bool IsTileVisible(int x, int y, int headX, int headY, int visionSq, int airPocketSightSq, bool hasAirPocketSight)
    {
        int dx = x - headX;
        int dy = y - headY;
        int distSq = dx * dx + dy * dy;

        if (distSq <= visionSq)
            return true;

        if (hasAirPocketSight && distSq <= airPocketSightSq)
            return HasLineOfSight(headX, headY, x, y);

        return false;
    }

    private static int DistanceToNearestVisibleTile(
        int x,
        int y,
        int minX,
        int minY,
        int maxX,
        int maxY,
        int maxDistance,
        HashSet<long> visibleTiles)
    {
        for (int radius = 1; radius <= maxDistance; radius++)
        {
            int left = x - radius;
            int right = x + radius;
            int top = y - radius;
            int bottom = y + radius;

            for (int ix = left; ix <= right; ix++)
            {
                if (top >= minY && top <= maxY && ix >= minX && ix <= maxX && visibleTiles.Contains(PackCoords(ix, top)))
                    return radius;

                if (bottom >= minY && bottom <= maxY && ix >= minX && ix <= maxX && visibleTiles.Contains(PackCoords(ix, bottom)))
                    return radius;
            }

            for (int iy = top + 1; iy <= bottom - 1; iy++)
            {
                if (left >= minX && left <= maxX && iy >= minY && iy <= maxY && visibleTiles.Contains(PackCoords(left, iy)))
                    return radius;

                if (right >= minX && right <= maxX && iy >= minY && iy <= maxY && visibleTiles.Contains(PackCoords(right, iy)))
                    return radius;
            }
        }

        return maxDistance + 1;
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

    private static long PackCoords(int x, int y)
    {
        return ((long)(x + 65536) << 20) | (long)(y + 65536);
    }
}
