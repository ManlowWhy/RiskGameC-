using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

// ===== Mensajes base/lobby =====
public class MsgBase { public string type { get; set; } }
public class MsgHello : MsgBase { public string name { get; set; } }                         // cli -> srv
public class MsgLobby : MsgBase { public int max { get; set; } public List<string> names { get; set; } } // srv -> all
public class MsgStart : MsgBase { public int max { get; set; } public List<string> names { get; set; } } // srv -> all

// ===== Comandos (cliente -> host) =====
public class CmdClick : MsgBase { public string terr { get; set; } public string actor { get; set; } }
public class CmdEndPhase : MsgBase { }

// ===== Parches (host -> todos) =====
public class PatchTerr  : MsgBase { public string terr { get; set; } public string ownerId { get; set; } public int tropas { get; set; } }
public class PatchPhase : MsgBase { public string fase { get; set; } public string turno { get; set; } public int refuerzos { get; set; } }

public partial class GameManager : Node
{
	public static GameManager Instance { get; private set; }

	public int NumPlayers { get; private set; } = 2;
	public string[] PlayerNames { get; private set; } = new[] { "Jugador 1", "Guest 2", "Guest 3" };
	[Export] public string GameScenePath { get; set; } = "res://Scenes/mapa.tscn";

	public bool IsOnline { get; private set; }
	public bool IsHost { get; private set; }
	public string LocalName { get; private set; } = "Jugador 1";

	// <<< NUEVO: Id del jugador local ("J1"/"J2"/"J3") >>>
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
		if (_net != null)
		{
			_net.Connected    += () => {
				// al conectar como cliente, me presento
				var hello = new MsgHello { type = "hello", name = LocalName };
				_net.SendJson(hello);
			};
			_net.Disconnected += (string r) => { IsOnline = false; IsHost = false; };

			_net.MessageReceived += (string json, int peerId) => OnNetMessage(json, peerId);
			_net.ServerClientConnected    += (int id) => GD.Print($"[GM][SRV] peer #{id} conectado");
			_net.ServerClientDisconnected += (int id) => { GD.Print($"[GM][SRV] peer #{id} salió"); if (IsHost) ReemitLobby(); };
		}
		else
		{
			GD.PushWarning("[GameManager] No encontré /root/NetworkManager. El modo online no funcionará.");
		}
	}

	// ========= Local (singleplayer rápido) =========
	public void StartGame(int numPlayers) => StartGame(numPlayers, "Jugador 1");
	public void StartGame(int numPlayers, string player1Name)
	{
		IsOnline = false; IsHost = false;
		MaxPlayers = NumPlayers = Mathf.Clamp(numPlayers, 2, 3);

		var p1 = string.IsNullOrWhiteSpace(player1Name) ? "Jugador 1" : player1Name.Trim();
		var p2 = "Guest 2"; var p3 = "Guest 3";
		PlayerNames = NumPlayers == 2 ? new[] { p1, p2 } : new[] { p1, p2, p3 };

		// en singleplayer MyId no importa, pero dejamos J1
		MyId = "J1";

		GetTree().ChangeSceneToFile(GameScenePath);
	}

	// ========= Online =========
	public void HostGame(int port, int maxPlayers, string hostName)
	{
		if (_net == null) { GD.PushError("[GameManager] No hay NetworkManager autoload."); return; }

		IsOnline = true; IsHost = true;
		LocalName = string.IsNullOrWhiteSpace(hostName) ? "Host" : hostName.Trim();
		MaxPlayers = Mathf.Clamp(maxPlayers, 2, 3);

		_lobbyNames.Clear();
		_lobbyNames.Add(LocalName); // host primero
		MyId = "J1";                // <<< host siempre J1

		_net.StartServer(port);     // auto-conecta al host como cliente
		ReemitLobby();
	}

	public void JoinGame(string host, int port, string playerName)
	{
		if (_net == null) { GD.PushError("[GameManager] No hay NetworkManager autoload."); return; }

		IsOnline = true; IsHost = false;
		LocalName = string.IsNullOrWhiteSpace(playerName) ? "Jugador" : playerName.Trim();
		_net.ConnectTo(host, port);
	}

	// ========= Helpers de red =========
	public void SendCmd(object cmd)
	{
		if (!IsOnline) return;
		if (IsHost) OnNetMessage(JsonSerializer.Serialize(cmd), 0);
		else        _net?.SendJson(cmd);
	}
	public void BroadcastPatch(object patch)
	{
		if (IsOnline && IsHost) _net?.SendJsonToAll(patch);
	}

	// ========= Protocolo =========
	private void OnNetMessage(string json, int peerId)
	{
		try
		{
			using var jdoc = JsonDocument.Parse(json);
			if (!jdoc.RootElement.TryGetProperty("type", out var typeEl)) return;
			var type = typeEl.GetString();

			// ---- Servidor ----
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
			// ---- Cliente ----
			else
			{
				if (type == "lobby")
				{
					var lobby = JsonSerializer.Deserialize<MsgLobby>(json);
					MaxPlayers = lobby.max;
					EmitSignal(SignalName.LobbyUpdated, lobby.max, lobby.names.ToArray());
					return;
				}
				else if (type == "start")
				{
					var start = JsonSerializer.Deserialize<MsgStart>(json);
					MaxPlayers = NumPlayers = start.max;
					PlayerNames = start.names.ToArray();

					// <<< NUEVO: determinar MyId por mi nombre en la lista >>>
					int idx = Array.FindIndex(PlayerNames, n => string.Equals(n, LocalName, StringComparison.Ordinal));
					if (idx < 0) idx = 0;
					MyId = idx == 0 ? "J1" : (idx == 1 ? "J2" : "J3");

					GetTree().ChangeSceneToFile(GameScenePath);
					return;
				}
			}

			// ---- Parches (host -> todos) ----
			if (type == "patch_terr" || type == "patch_phase")
			{
				var mapa = GetTree().CurrentScene?.GetNodeOrNull("MapaUI") as Node;
				(mapa as IAplicaParches)?.ApplyNetPatch(json);
				return;
			}

			// ---- Comandos (cliente -> host) ----
			if (IsHost && (type == "cmd_click" || type == "cmd_end_phase"))
			{
				var mapa = GetTree().CurrentScene?.GetNodeOrNull("MapaUI") as Node;
				(mapa as IProcesaComandos)?.ProcessNetCommand(json);
				return;
			}
		}
		catch (Exception e)
		{
			GD.PushWarning("[GameManager][Net] JSON error: " + e.Message + " :: " + json);
		}
	}

	// ==== Lobby helpers (host) ====
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
		MyId        = "J1"; // host

		GetTree().ChangeSceneToFile(GameScenePath);
	}

	public void QuitGame() => GetTree().Quit();
}
