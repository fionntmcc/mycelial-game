namespace Mycorrhiza.Debug;

using Godot;
using Mycorrhiza.World;

/// <summary>
/// Debug overlay for live-tuning tendril physics without restarting.
///
/// Shows real-time state: speed, heading, tile position, terrain drag, wall-slide status.
/// Provides sliders for key physics constants.
///
/// Toggle with F3. Only active in debug builds.
///
/// SETUP:
///   - Add as a CanvasLayer child of your scene root
///   - Assign TendrilControllerPath
///   - The overlay finds the TendrilHead automatically
/// </summary>
public partial class TendrilDebugOverlay : CanvasLayer
{
	[Export] public NodePath TendrilControllerPath { get; set; }
	[Export] public Key ToggleKey = Key.F3;

	private TendrilController _controller;
	private TendrilHead _head;
	private PanelContainer _panel;
	private VBoxContainer _vbox;
	private bool _visible;

	// Readout labels
	private Label _speedLabel;
	private Label _headingLabel;
	private Label _tileLabel;
	private Label _hungerLabel;
	private Label _slidingLabel;
	private Label _infectedLabel;

	public override void _Ready()
	{
		if (!OS.IsDebugBuild())
		{
			SetProcess(false);
			return;
		}

		if (TendrilControllerPath != null)
		{
			_controller = GetNode<TendrilController>(TendrilControllerPath);
			_head = _controller?.GetNodeOrNull<TendrilHead>("TendrilHead");
		}

		BuildUI();
		_panel.Visible = false;
		_visible = false;
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == ToggleKey)
		{
			_visible = !_visible;
			_panel.Visible = _visible;
		}
	}

	public override void _Process(double delta)
	{
		if (!_visible || _head == null || _controller == null) return;

		_speedLabel.Text = $"Speed: {_head.Speed:F0} px/s";
		_headingLabel.Text = $"Heading: {Mathf.RadToDeg(_head.Heading):F0}\u00b0";
		_tileLabel.Text = $"Tile: ({_head.CurrentTile.X}, {_head.CurrentTile.Y})";
		_hungerLabel.Text = $"Hunger: {_controller.Hunger:F0} / {_controller.MaxHunger:F0}";
		_slidingLabel.Text = _head.IsSlidingAlongWall ? "WALL SLIDE" : "";
		_infectedLabel.Text = $"Claimed: {_controller.ClaimedTileCount}";
	}

	private void BuildUI()
	{
		_panel = new PanelContainer();
		_panel.AnchorLeft = 0;
		_panel.AnchorTop = 0;
		_panel.OffsetLeft = 8;
		_panel.OffsetTop = 8;

		var panelStyle = new StyleBoxFlat
		{
			BgColor = new Color(0, 0, 0, 0.75f),
			ContentMarginLeft = 10,
			ContentMarginRight = 10,
			ContentMarginTop = 6,
			ContentMarginBottom = 6,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
		};
		_panel.AddThemeStyleboxOverride("panel", panelStyle);

		_vbox = new VBoxContainer();
		_panel.AddChild(_vbox);

		// Title
		var title = new Label { Text = "TENDRIL DEBUG [F3]" };
		title.AddThemeColorOverride("font_color", Colors.Yellow);
		_vbox.AddChild(title);
		_vbox.AddChild(new HSeparator());

		// Readouts
		_speedLabel = AddLabel("Speed: 0");
		_headingLabel = AddLabel("Heading: 0\u00b0");
		_tileLabel = AddLabel("Tile: (0, 0)");
		_hungerLabel = AddLabel("Hunger: 100 / 100");
		_infectedLabel = AddLabel("Claimed: 0");
		_slidingLabel = AddLabel("");
		_slidingLabel.AddThemeColorOverride("font_color", Colors.Cyan);

		_vbox.AddChild(new HSeparator());

		// Physics tuning sliders
		if (_head != null)
		{
			AddSlider("Thrust", 100, 800, _head.Thrust,
				v => _head.Thrust = (float)v);
			AddSlider("Drag", 0.5, 10, _head.BaseDrag,
				v => _head.BaseDrag = (float)v);
			AddSlider("Turn Rate", 1, 12, _head.MaxTurnRate,
				v => _head.MaxTurnRate = (float)v);
			AddSlider("Max Speed", 50, 600, _head.MaxSpeed,
				v => _head.MaxSpeed = (float)v);
			AddSlider("Wall Friction", 0, 1, _head.WallSlideFriction,
				v => _head.WallSlideFriction = (float)v);
			AddSlider("Collision R", 2, 14, _head.CollisionRadius,
				v => _head.CollisionRadius = (float)v);
		}

		AddChild(_panel);
	}

	private Label AddLabel(string text)
	{
		var label = new Label { Text = text };
		_vbox.AddChild(label);
		return label;
	}

	private void AddSlider(string name, double min, double max, double defaultVal,
		System.Action<double> onChange)
	{
		var hbox = new HBoxContainer();

		var nameLabel = new Label
		{
			Text = name,
			CustomMinimumSize = new Vector2(100, 0),
		};
		hbox.AddChild(nameLabel);

		var slider = new HSlider
		{
			MinValue = min,
			MaxValue = max,
			Value = defaultVal,
			Step = (max - min) > 20 ? 1 : 0.05,
			CustomMinimumSize = new Vector2(100, 0),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};

		var valLabel = new Label
		{
			Text = defaultVal.ToString("F1"),
			CustomMinimumSize = new Vector2(44, 0),
		};

		slider.ValueChanged += v =>
		{
			valLabel.Text = v.ToString("F1");
			onChange(v);
		};

		hbox.AddChild(slider);
		hbox.AddChild(valLabel);
		_vbox.AddChild(hbox);
	}
}
