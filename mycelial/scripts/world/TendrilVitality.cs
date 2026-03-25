namespace Mycorrhiza.World;

using Godot;

/// <summary>
/// Self-contained Vitality + Vigor component for the tendril.
///
/// Owns all health/energy state, decay, regeneration, connection-drain,
/// vigor scaling multipliers, and the signals that drive UI.
///
/// TendrilController feeds it per-frame context (delta, tile state, connection)
/// and reads back multipliers and status flags. No back-reference to the controller.
///
/// ARCHITECTURE:
///   Vitality = health. Drains when disconnected or vigor hits zero. Zero → retreat.
///   Vigor    = combat energy. Gained from kills/catches, decays over time.
///              Drives multipliers for speed, blob size, harpoon range, root spread,
///              and passive corruption via configurable power curves.
/// </summary>
public partial class TendrilVitality : Node
{
	// =========================================================================
	//  VITALITY CONFIG
	// =========================================================================

	[Export] public float MaxVitality = 100f;
	[Export] public float VitalityRegenOnTerritory = 3f;
	[Export] public float VitalityRegenOnTree = 8f;
	[Export] public float DisconnectVitalityDrain = 5f;
	[Export] public float DisconnectDrainEscalation = 2f;
	[Export] public float DisconnectEscalationInterval = 4f;

	// =========================================================================
	//  VIGOR CONFIG
	// =========================================================================

	[Export] public float MaxVigor = 100f;
	[Export] public float VigorDecayRate = 4f;
	[Export] public float VigorGracePeriod = 1.5f;
	[Export] public float VigorGainHarpoonCatch = 5f;
	[Export] public float VigorGainSlamKill = 25f;
	[Export] public float VigorGainReconnect = 10f;

	/// <summary>Vitality drain per second while vigor is at zero.</summary>
	[Export] public float ZeroVigorVitalityDrain = 2f;

	// =========================================================================
	//  VIGOR SCALING — SPEED
	// =========================================================================

	/// <summary>Speed multiplier at zero vigor.</summary>
	[Export] public float VigorSpeedMin = 0.6f;
	/// <summary>Speed multiplier at max vigor.</summary>
	[Export] public float VigorSpeedMax = 1.4f;
	/// <summary>Curve exponent. &lt;1 = diminishing returns, 1 = linear, &gt;1 = accelerating.</summary>
	[Export] public float VigorSpeedExponent = 0.75f;

	// =========================================================================
	//  VIGOR SCALING — BLOB SIZE
	// =========================================================================

	/// <summary>Blob size multiplier at zero vigor.</summary>
	[Export] public float VigorBlobSizeMin = 1.0f;
	/// <summary>Blob size multiplier at max vigor.</summary>
	[Export] public float VigorBlobSizeMax = 1.4f;
	/// <summary>Curve exponent. &lt;1 = diminishing returns, 1 = linear, &gt;1 = accelerating.</summary>
	[Export] public float VigorBlobSizeExponent = 0.75f;

	// =========================================================================
	//  CONNECTION CONFIG
	// =========================================================================

	[Export] public float ConnectionCheckInterval = 1.0f;
	[Export] public float DisconnectedCheckInterval = 0.5f;

	// =========================================================================
	//  SIGNALS
	// =========================================================================

	[Signal] public delegate void VitalityChangedEventHandler(float current, float max);
	[Signal] public delegate void VigorChangedEventHandler(float current, float max);
	[Signal] public delegate void ConnectionChangedEventHandler(bool connected);

	/// <summary>Emitted when vitality hits zero — the controller should start retreating.</summary>
	[Signal] public delegate void VitalityDepletedEventHandler();

	// =========================================================================
	//  PUBLIC STATE (read-only for external systems)
	// =========================================================================

	public float Vitality { get; private set; }
	public float Vigor { get; private set; }
	public new bool IsConnected { get; private set; } = true;

	// Vigor-scaled multipliers
	public float SpeedMultiplier => PowerCurve(Vigor, MaxVigor, VigorSpeedMin, VigorSpeedMax, VigorSpeedExponent);
	public float BlobSizeMultiplier => PowerCurve(Vigor, MaxVigor, VigorBlobSizeMin, VigorBlobSizeMax, VigorBlobSizeExponent);
	public float HarpoonRangeMultiplier => TieredMultiplier(Vigor, 0.7f, 1.0f, 1.2f, 1.4f, 1.5f);
	public float RootSpreadMultiplier => TieredMultiplier(Vigor, 0.3f, 1.0f, 1.3f, 1.6f, 2.0f);
	public float CorruptionSpeedMultiplier => TieredMultiplier(Vigor, 0f, 1.0f, 1.2f, 1.5f, 2.0f);

	// =========================================================================
	//  PRIVATE STATE
	// =========================================================================

	private float _vigorGraceTimer;
	private float _connectionCheckTimer;
	private float _disconnectTime;
	private int _disconnectEscalationCount;

	// =========================================================================
	//  PUBLIC API — called by TendrilController each frame
	// =========================================================================

	/// <summary>Reset all state when spawning/respawning.</summary>
	public void Reset(float startingVigor = 30f)
	{
		Vitality = MaxVitality;
		Vigor = startingVigor;
		IsConnected = true;
		_vigorGraceTimer = 0f;
		_connectionCheckTimer = 0f;
		_disconnectTime = 0f;
		_disconnectEscalationCount = 0;

		EmitSignal(SignalName.VitalityChanged, Vitality, MaxVitality);
		EmitSignal(SignalName.VigorChanged, Vigor, MaxVigor);
	}

