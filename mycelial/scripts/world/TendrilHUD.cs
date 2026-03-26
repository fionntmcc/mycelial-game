namespace Mycorrhiza.World;

using Godot;

/// <summary>
/// HUD displaying Vitality bar, Vigor bar, connection status, and tile count.
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
    private ColorRect _vitalityBarBg;
    private ColorRect _vitalityBarFill;
    private Label _vitalityLabel;
    private ColorRect _vigorBarBg;
    private ColorRect _vigorBarFill;
    private Label _vigorLabel;
    private Label _connectionLabel;
    private Label _statusLabel;
    private Label _tileCountLabel;

    // Bar config
    private const float BarWidth = 250f;
    private const float BarHeight = 16f;
    private const float BarMargin = 20f;
    private const float BarSpacing = 6f;

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

        // Defer signal connections — TendrilController._Ready() hasn't run yet
        // (HUD appears before TendrilController in the scene tree), so Vitals
        // is null at this point. CallDeferred waits until the current frame's
        // _Ready() chain completes.
        CallDeferred(nameof(ConnectSignals));
    }

    private void ConnectSignals()
    {
        if (_tendril.Vitals == null)
        {
            GD.PrintErr("TendrilHUD: TendrilVitality not found on controller!");
            return;
        }

        _tendril.Vitals.VitalityChanged += OnVitalityChanged;
        _tendril.Vitals.VigorChanged += OnVigorChanged;
        _tendril.Vitals.ConnectionChanged += OnConnectionChanged;
        _tendril.RetreatStarted += OnRetreatStarted;
        _tendril.RetreatEnded += OnRetreatEnded;

        // Initial display now that vitals are initialized
        UpdateVitalityDisplay(_tendril.Vitality, _tendril.MaxVitality);
        UpdateVigorDisplay(_tendril.Vigor, _tendril.MaxVigor);
    }

    private void CreateUI()
    {
        float yOffset = BarMargin;

        // --- Vitality Bar Background ---
        _vitalityBarBg = new ColorRect();
        _vitalityBarBg.Color = new Color(0.15f, 0.15f, 0.15f, 0.8f);
        _vitalityBarBg.Size = new Vector2(BarWidth + 4, BarHeight + 4);
        _vitalityBarBg.Position = new Vector2(BarMargin - 2, yOffset - 2);
        AddChild(_vitalityBarBg);

        // --- Vitality Bar Fill ---
        _vitalityBarFill = new ColorRect();
        _vitalityBarFill.Color = new Color(0.8f, 0.2f, 0.2f); // Red for health
        _vitalityBarFill.Size = new Vector2(BarWidth, BarHeight);
        _vitalityBarFill.Position = new Vector2(BarMargin, yOffset);
        AddChild(_vitalityBarFill);

        // --- Vitality Label ---
        _vitalityLabel = new Label();
        _vitalityLabel.Position = new Vector2(BarMargin + BarWidth + 10, yOffset - 2);
        _vitalityLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        _vitalityLabel.AddThemeFontSizeOverride("font_size", 13);
        AddChild(_vitalityLabel);

        yOffset += BarHeight + BarSpacing;

        // --- Vigor Bar Background ---
        _vigorBarBg = new ColorRect();
        _vigorBarBg.Color = new Color(0.15f, 0.15f, 0.15f, 0.8f);
        _vigorBarBg.Size = new Vector2(BarWidth + 4, BarHeight + 4);
        _vigorBarBg.Position = new Vector2(BarMargin - 2, yOffset - 2);
        AddChild(_vigorBarBg);

        // --- Vigor Bar Fill ---
        _vigorBarFill = new ColorRect();
        _vigorBarFill.Color = new Color(0.2f, 0.8f, 0.2f); // Green for vigor
        _vigorBarFill.Size = new Vector2(BarWidth, BarHeight);
        _vigorBarFill.Position = new Vector2(BarMargin, yOffset);
        AddChild(_vigorBarFill);

        // --- Vigor Label ---
        _vigorLabel = new Label();
        _vigorLabel.Position = new Vector2(BarMargin + BarWidth + 10, yOffset - 2);
        _vigorLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        _vigorLabel.AddThemeFontSizeOverride("font_size", 13);
        AddChild(_vigorLabel);

        yOffset += BarHeight + BarSpacing + 2;

        // --- Connection Warning ---
        _connectionLabel = new Label();
        _connectionLabel.Position = new Vector2(BarMargin, yOffset);
        _connectionLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.3f, 0.3f));
        _connectionLabel.AddThemeFontSizeOverride("font_size", 13);
        _connectionLabel.Visible = false;
        AddChild(_connectionLabel);

        // --- Status Label ---
        _statusLabel = new Label();
        _statusLabel.Position = new Vector2(BarMargin, yOffset + 18);
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.7f, 1.0f, 0.7f));
        _statusLabel.AddThemeFontSizeOverride("font_size", 12);
        AddChild(_statusLabel);

        // --- Tile Count ---
        _tileCountLabel = new Label();
        _tileCountLabel.Position = new Vector2(BarMargin, yOffset + 36);
        _tileCountLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 0.6f));
        _tileCountLabel.AddThemeFontSizeOverride("font_size", 12);
        AddChild(_tileCountLabel);
    }

    public override void _Process(double delta)
    {
        if (_tendril == null) return;

        _tileCountLabel.Text = $"Territory: {_tendril.ClaimedTileCount} tiles";

        if (_tendril.IsRegenerating)
            UpdateStatus("REGENERATING...");
        else if (_tendril.IsRetreating)
            UpdateStatus("RETREATING!");
        else
        {
            string tier = _tendril.GetVigorTierName();
            UpdateStatus($"{tier} — Spreading...");
        }
    }

    private void OnVitalityChanged(float current, float max)
    {
        UpdateVitalityDisplay(current, max);
    }

    private void OnVigorChanged(float current, float max)
    {
        UpdateVigorDisplay(current, max);
    }

    private void OnConnectionChanged(bool connected)
    {
        _connectionLabel.Visible = !connected;
        _connectionLabel.Text = "DISCONNECTED — vitality draining!";
    }

    private void OnRetreatStarted()
    {
        UpdateStatus("RETREATING!");
    }

    private void OnRetreatEnded()
    {
        UpdateStatus("Regenerating...");
    }

    private void UpdateVitalityDisplay(float current, float max)
    {
        float ratio = max > 0 ? current / max : 0;
        _vitalityBarFill.Size = new Vector2(BarWidth * ratio, BarHeight);

        // Red → dark red as vitality drops
        if (ratio > 0.5f)
            _vitalityBarFill.Color = new Color(0.8f, 0.2f, 0.2f);
        else if (ratio > 0.25f)
            _vitalityBarFill.Color = new Color(0.7f, 0.15f, 0.15f);
        else
            _vitalityBarFill.Color = new Color(0.5f, 0.1f, 0.1f);

        _vitalityLabel.Text = $"Vitality {current:F0}/{max:F0}";
    }

    private void UpdateVigorDisplay(float current, float max)
    {
        float ratio = max > 0 ? current / max : 0;
        _vigorBarFill.Size = new Vector2(BarWidth * ratio, BarHeight);

        // Vigor color by tier
        if (current > 90f)
            _vigorBarFill.Color = new Color(1.0f, 0.85f, 0.0f); // Gold — Unstoppable
        else if (current > 70f)
            _vigorBarFill.Color = new Color(0.1f, 0.9f, 0.1f); // Bright green — Apex
        else if (current > 40f)
            _vigorBarFill.Color = new Color(0.2f, 0.7f, 0.2f); // Green — Thriving
        else if (current > 15f)
            _vigorBarFill.Color = new Color(0.6f, 0.6f, 0.2f); // Yellow — Surviving
        else
            _vigorBarFill.Color = new Color(0.5f, 0.2f, 0.1f); // Brown — Withering

        _vigorLabel.Text = $"Vigor {current:F0}/{max:F0}";
    }

    private void UpdateStatus(string text)
    {
        if (_statusLabel != null)
            _statusLabel.Text = text;
    }
}
