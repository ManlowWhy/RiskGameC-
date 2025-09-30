using Godot;

public partial class SettingsMenu : Control
{
	[Export] public NodePath BackButtonPath;
	private Button _btnBack;

	public override void _Ready()
	{
		_btnBack = GetNode<Button>(BackButtonPath ?? "Center/VBox/BtnBack");
		_btnBack.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
	}
}
