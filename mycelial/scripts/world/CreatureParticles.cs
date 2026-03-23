namespace Mycorrhiza.World;

using Godot;
using System;
using System.Collections.Generic;
using Mycorrhiza.Data;

/// <summary>
/// Custom particle system for creature death/consumption effects.
///
/// When a creature dies, this spawns one particle per body cell using that cell's
/// color. Particles burst outward with:
///   - Arcing trajectories (gravity pulls them down)
///   - Spin (angular velocity per particle)
///   - Scale wobble (noise-driven size pulsing)
///   - Fade-out over lifetime
///   - Size variation (not locked to sub-grid resolution)
///
/// Rendered via _Draw() so particles can rotate, scale freely, and overlap
/// the sub-grid without being constrained to it.
///
/// SETUP:
///   1. Add a Node2D as sibling of TendrilRenderer/CreatureRenderer
///   2. Attach this script
///   3. Set Z-Index above CreatureRenderer (e.g. 102)
///   4. Call SpawnBurst() from CreatureManager when a creature dies
///
/// PERFORMANCE:
///   Typically 0–200 active particles. Each is a single DrawRect call.
///   At 60fps this is trivial.
/// </summary>
public partial class CreatureParticles : Node2D
{
	// --- Burst Config ---

	/// <summary>Base speed of particles flying outward (pixels/sec).</summary>
	[Export] public float BurstSpeed = 120f;

	/// <summary>Random spread added to burst speed (pixels/sec).</summary>
	[Export] public float BurstSpeedVariance = 60f;

	/// <summary>Upward bias — particles arc up before falling. Higher = more loft.</summary>
	[Export] public float LaunchUpwardBias = 80f;

	/// <summary>Gravity pulling particles down (pixels/sec²).</summary>
	[Export] public float Gravity = 280f;

	/// <summary>How long particles live (seconds).</summary>
	[Export] public float ParticleLifetime = 0.6f;

	/// <summary>Random variance on lifetime (seconds).</summary>
	[Export] public float LifetimeVariance = 0.25f;

	// --- Size Config ---

	/// <summary>Base particle size in pixels.</summary>
	[Export] public float BaseSize = 5.0f;

	/// <summary>Random variance on base size (pixels).</summary>
	[Export] public float SizeVariance = 3.0f;

	/// <summary>How much noise wobbles the size over time (multiplier 0–1).</summary>
	[Export] public float SizeNoiseAmount = 0.3f;

	/// <summary>Speed of the size noise wobble.</summary>
	[Export] public float SizeNoiseSpeed = 8.0f;

	/// <summary>Particles shrink to this fraction of their size at end of life.</summary>
	[Export] public float EndSizeMultiplier = 0.15f;

	// --- Rotation Config ---

	/// <summary>Base spin speed in radians/sec.</summary>
	[Export] public float SpinSpeed = 6.0f;

	/// <summary>Random variance on spin speed.</summary>
	[Export] public float SpinVariance = 8.0f;

	// --- Visual Config ---

	/// <summary>Particles start fading out after this fraction of their lifetime (0–1).</summary>
	[Export] public float FadeStartFraction = 0.3f;

	/// <summary>Brighten particles slightly on spawn for a "flash" feel.</summary>
	[Export] public float SpawnBrighten = 0.25f;

	/// <summary>Air resistance — slows particles over time (0 = none, 1 = stops instantly).</summary>
	[Export] public float Drag = 1.2f;

	/// <summary>Max active particles. Oldest are culled if exceeded.</summary>
	[Export] public int MaxParticles = 300;

	// --- Internal ---

	private struct Particle
	{
		public Vector2 Position;      // World pixels
		public Vector2 Velocity;      // Pixels/sec
		public float Rotation;        // Radians
		public float AngularVelocity; // Radians/sec
		public float Size;            // Base size in pixels
		public float Lifetime;        // Total lifetime
		public float Age;             // Current age
		public Color BaseColor;       // Original cell color
		public float NoisePhase;      // Per-particle noise offset
	}

	private readonly List<Particle> _particles = new();
	private readonly Random _rng = new();

