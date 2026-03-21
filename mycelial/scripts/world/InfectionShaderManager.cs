namespace Mycorrhiza.World;

using Godot;
using System.Collections.Generic;

/// <summary>
/// Applies the mycelium infection shader to all loaded ChunkRenderer nodes.
///
/// This manager finds all TileMapLayer children of ChunkRenderers and assigns
/// the infection ShaderMaterial. It also updates shader uniforms each frame
/// so all chunks pulse in sync.
///
/// The shader affects ALL tiles visually, but the effect is designed to only
/// be noticeable on mycelium/infected tiles (the pulse and veins blend
/// naturally with the darker fungal palette and are invisible on bright
/// dirt/grass tiles).
///
/// SETUP:
///   - Add as a child of ChunkManager (or as a sibling)
///   - Assign ChunkManagerPath
///   - Assign the shader file: res://shaders/mycelium_infection.gdshader
///   - The manager handles everything else automatically
///
/// ALTERNATIVE SETUP (no scene changes):
///   If you don't want to modify your scene, you can instead apply the shader
///   manually in ChunkRenderer.Initialize() — see the README.
/// </summary>
public partial class InfectionShaderManager : Node
{
	[Export] public NodePath ChunkManagerPath { get; set; }

	/// <summary>
	/// The shader resource. Assign res://shaders/mycelium_infection.gdshader
	/// in the inspector.
	/// </summary>
	[Export] public Shader InfectionShader { get; set; }

	[ExportGroup("Shader Tuning")]
	[Export(PropertyHint.Range, "0.5,4.0")] public float PulseSpeed = 1.8f;
	[Export(PropertyHint.Range, "0.0,0.4")] public float PulseIntensity = 0.15f;
	[Export(PropertyHint.Range, "0.2,3.0")] public float WritheSpeed = 0.9f;
	[Export(PropertyHint.Range, "0.0,3.0")] public float WritheAmplitude = 1.2f;
	[Export(PropertyHint.Range, "0.0,1.0")] public float GlowIntensity = 0.35f;

	private Node _chunkManager;
	private ShaderMaterial _sharedMaterial;
	private readonly HashSet<ulong> _processedLayers = new();

	public override void _Ready()
	{
		if (ChunkManagerPath != null)
			_chunkManager = GetNode(ChunkManagerPath);

		if (_chunkManager == null)
		{
			GD.PrintErr("InfectionShaderManager: No ChunkManager assigned!");
			return;
		}

		if (InfectionShader == null)
		{
			GD.PrintErr("InfectionShaderManager: No shader assigned! Assign mycelium_infection.gdshader.");
			return;
		}

		// Create a single shared ShaderMaterial — all TileMapLayers reference this
		// so uniform changes propagate to everything instantly
		_sharedMaterial = new ShaderMaterial
		{
			Shader = InfectionShader
		};

		SyncUniforms();
	}

	public override void _Process(double delta)
	{
		if (_chunkManager == null || _sharedMaterial == null) return;

		// Scan for new ChunkRenderer children that haven't had the shader applied yet
		ApplyShaderToNewChunks();

		// Update uniforms if they've been changed in the inspector
		SyncUniforms();
	}

	/// <summary>
	/// Find any TileMapLayer nodes in ChunkRenderer children that don't have
	/// the shader yet, and apply it.
	/// </summary>
	private void ApplyShaderToNewChunks()
	{
		foreach (Node child in _chunkManager.GetChildren())
		{
			if (child is not ChunkRenderer renderer) continue;

			foreach (Node subChild in renderer.GetChildren())
			{
				if (subChild is not TileMapLayer tileMap) continue;

				ulong id = tileMap.GetInstanceId();
				if (_processedLayers.Contains(id)) continue;

				tileMap.Material = _sharedMaterial;
				_processedLayers.Add(id);
			}
		}
	}

	/// <summary>
	/// Push exported tuning values into the shader uniforms.
	/// </summary>
	private void SyncUniforms()
	{
		_sharedMaterial.SetShaderParameter("pulse_speed", PulseSpeed);
		_sharedMaterial.SetShaderParameter("pulse_intensity", PulseIntensity);
		_sharedMaterial.SetShaderParameter("writhe_speed", WritheSpeed);
		_sharedMaterial.SetShaderParameter("writhe_amplitude", WritheAmplitude);
		_sharedMaterial.SetShaderParameter("glow_intensity", GlowIntensity);
	}
}
