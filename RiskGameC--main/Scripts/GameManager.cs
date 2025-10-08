using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

// LOBBY
public class MsgBase { public string type { get; set; } }
public class MsgHello : MsgBase { public string name { get; set; } }                                  // CLI→SRV
public class MsgLobby : MsgBase { public int max { get; set; } public List<string> names { get; set; } } // SRV→ALL
public class MsgStart : MsgBase { public int max { get; set; } public List<string> names { get; set; } } // SRV→ALL

// COMANDOS
public class CmdClick : MsgBase { public string terr { get; set; } public string actor { get; set; } }
public class CmdEndPhase : MsgBase { }

// PARCHES
public class PatchTerr  : MsgBase { public string terr { get; set; } public string ownerId { get; set; } public int tropas { get; set; } }
public class PatchPhase : MsgBase { public string fase { get; set; } public string turno { get; set; } public int refuerzos { get; set; } }

public partial class GameManager : Node
{
	public static GameManager Instance { get; private set; }

	public int NumPlayers { get; private set; } = 2;
	public string[] PlayerNames { get; private set; } = new[] { "Jugador 1", "Guest 2", "Guest 3" };
	[Export] public string GameScenePath { get; set; } = "res://Scenes/mapa.tscn";

	public bool IsOnline { get; private set; }
	public bool IsHost   { get; private set; }
	public string LocalName { get; private set; } = "Jugador 1";

	// ID LOCAL
	public string MyId { get; private set; } = null;

	public int MaxPlayers { get; private set; } = 2;

	private readonly List<string> _lobbyNames = new();
	private NetworkManager _net;

	[Signal] public delegate void LobbyUpdatedEventHandler(int max, string[] names);

	public override void _EnterTree()
	{
		if (Instance != null && Instance != this) { QueueFree(); return; }
		Instance = this;
		ProcessMode = ProcessModeEnum.Always;
	}

	public override void _Ready()
	{
		_net = GetNodeOrNull<NetworkManager>("/root/NetworkManager");
		if (_net == null)
		{
			GD.PushWarning("[GameManager] No encontré /root/NetworkManager. El modo online no funcionará.");
			return;
		}

		_net.Connected += () =>
		{
			// HELLO
			var hello = new MsgHello { type = "hello", name = LocalName };
			_net.SendJson(hello);
		};
		_net.Disconnected += (string r) => { IsOnline = false; IsHost = false; };

		_net.MessageReceived += (string json, int peerId) => OnNetMessage(json, peerId);
		_net.ServerClientConnected    += (int id) => GD.Print($"[GM][SRV] peer #{id} conectado");
		_net.ServerClientDisconnected += (int id) => { GD.Print($"[GM][SRV] peer #{id} salió"); if (IsHost) ReemitLobby(); };
	}

	// SINGLEPLAYER
	public void StartGame(int numPlayers) => StartGame(numPlayers, "Jugador 1");

	public void StartGame(int numPlayers, string player1Name)
	{
		IsOnline = false; IsHost = false;
		MaxPlayers = NumPlayers = Mathf.Clamp(numPlayers, 2, 3);

		var p1 = string.IsNullOrWhiteSpace(player1Name) ? "Jugador 1" : player1Name.Trim();
		var p2 = "Guest 2"; var p3 = "Guest 3";
		PlayerNames = NumPlayers == 2 ? new[] { p1, p2 } : new[] { p1, p2, p3 };

		MyId = "J1"; // J1

		GetTree().ChangeSceneToFile(GameScenePath);
	}

	// ONLINE
	public void HostGame(int port, int maxPlayers, string hostName)
	{
		if (_net == null) { GD.PushError("[GameManager] No hay NetworkManager autoload."); return; }

		IsOnline = true; IsHost = true;
		LocalName = string.IsNullOrWhiteSpace(hostName) ? "Host" : hostName.Trim();
		MaxPlayers = Mathf.Clamp(maxPlayers, 2, 3);

		_lobbyNames.Clear();
		_lobbyNames.Add(LocalName); // HOST
		MyId = "J1";                // J1

		_net.StartServer(port);     // AUTOCONECT
		ReemitLobby();
	}

	public void JoinGame(string host, int port, string playerName)
	{
		if (_net == null) { GD.PushError("[GameManager] No hay NetworkManager autoload."); return; }

		IsOnline = true; IsHost = false;
		LocalName = string.IsNullOrWhiteSpace(playerName) ? "Jugador" : playerName.Trim();
		_net.ConnectTo(host, port);
	}

