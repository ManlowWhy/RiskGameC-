using Godot;
using System.Linq;

public partial class MainMenu : Control
{
	private Button _btnHost2, _btnHost3, _btnSettings, _btnQuit, _btnJoin;
	private AcceptDialog _nameDialog, _hostDialog, _joinDialog, _lobbyDialog;
	private LineEdit _nameEdit, _portEdit, _joinIpPortEdit;
	private Label _lobbyLabel;
	private int _pendingMaxPlayers = 2;

	private GameManager GM => GameManager.Instance;

	public override void _Ready()
	{
		// --- localizar botones existentes por texto ---
		var center = GetChildren().OfType<CenterContainer>().FirstOrDefault();
		var vbox   = center?.GetChildren().OfType<VBoxContainer>().FirstOrDefault();
		var buttons = vbox?.GetChildren().OfType<Button>().ToList() ?? new();

		_btnHost2    = buttons.FirstOrDefault(b => b.Text.Trim().ToLower() == "dos jugadores");
		_btnHost3    = buttons.FirstOrDefault(b => b.Text.Trim().ToLower() == "3 jugadores");
		_btnSettings = buttons.FirstOrDefault(b => b.Text.Trim().ToLower() == "configuraciones");
		_btnQuit     = buttons.FirstOrDefault(b => b.Text.Trim().ToLower() == "salir");

		// Crear botón "Unirse a partida" si no existe
		_btnJoin = buttons.FirstOrDefault(b => b.Text.ToLower().Contains("unirse")) ?? new Button { Text = "Unirse a partida" };
		if (!buttons.Contains(_btnJoin)) vbox?.AddChild(_btnJoin);

		BuildDialogs();

		_btnHost2.Pressed    += () => AskNameThenHost(2);
		_btnHost3.Pressed    += () => AskNameThenHost(3);
		_btnJoin.Pressed     += AskJoin;
		_btnSettings.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/SettingsMenu.tscn");
		_btnQuit.Pressed     += () => GM.QuitGame();

		// Actualiza el lobby (tanto host como cliente reciben esta señal)
		GM.LobbyUpdated += (int max, string[] names) => {
			if (_lobbyLabel != null) _lobbyLabel.Text = $"Jugadores ({names.Length}/{max}):\n - " + string.Join("\n - ", names);
		};
	}