	public override void _Ready()
	{
		// Render above creatures and tendril
		ZAsRelative = false;
		ZIndex = 102;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		bool anyAlive = false;

		for (int i = _particles.Count - 1; i >= 0; i--)
		{
			var p = _particles[i];
			p.Age += dt;

			if (p.Age >= p.Lifetime)
			{
				_particles.RemoveAt(i);
				continue;
			}

			// Physics
			p.Velocity.Y += Gravity * dt;
			p.Velocity *= 1f - (Drag * dt);
			p.Position += p.Velocity * dt;
			p.Rotation += p.AngularVelocity * dt;

			_particles[i] = p;
			anyAlive = true;
		}

		if (anyAlive || _particles.Count > 0)
			QueueRedraw();
	}

	public override void _Draw()
	{
		if (_particles.Count == 0) return;

		for (int i = 0; i < _particles.Count; i++)
		{
			var p = _particles[i];
			float t = p.Age / p.Lifetime; // 0 → 1 over lifetime

			// --- Alpha fade ---
			float alpha;
			if (t < FadeStartFraction)
			{
				alpha = 1f;
			}
			else
			{
				float fadeFraction = (t - FadeStartFraction) / (1f - FadeStartFraction);
				alpha = 1f - fadeFraction;
				alpha = Mathf.Clamp(alpha, 0f, 1f);
			}

			// --- Color ---
			// Brighten on spawn, then return to base color
			float brighten = SpawnBrighten * (1f - Mathf.Clamp(t * 4f, 0f, 1f));
			Color color = new Color(
				Mathf.Clamp(p.BaseColor.R + brighten, 0f, 1f),
				Mathf.Clamp(p.BaseColor.G + brighten, 0f, 1f),
				Mathf.Clamp(p.BaseColor.B + brighten, 0f, 1f),
				alpha
			);

			// --- Size with noise wobble ---
			float lifetimeScale = Mathf.Lerp(1f, EndSizeMultiplier, t * t); // Ease-in shrink
			float noise = Mathf.Sin((p.Age * SizeNoiseSpeed) + p.NoisePhase);
			float sizeNoise = 1f + noise * SizeNoiseAmount;
			float finalSize = p.Size * lifetimeScale * sizeNoise;

			if (finalSize < 0.5f) continue; // Too small to see

			// --- Draw rotated rect ---
			Vector2 localPos = p.Position - GlobalPosition;
			float halfSize = finalSize * 0.5f;

			// Build a small rotated rectangle
			Transform2D xform = Transform2D.Identity;
			xform = xform.Translated(-new Vector2(halfSize, halfSize)); // Center pivot
			xform = xform.Rotated(p.Rotation);
			xform = xform.Translated(localPos);

			DrawSetTransformMatrix(xform);
			DrawRect(new Rect2(0, 0, finalSize, finalSize), color);
		}

		// Reset transform for next frame
		DrawSetTransformMatrix(Transform2D.Identity);
	}

	// =========================================================================
	//  PUBLIC API — call from CreatureManager on creature death
	// =========================================================================

	/// <summary>
	/// Spawn a burst of particles from a creature's body cells.
	/// Call this when a creature is consumed or killed.
	/// </summary>
	/// <param name="creature">The creature that just died.</param>
	/// <param name="tendrilSubX">Tendril head sub-grid X (for directional bias).</param>
	/// <param name="tendrilSubY">Tendril head sub-grid Y (for directional bias).</param>
	public void SpawnBurst(Creature creature, int tendrilSubX, int tendrilSubY)
	{
		if (creature?.Body == null) return;

		var bodySet = CreatureBodyRegistry.GetBodySet(creature.Species);
		var body = bodySet.Idle;

		int cellSize = WorldConfig.SubCellSize;

		// Direction from creature toward tendril (particles fly AWAY from consumer)
		float creatureWorldX = creature.SubX * cellSize;
		float creatureWorldY = creature.SubY * cellSize;
		float tendrilWorldX = tendrilSubX * cellSize;
		float tendrilWorldY = tendrilSubY * cellSize;

		Vector2 awayDir = new Vector2(
			creatureWorldX - tendrilWorldX,
			creatureWorldY - tendrilWorldY
		);
		if (awayDir.LengthSquared() > 0.01f)
			awayDir = awayDir.Normalized();
		else
			awayDir = Vector2.Up;

		for (int i = 0; i < body.Cells.Length; i++)
		{
			if (_particles.Count >= MaxParticles)
			{
				// Cull oldest particles to make room
				int toRemove = Mathf.Min(body.Cells.Length, _particles.Count / 4);
				_particles.RemoveRange(0, toRemove);
			}

			var (dx, dy) = body.Cells[i];
			Color cellColor = body.Colors[i];

			// World position of this cell
			float worldX = (creature.SubX + dx) * cellSize + cellSize * 0.5f;
			float worldY = (creature.SubY + dy) * cellSize + cellSize * 0.5f;

			// Outward velocity — biased away from tendril with random spread
			float angle = Mathf.Atan2(awayDir.Y, awayDir.X);
			float spread = (NextFloat() - 0.5f) * Mathf.Pi * 1.2f; // ±108° spread
			float finalAngle = angle + spread;

			float speed = BurstSpeed + NextFloat() * BurstSpeedVariance;

			Vector2 velocity = new Vector2(
				Mathf.Cos(finalAngle) * speed,
				Mathf.Sin(finalAngle) * speed - LaunchUpwardBias // Upward loft
			);

			// Per-particle size variation
			float size = BaseSize + (NextFloat() - 0.5f) * 2f * SizeVariance;
			size = Mathf.Max(1.5f, size);

			// Per-particle spin
			float spin = SpinSpeed + (NextFloat() - 0.5f) * 2f * SpinVariance;

			// Per-particle lifetime
			float lifetime = ParticleLifetime + (NextFloat() - 0.5f) * 2f * LifetimeVariance;
			lifetime = Mathf.Max(0.1f, lifetime);

			_particles.Add(new Particle
			{
				Position = new Vector2(worldX, worldY),
				Velocity = velocity,
				Rotation = NextFloat() * Mathf.Tau,
				AngularVelocity = spin,
				Size = size,
				Lifetime = lifetime,
				Age = 0f,
				BaseColor = cellColor,
				NoisePhase = NextFloat() * Mathf.Tau * 4f,
			});
		}
	}