	// RED
	public void SendCmd(object cmd)
	{
		if (!IsOnline) return;
		// HOST FAST-PATH
		if (IsHost) OnNetMessage(JsonSerializer.Serialize(cmd), 0);
		else        _net?.SendJson(cmd);
	}

	public void BroadcastPatch(object patch)
	{
		if (IsOnline && IsHost) _net?.SendJsonToAll(patch);
	}

	// PROTOCOLO
	private void OnNetMessage(string json, int peerId)
	{
		try
		{
			using var jdoc = JsonDocument.Parse(json);
			if (!jdoc.RootElement.TryGetProperty("type", out var typeEl)) return;
			var type = typeEl.GetString() ?? "";

			// SERVIDOR
			if (IsHost)
			{
				if (type == "hello")
				{
					var hello = JsonSerializer.Deserialize<MsgHello>(json);
					if (!_lobbyNames.Contains(hello.name))
						_lobbyNames.Add(hello.name);

					ReemitLobby();

					if (_lobbyNames.Count >= MaxPlayers)
						SendStartAndLoad();
					return;
				}
			}
			// CLIENTE
			else
			{
				if (type == "lobby")
				{
					var lobby = JsonSerializer.Deserialize<MsgLobby>(json);
					MaxPlayers = lobby.max;
					EmitSignal(SignalName.LobbyUpdated, lobby.max, lobby.names.ToArray());
					return;
				}
				if (type == "start")
				{
					var start = JsonSerializer.Deserialize<MsgStart>(json);
					MaxPlayers = NumPlayers = start.max;
					PlayerNames = start.names.ToArray();

					// MYID
					int idx = Array.FindIndex(PlayerNames, n => string.Equals(n, LocalName, StringComparison.Ordinal));
					if (idx < 0) idx = 0;
					MyId = idx == 0 ? "J1" : (idx == 1 ? "J2" : "J3");

					GetTree().ChangeSceneToFile(GameScenePath);
					return;
				}
			}

			// PARCHES
			if (type.StartsWith("patch_", StringComparison.OrdinalIgnoreCase)
				|| type == "start_defense"
				|| type == "battle_result")
			{
				var mapa = FindMapaUINode();
				(mapa as IAplicaParches)?.ApplyNetPatch(json);
				return;
			}

			// COMANDOS
			if (IsHost && type.StartsWith("cmd_", StringComparison.OrdinalIgnoreCase))
			{
				var mapa = FindMapaUINode();
				(mapa as IProcesaComandos)?.ProcessNetCommand(json);
				return;
			}
		}
		catch (Exception e)
		{
			GD.PushWarning("[GameManager][Net] JSON error: " + e.Message + " :: " + json);
		}
	}

	// LOBBY HOST
	private void ReemitLobby()
	{
		if (!IsHost || _net == null) return;
		var msg = new MsgLobby { type = "lobby", max = MaxPlayers, names = new List<string>(_lobbyNames) };
		_net.SendJsonToAll(msg);
		EmitSignal(SignalName.LobbyUpdated, msg.max, msg.names.ToArray());
	}

	private void SendStartAndLoad()
	{
		if (!IsHost || _net == null) return;

		var msg = new MsgStart { type = "start", max = MaxPlayers, names = new List<string>(_lobbyNames) };
		_net.SendJsonToAll(msg);

		NumPlayers  = MaxPlayers;
		PlayerNames = msg.names.ToArray();
		MyId        = "J1"; // J1

		GetTree().ChangeSceneToFile(GameScenePath);
	}

	// LOCALIZADOR MAPAUI
	private Node FindMapaUINode()
	{
		var scene = GetTree().CurrentScene;
		if (scene == null) return null;

		// ROOT
		if (scene is IAplicaParches || scene is IProcesaComandos)
			return scene;

		// NOMBRE
		var byName = scene.GetNodeOrNull<Node>("MapaUI");
		if (byName != null) return byName;

		// DFS
		Node found = null;
		void DFS(Node n)
		{
			if (found != null || n == null) return;
			if (n is IAplicaParches || n is IProcesaComandos) { found = n; return; }
			foreach (Node ch in n.GetChildren()) DFS(ch);
		}
		DFS(scene);
		return found;
	}

	public void QuitGame() => GetTree().Quit();
}
