using Godot;

public partial class SettingsMenu : Control
{
	[Export] public NodePath BackButtonPath;
	private Button _btnBack;
	private HSlider _slider;

	public override void _Ready()
	{
		// Botón Back
		_btnBack = GetNode<Button>(BackButtonPath ?? "Center/VBox/BtnBack");
		_btnBack.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");

		// Slider de volumen
		_slider = GetNode<HSlider>("Center/VBox/Volumen");

		int bus = AudioServer.GetBusIndex("Music"); // ⚡ bus de música
		_slider.MinValue = -40;
		_slider.MaxValue = 0;
		_slider.Step = 1;
		_slider.Value = AudioServer.GetBusVolumeDb(bus);

		_slider.ValueChanged += (double v) =>
		{
			AudioServer.SetBusVolumeDb(bus, (float)v);
		};
	}
}