	/// <summary>
	/// Per-frame update. The controller passes in tile context so TendrilVitality
	/// never needs a back-reference to TendrilController.
	/// </summary>
	/// <param name="dt">Frame delta.</param>
	/// <param name="onTree">Is the head currently on a tree tile?</param>
	/// <param name="onTerritory">Is the head on a claimed territory tile?</param>
	/// <param name="connectionCheck">Lazy BFS check — only invoked when the timer fires.</param>
	public void Process(float dt, bool onTree, bool onTerritory, System.Func<bool> connectionCheck)
	{
		UpdateConnection(dt, connectionCheck);
		UpdateVitality(dt, onTree, onTerritory);
		UpdateVigorDecay(dt);
	}

	/// <summary>Add vigor from any source (kills, catches, reconnection). Resets grace timer.</summary>
	public void AddVigor(float amount)
	{
		Vigor = System.Math.Min(MaxVigor, Vigor + amount);
		_vigorGraceTimer = VigorGracePeriod;
		EmitSignal(SignalName.VigorChanged, Vigor, MaxVigor);
	}

	/// <summary>Damage vitality from external source (creature attack, hazard, etc.).</summary>
	public void DamageVitality(float amount)
	{
		Vitality = System.Math.Max(0f, Vitality - amount);
		EmitSignal(SignalName.VitalityChanged, Vitality, MaxVitality);
		if (Vitality <= 0f)
			EmitSignal(SignalName.VitalityDepleted);
	}

	/// <summary>Returns the name of the current vigor tier.</summary>
	public string GetVigorTierName()
	{
		if (Vigor <= 15) return "Withering";
		if (Vigor <= 40) return "Surviving";
		if (Vigor <= 70) return "Thriving";
		if (Vigor <= 90) return "Apex";
		return "Unstoppable";
	}

	// =========================================================================
	//  INTERNALS
	// =========================================================================

	private void UpdateConnection(float dt, System.Func<bool> connectionCheck)
	{
		float interval = IsConnected ? ConnectionCheckInterval : DisconnectedCheckInterval;
		_connectionCheckTimer += dt;
		if (_connectionCheckTimer < interval) return;
		_connectionCheckTimer = 0f;

		bool wasConnected = IsConnected;
		IsConnected = connectionCheck();

		if (wasConnected && !IsConnected)
		{
			_disconnectTime = 0f;
			_disconnectEscalationCount = 0;
			GD.Print("DISCONNECTED from network!");
			EmitSignal(SignalName.ConnectionChanged, false);
		}
		else if (!wasConnected && IsConnected)
		{
			_disconnectTime = 0f;
			_disconnectEscalationCount = 0;
			AddVigor(VigorGainReconnect);
			GD.Print("Reconnected to network! +vigor");
			EmitSignal(SignalName.ConnectionChanged, true);
		}
	}

	private void UpdateVitality(float dt, bool onTree, bool onTerritory)
	{
		if (IsConnected)
		{
			if (onTree)
			{
				Vitality = System.Math.Min(MaxVitality, Vitality + VitalityRegenOnTree * dt);
				EmitSignal(SignalName.VitalityChanged, Vitality, MaxVitality);
			}
			else if (onTerritory)
			{
				Vitality = System.Math.Min(MaxVitality, Vitality + VitalityRegenOnTerritory * dt);
				EmitSignal(SignalName.VitalityChanged, Vitality, MaxVitality);
			}
		}
		else
		{
			float drain = DisconnectVitalityDrain + _disconnectEscalationCount * DisconnectDrainEscalation;
			Vitality = System.Math.Max(0f, Vitality - drain * dt);
			EmitSignal(SignalName.VitalityChanged, Vitality, MaxVitality);

			_disconnectTime += dt;
			_disconnectEscalationCount = (int)(_disconnectTime / DisconnectEscalationInterval);

			if (Vitality <= 0f)
			{
				EmitSignal(SignalName.VitalityDepleted);
				return;
			}
		}

		// Prolonged zero vigor drains vitality
		if (Vigor <= 0f)
		{
			Vitality = System.Math.Max(0f, Vitality - ZeroVigorVitalityDrain * dt);
			EmitSignal(SignalName.VitalityChanged, Vitality, MaxVitality);
			if (Vitality <= 0f)
				EmitSignal(SignalName.VitalityDepleted);
		}
	}

	private void UpdateVigorDecay(float dt)
	{
		if (_vigorGraceTimer > 0f)
		{
			_vigorGraceTimer -= dt;
			return;
		}

		float decayRate = IsConnected ? VigorDecayRate : VigorDecayRate * 2f;
		Vigor = System.Math.Max(0f, Vigor - decayRate * dt);
		EmitSignal(SignalName.VigorChanged, Vigor, MaxVigor);
	}

	// =========================================================================
	//  CURVE HELPERS
	// =========================================================================

	/// <summary>Power-curve mapping: min + (max - min) * (vigor/maxVigor)^exponent</summary>
	private static float PowerCurve(float vigor, float maxVigor, float min, float max, float exponent)
	{
		float t = Mathf.Clamp(vigor / maxVigor, 0f, 1f);
		return min + (max - min) * Mathf.Pow(t, exponent);
	}

	/// <summary>Step-based multiplier for systems that haven't moved to curves yet.</summary>
	private static float TieredMultiplier(float vigor, float withering, float surviving, float thriving, float apex, float unstoppable)
	{
		if (vigor <= 15) return withering;
		if (vigor <= 40) return surviving;
		if (vigor <= 70) return thriving;
		if (vigor <= 90) return apex;
		return unstoppable;
	}
}
