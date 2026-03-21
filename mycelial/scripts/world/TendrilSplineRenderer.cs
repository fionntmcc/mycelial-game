namespace Mycorrhiza.World;

using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Renders the tendril body as a smooth spline trail behind the head.
///
/// Records the head's position history as control points, generates a Catmull-Rom
/// spline through them, and renders via Line2D with:
///   - Variable width: thick at base, tapering toward the tip
///   - Organic pulse tied to movement speed
///   - Perlin noise micro-wobble (visual only — no physics effect)
///   - Color gradient from old (earthy) to new (fungal)
///
/// This is purely visual. The actual head position for gameplay lives in TendrilHead.
/// Tile infection is handled by TendrilInfection.
///
/// SETUP:
///   - Add as child/sibling of TendrilHead
///   - Creates its own Line2D child node automatically
///   - Call RecordPoint() and UpdateRenderer() each frame from TendrilController
/// </summary>
public partial class TendrilSplineRenderer : Node2D
{
	// =========================================================================
	//  CONFIGURATION
	// =========================================================================

	[ExportGroup("Trail Shape")]
	[Export] public int MaxControlPoints = 250;
	[Export] public float MinPointDistance = 4f;
	[Export] public int SplineSubdivisions = 4;

	[ExportGroup("Width & Taper")]
	[Export] public float BaseWidth = 8f;
	[Export] public float TipWidth = 3f;
	[Export] public float TaperStart = 0.3f;

	[ExportGroup("Pulse Effect")]
	[Export] public bool EnablePulse = true;
	[Export] public float PulseAmplitude = 0.12f;
	[Export] public float PulseBaseFrequency = 1.5f;
	[Export] public float PulseSpeedScale = 0.006f;
	[Export] public float PulseSpatialFrequency = 3f;

	[ExportGroup("Noise Wobble")]
	[Export] public bool EnableNoiseWobble = true;
	[Export] public float NoiseAmplitude = 2.5f;
	[Export] public float NoiseScrollSpeed = 0.8f;
	[Export] public float NoiseSpatialScale = 0.04f;
	[Export] public float NoiseTipDetailScale = 0.12f;
	[Export] public float NoiseTipDetailBlend = 0.4f;

	[ExportGroup("Colors")]
	[Export] public Color TipColor = new(0.55f, 0.35f, 0.55f, 1f);
	[Export] public Color BaseColor = new(0.3f, 0.22f, 0.18f, 0.85f);

	// =========================================================================
	//  INTERNAL STATE
	// =========================================================================

	private Line2D _line;
	private readonly List<Vector2> _controlPoints = new();
	private float _time;
	private float _currentSpeed;
	private FastNoiseLite _noise;

	/// <summary>True when the trail has no points.</summary>
	public bool IsEmpty => _controlPoints.Count < 2;

	/// <summary>Number of control points currently stored.</summary>
	public int PointCount => _controlPoints.Count;

	// =========================================================================
	//  LIFECYCLE
	// =========================================================================

	public override void _Ready()
	{
		_line = new Line2D
		{
			Width = BaseWidth,
			JointMode = Line2D.LineJointMode.Round,
			BeginCapMode = Line2D.LineCapMode.Round,
			EndCapMode = Line2D.LineCapMode.Round,
			Antialiased = true,
			DefaultColor = TipColor,
			ZIndex = -1,
		};
		AddChild(_line);

		_noise = new FastNoiseLite
		{
			NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex,
			Frequency = 1f,
			Seed = (int)(GD.Randi() % 100000),
		};
	}

	// =========================================================================
	//  PUBLIC API
	// =========================================================================

	/// <summary>
	/// Record the head's current position. Only adds if moved enough from last point.
	/// </summary>
	public void RecordPoint(Vector2 headPosition, float speed)
	{
		_currentSpeed = speed;

		if (_controlPoints.Count == 0)
		{
			_controlPoints.Add(headPosition);
			return;
		}

		Vector2 last = _controlPoints[_controlPoints.Count - 1];
		if (headPosition.DistanceSquaredTo(last) >= MinPointDistance * MinPointDistance)
		{
			_controlPoints.Add(headPosition);

			while (_controlPoints.Count > MaxControlPoints)
				_controlPoints.RemoveAt(0);
		}
	}

	/// <summary>
	/// Rebuild the Line2D visual. Call every frame.
	/// </summary>
	public void UpdateRenderer(float delta, Vector2 currentHeadPosition)
	{
		_time += delta;

		if (_controlPoints.Count < 2)
		{
			_line.ClearPoints();
			return;
		}

		List<Vector2> splinePoints = GenerateSpline(currentHeadPosition);

		if (EnableNoiseWobble && splinePoints.Count > 2)
			ApplyNoiseWobble(splinePoints);

		_line.ClearPoints();
		foreach (var pt in splinePoints)
			_line.AddPoint(pt);

		_line.WidthCurve = BuildWidthCurve(splinePoints.Count);
		_line.Gradient = BuildColorGradient();
	}