	/// <summary>
	/// Spawn a smaller burst for damage impacts (not death).
	/// Fewer particles, less speed, shorter lifetime.
	/// </summary>
	public void SpawnDamageHit(Creature creature, int impactSubX, int impactSubY)
	{
		if (creature?.Body == null) return;

		var bodySet = CreatureBodyRegistry.GetBodySet(creature.Species);
		var body = bodySet.Idle;

		int cellSize = WorldConfig.SubCellSize;

		Vector2 awayDir = new Vector2(
			creature.SubX - impactSubX,
			creature.SubY - impactSubY
		);
		if (awayDir.LengthSquared() > 0.01f)
			awayDir = awayDir.Normalized();
		else
			awayDir = Vector2.Up;

		// Only spawn a fraction of the cells for a hit (not full death burst)
		int count = Mathf.Max(2, body.Cells.Length / 3);

		for (int i = 0; i < count; i++)
		{
			if (_particles.Count >= MaxParticles) break;

			// Pick a random cell from the body
			int cellIdx = _rng.Next(body.Cells.Length);
			var (dx, dy) = body.Cells[cellIdx];
			Color cellColor = body.Colors[cellIdx];

			float worldX = (creature.SubX + dx) * cellSize + cellSize * 0.5f;
			float worldY = (creature.SubY + dy) * cellSize + cellSize * 0.5f;

			float angle = Mathf.Atan2(awayDir.Y, awayDir.X);
			float spread = (NextFloat() - 0.5f) * Mathf.Pi * 0.8f;
			float finalAngle = angle + spread;

			float speed = BurstSpeed * 0.5f + NextFloat() * BurstSpeedVariance * 0.3f;

			Vector2 velocity = new Vector2(
				Mathf.Cos(finalAngle) * speed,
				Mathf.Sin(finalAngle) * speed - LaunchUpwardBias * 0.4f
			);

			float size = BaseSize * 0.7f + (NextFloat() - 0.5f) * SizeVariance;
			size = Mathf.Max(1f, size);

			float spin = SpinSpeed * 0.6f + (NextFloat() - 0.5f) * SpinVariance;

			float lifetime = ParticleLifetime * 0.5f + (NextFloat() - 0.5f) * LifetimeVariance * 0.3f;
			lifetime = Mathf.Max(0.08f, lifetime);

			_particles.Add(new Particle
			{
				Position = new Vector2(worldX, worldY),
				Velocity = velocity,
				Rotation = NextFloat() * Mathf.Tau,
				AngularVelocity = spin,
				Size = size,
				Lifetime = lifetime,
				Age = 0f,
				BaseColor = cellColor,
				NoisePhase = NextFloat() * Mathf.Tau * 4f,
			});
		}
	}

	// =========================================================================
	//  HELPERS
	// =========================================================================

	private float NextFloat() => _rng.NextSingle();
}
