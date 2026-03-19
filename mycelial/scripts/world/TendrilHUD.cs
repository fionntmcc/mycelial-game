namespace Mycorrhiza.World;

using Godot;

/// <summary>
/// Simple HUD that displays the hunger bar, tendril status, and tile count.
/// Uses a CanvasLayer so it stays on screen regardless of camera position.
///
/// SETUP:
///   - Add a CanvasLayer to the scene, attach this script
///   - Assign TendrilControllerPath to your TendrilController node
///   - Everything is drawn procedurally — no scene setup needed
/// </summary>
public partial class TendrilHUD : CanvasLayer
{
    [Export] public NodePath TendrilControllerPath { get; set; }

    private TendrilController _tendril;

    // UI elements (created in code)
    private ColorRect _hungerBarBg;
    private ColorRect _hungerBarFill;
    private Label _hungerLabel;
    private Label _statusLabel;
    private Label _tileCountLabel;

    // Bar config
    private const float BarWidth = 250f;
    private const float BarHeight = 20f;
    private const float BarMargin = 20f;

    public override void _Ready()
    {
        if (TendrilControllerPath != null)
            _tendril = GetNode<TendrilController>(TendrilControllerPath);

        if (_tendril == null)
        {
            GD.PrintErr("TendrilHUD: No TendrilController assigned!");
            return;
        }

        CreateUI();

        // Connect signals
        _tendril.HungerChanged += OnHungerChanged;
        _tendril.RetreatStarted += OnRetreatStarted;
        _tendril.RetreatEnded += OnRetreatEnded;
    }

    private void CreateUI()
    {
        // --- Hunger Bar Background ---
        _hungerBarBg = new ColorRect();
        _hungerBarBg.Color = new Color(0.15f, 0.15f, 0.15f, 0.8f);
        _hungerBarBg.Size = new Vector2(BarWidth + 4, BarHeight + 4);
        _hungerBarBg.Position = new Vector2(BarMargin - 2, BarMargin - 2);
        AddChild(_hungerBarBg);

        // --- Hunger Bar Fill ---
        _hungerBarFill = new ColorRect();
        _hungerBarFill.Color = new Color(0.2f, 0.8f, 0.2f); // Green
        _hungerBarFill.Size = new Vector2(BarWidth, BarHeight);
        _hungerBarFill.Position = new Vector2(BarMargin, BarMargin);
        AddChild(_hungerBarFill);

        // --- Hunger Label ---
        _hungerLabel = new Label();
        _hungerLabel.Position = new Vector2(BarMargin, BarMargin + BarHeight + 4);
        _hungerLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        _hungerLabel.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_hungerLabel);

        // --- Status Label ---
        _statusLabel = new Label();
        _statusLabel.Position = new Vector2(BarMargin, BarMargin + BarHeight + 24);
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.7f, 1.0f, 0.7f));
        _statusLabel.AddThemeFontSizeOverride("font_size", 12);
        AddChild(_statusLabel);

        // --- Tile Count ---
        _tileCountLabel = new Label();
        _tileCountLabel.Position = new Vector2(BarMargin, BarMargin + BarHeight + 44);
        _tileCountLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 0.6f));
        _tileCountLabel.AddThemeFontSizeOverride("font_size", 12);
        AddChild(_tileCountLabel);

        // Initial update
        UpdateHungerDisplay(_tendril.MaxHunger, _tendril.MaxHunger);
        UpdateStatus("Spreading...");
    }

    public override void _Process(double delta)
    {
        if (_tendril == null) return;

        // Update tile count
        _tileCountLabel.Text = $"Territory: {_tendril.ClaimedTileCount} tiles";

        // Update status
        if (_tendril.IsRegenerating)
            UpdateStatus("REGENERATING...");
        else if (_tendril.IsRetreating)
            UpdateStatus("RETREATING!");
        else
            UpdateStatus("Spreading...");
    }

    private void OnHungerChanged(float current, float max)
    {
        UpdateHungerDisplay(current, max);
    }

    private void OnRetreatStarted()
    {
        UpdateStatus("RETREATING!");
        _hungerBarFill.Color = new Color(0.8f, 0.2f, 0.2f); // Red during retreat
    }

    private void OnRetreatEnded()
    {
        UpdateStatus("Regenerating...");
    }

    private void UpdateHungerDisplay(float current, float max)
    {
        float ratio = max > 0 ? current / max : 0;

        // Resize fill bar
        _hungerBarFill.Size = new Vector2(BarWidth * ratio, BarHeight);

        // Color transitions: green → yellow → orange → red
        if (ratio > 0.6f)
            _hungerBarFill.Color = new Color(0.2f, 0.8f, 0.2f); // Green
        else if (ratio > 0.3f)
            _hungerBarFill.Color = new Color(0.9f, 0.7f, 0.1f); // Yellow-orange
        else
            _hungerBarFill.Color = new Color(0.8f, 0.2f, 0.2f); // Red

        _hungerLabel.Text = $"Hunger: {current:F0} / {max:F0}";
    }

    private void UpdateStatus(string text)
    {
        if (_statusLabel != null)
            _statusLabel.Text = text;
    }
}