	private void BuildDialogs()
	{
		// --- diálogo nombre ---
		_nameDialog = new AcceptDialog { Title = "Tu nombre", MinSize = new Vector2I(420, 200) };
		var vb1 = new VBoxContainer { CustomMinimumSize = new Vector2(380, 80) };
		vb1.AddChild(new Label { Text = "Escribe tu nombre:", HorizontalAlignment = HorizontalAlignment.Center });
		_nameEdit = new LineEdit { PlaceholderText = "Jugador" };
		vb1.AddChild(_nameEdit);
		_nameDialog.AddChild(vb1);
		AddChild(_nameDialog);
		_nameEdit.TextSubmitted += (_) => _nameDialog.EmitSignal(AcceptDialog.SignalName.Confirmed);

		// --- diálogo host (puerto) ---
		_hostDialog = new AcceptDialog { Title = "Crear partida", MinSize = new Vector2I(420, 220) };
		var vb2 = new VBoxContainer { CustomMinimumSize = new Vector2(380, 80) };
		vb2.AddChild(new Label { Text = "Puerto (ej. 7777):", HorizontalAlignment = HorizontalAlignment.Center });
		_portEdit = new LineEdit { PlaceholderText = "7777", Text = "7777" };
		vb2.AddChild(_portEdit);
		_hostDialog.AddChild(vb2);
		AddChild(_hostDialog);
		_portEdit.TextSubmitted += (_) => _hostDialog.EmitSignal(AcceptDialog.SignalName.Confirmed);

		// --- diálogo join (ip:puerto) ---
		_joinDialog = new AcceptDialog { Title = "Unirse a partida", MinSize = new Vector2I(460, 240) };
		var vb3 = new VBoxContainer { CustomMinimumSize = new Vector2(400, 100) };
		vb3.AddChild(new Label { Text = "IP:Puerto (ej. 127.0.0.1:7777)", HorizontalAlignment = HorizontalAlignment.Center });
		_joinIpPortEdit = new LineEdit { PlaceholderText = "127.0.0.1:7777", Text = "127.0.0.1:7777" };
		vb3.AddChild(_joinIpPortEdit);
		_joinDialog.AddChild(vb3);
		AddChild(_joinDialog);
		_joinIpPortEdit.TextSubmitted += (_) => _joinDialog.EmitSignal(AcceptDialog.SignalName.Confirmed);

		// --- diálogo lobby (solo info/espera) ---
		_lobbyDialog = new AcceptDialog { Title = "Lobby", MinSize = new Vector2I(500, 260) };
		var vb4 = new VBoxContainer { CustomMinimumSize = new Vector2(460, 120) };
		_lobbyLabel = new Label { Text = "Jugadores (0/0):", AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_lobbyLabel.HorizontalAlignment = HorizontalAlignment.Left;
		vb4.AddChild(_lobbyLabel);
		_lobbyDialog.AddChild(vb4);
		AddChild(_lobbyDialog);
		// Botón OK cierra, pero el host seguirá esperando hasta que se llene; puedes dejarlo abierto.
	}

	// -------- Flujos --------
	private void AskNameThenHost(int maxPlayers)
	{
		_pendingMaxPlayers = maxPlayers;
		_nameEdit.Text = "";
		_nameDialog.Title = $"Nombre del Jugador 1 (host {maxPlayers}P)";
		_nameDialog.Confirmed += OnNameForHostConfirmed;
		_nameDialog.PopupCentered(new Vector2I(420, 200));
		_nameEdit.GrabFocus();
	}
	private void OnNameForHostConfirmed()
	{
		_nameDialog.Confirmed -= OnNameForHostConfirmed;
		var name = string.IsNullOrWhiteSpace(_nameEdit.Text) ? "Host" : _nameEdit.Text.Trim();
		// pedir puerto
		_hostDialog.Confirmed += () => {
			if (!int.TryParse(_portEdit.Text, out int port)) port = 7777;
			GM.HostGame(port, _pendingMaxPlayers, name);
			// Mostrar lobby
			_lobbyLabel.Text = "Esperando jugadores...";
			_lobbyDialog.PopupCentered(new Vector2I(500, 260));
		};
		_hostDialog.PopupCentered(new Vector2I(420, 220));
		_portEdit.GrabFocus();
	}

	private void AskJoin()
	{
		// nombre primero
		_nameEdit.Text = "";
		_nameDialog.Title = "Tu nombre";
		_nameDialog.Confirmed += OnNameForJoinConfirmed;
		_nameDialog.PopupCentered(new Vector2I(420, 200));
		_nameEdit.GrabFocus();
	}
	private void OnNameForJoinConfirmed()
	{
		_nameDialog.Confirmed -= OnNameForJoinConfirmed;
		var name = string.IsNullOrWhiteSpace(_nameEdit.Text) ? "Jugador" : _nameEdit.Text.Trim();

		_joinDialog.Confirmed += () => {
			var txt = _joinIpPortEdit.Text.Trim();
			var parts = txt.Split(':');
			string host = (parts.Length >= 1 && parts[0] != "") ? parts[0] : "127.0.0.1";
			int port = (parts.Length == 2 && int.TryParse(parts[1], out var p)) ? p : 7777;
			GM.JoinGame(host, port, name);
			_lobbyLabel.Text = "Conectado. Esperando inicio...";
			_lobbyDialog.PopupCentered(new Vector2I(500, 260));
		};
		_joinDialog.PopupCentered(new Vector2I(460, 240));
		_joinIpPortEdit.GrabFocus();
	}
}