	/// <summary>Clear all trail data (respawn).</summary>
	public void ClearTrail()
	{
		_controlPoints.Clear();
		_line?.ClearPoints();
	}

	/// <summary>Remove oldest N control points (retreat animation).</summary>
	public void TrimFromBase(int count)
	{
		int toRemove = Math.Min(count, _controlPoints.Count);
		if (toRemove > 0)
			_controlPoints.RemoveRange(0, toRemove);
	}

	/// <summary>Get the oldest recorded position (for retreat targeting).</summary>
	public Vector2? GetBasePosition()
	{
		return _controlPoints.Count > 0 ? _controlPoints[0] : null;
	}

	// =========================================================================
	//  SPLINE GENERATION
	// =========================================================================

	private List<Vector2> GenerateSpline(Vector2 headPos)
	{
		var result = new List<Vector2>();

		// Ensure current head position is the final point
		var points = new List<Vector2>(_controlPoints);
		if (points.Count > 0 && points[points.Count - 1].DistanceSquaredTo(headPos) > 1f)
			points.Add(headPos);

		if (points.Count < 2)
			return result;

		for (int i = 0; i < points.Count - 1; i++)
		{
			Vector2 p0 = (i > 0) ? points[i - 1] : points[0] * 2 - points[1];
			Vector2 p1 = points[i];
			Vector2 p2 = points[i + 1];
			Vector2 p3 = (i + 2 < points.Count)
				? points[i + 2]
				: points[points.Count - 1] * 2 - points[points.Count - 2];

			for (int s = 0; s < SplineSubdivisions; s++)
			{
				float t = s / (float)SplineSubdivisions;
				result.Add(CatmullRom(p0, p1, p2, p3, t));
			}
		}

		// Final point
		if (points.Count > 0)
			result.Add(points[points.Count - 1]);

		return result;
	}

	private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
	{
		float t2 = t * t;
		float t3 = t2 * t;
		return 0.5f * (
			(2f * p1) +
			(-p0 + p2) * t +
			(2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
			(-p0 + 3f * p1 - 3f * p2 + p3) * t3
		);
	}

	// =========================================================================
	//  NOISE WOBBLE
	// =========================================================================

	private void ApplyNoiseWobble(List<Vector2> points)
	{
		for (int i = 1; i < points.Count - 1; i++)
		{
			float tAlongTrail = i / (float)(points.Count - 1);

			Vector2 tangent = (points[i + 1] - points[i - 1]).Normalized();
			Vector2 normal = new(-tangent.Y, tangent.X);

			float baseN = _noise.GetNoise2D(
				i * NoiseSpatialScale * 100f,
				_time * NoiseScrollSpeed * 100f
			);

			float tipN = _noise.GetNoise2D(
				i * NoiseTipDetailScale * 100f,
				_time * NoiseScrollSpeed * 200f + 1000f
			);

			float blended = Mathf.Lerp(baseN, tipN, tAlongTrail * NoiseTipDetailBlend);
			float amplitude = NoiseAmplitude * Mathf.Lerp(0.3f, 1f, tAlongTrail);

			points[i] += normal * blended * amplitude;
		}
	}

	// =========================================================================
	//  WIDTH CURVE
	// =========================================================================

	private Curve BuildWidthCurve(int pointCount)
	{
		var curve = new Curve();
		if (pointCount < 2) return curve;

		int steps = Math.Min(pointCount, 32);

		for (int i = 0; i <= steps; i++)
		{
			float t = i / (float)steps;

			// Taper
			float taperT = t < TaperStart ? 0f : (t - TaperStart) / (1f - TaperStart);
			float width = Mathf.Lerp(BaseWidth, TipWidth, taperT);

			// Pulse
			if (EnablePulse)
			{
				float freq = PulseBaseFrequency + _currentSpeed * PulseSpeedScale;
				float pulse = Mathf.Sin(t * PulseSpatialFrequency * Mathf.Tau + _time * freq * Mathf.Tau);
				width *= (1f + pulse * PulseAmplitude);
			}

			float normalized = width / BaseWidth;
			curve.AddPoint(new Vector2(t, Mathf.Max(0.05f, normalized)));
		}

		return curve;
	}

	// =========================================================================
	//  COLOR GRADIENT
	// =========================================================================

	private Gradient BuildColorGradient()
	{
		var gradient = new Gradient();
		gradient.SetColor(0, BaseColor);
		gradient.SetColor(1, TipColor);
		gradient.AddPoint(0.6f, BaseColor.Lerp(TipColor, 0.4f));
		return gradient;
	}
}
