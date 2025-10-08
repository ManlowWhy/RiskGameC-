using Godot;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

public partial class NetworkManager : Node
{
	// ===== Señales =====
	[Signal] public delegate void ConnectedEventHandler();                                // (Cliente) Conectado al server
	[Signal] public delegate void DisconnectedEventHandler(string reason);                // (Cliente) Desconectado del server
	[Signal] public delegate void ServerClientConnectedEventHandler(int peerId);          // (Servidor) Nuevo cliente
	[Signal] public delegate void ServerClientDisconnectedEventHandler(int peerId);       // (Servidor) Cliente salió
	[Signal] public delegate void MessageReceivedEventHandler(string json, int peerId);   // Mensaje entrante (peerId emisor en servidor; 0 en cliente)
	[Signal] public delegate void LogEventHandler(string text);                           // Logs

	// ===== Rol =====
	private enum Role { None, Server, Client }
	private Role _role = Role.None;

	// ===== Estado común =====
	private volatile bool _running = false;

	// ===== Servidor =====
	private TcpListener _listener;
	private Thread _acceptThread;

	private class ClientInfo
	{
		public int PeerId;
		public TcpClient Client;
		public NetworkStream Stream;
		public StreamReader Reader;
		public StreamWriter Writer;
		public Thread RxThread;
		public object SendLock = new object();
	}

	private int _nextPeerId = 1; // 1..N
	private readonly Dictionary<int, ClientInfo> _clients = new(); // peerId -> info
	private readonly object _clientsLock = new object();

	// ===== Cliente (incluye auto-conexión del host) =====
	private TcpClient _client;
	private NetworkStream _clientStream;
	private StreamReader _clientReader;
	private StreamWriter _clientWriter;
	private Thread _clientRxThread;

	// ===== Colas para pasar al hilo principal =====
	private readonly ConcurrentQueue<(string json, int peerId)> _inbox = new();
	private readonly ConcurrentQueue<string> _logs = new();

	private volatile bool _pendingConnected = false;
	private string _pendingDisconnectedReason = null;

	public override void _Ready()
	{
		GD.Print("[NetworkManager] Autoload listo.");
	}

	public override void _Process(double delta)
	{
		// Eventos diferidos al hilo principal
		if (_pendingConnected)
		{
			_pendingConnected = false;
			EmitSignal(SignalName.Connected);
			EmitSignal(SignalName.Log, "[NET] Conectado.");
		}
		if (_pendingDisconnectedReason != null)
		{
			var r = _pendingDisconnectedReason;
			_pendingDisconnectedReason = null;
			EmitSignal(SignalName.Disconnected, r);
			EmitSignal(SignalName.Log, "[NET] Desconectado: " + r);
		}
		while (_inbox.TryDequeue(out var item))
			EmitSignal(SignalName.MessageReceived, item.json, item.peerId);

		while (_logs.TryDequeue(out var l))
			EmitSignal(SignalName.Log, l);
	}

	// ================== SERVIDOR (multi-cliente) ==================
	public void StartServer(int port)
	{
		StopAll();
		_role = Role.Server;
		_running = true;

		try
		{
			_listener = new TcpListener(IPAddress.Any, port);
			_listener.Start();
			_logs.Enqueue($"[NET][SRV] Escuchando en 0.0.0.0:{port}");

			_acceptThread = new Thread(AcceptLoop) { IsBackground = true };
			_acceptThread.Start();

			// >>> Auto-conectar el host como cliente local <<<
			ConnectSelfAsClient(port);
		}
		catch (Exception e)
		{
			_logs.Enqueue("[NET][SRV][ERR] " + e.Message);
			_role = Role.None;
			_running = false;
		}
	}

	private void AcceptLoop()
	{
		try
		{
			while (_running)
			{
				var socket = _listener.AcceptTcpClient(); // bloqueante
				socket.NoDelay = true;

				var peerId = _nextPeerId++;
				var info = new ClientInfo
				{
					PeerId = peerId,
					Client = socket,
					Stream = socket.GetStream(),
				};
				info.Reader = new StreamReader(info.Stream, Encoding.UTF8);
				info.Writer = new StreamWriter(info.Stream, new UTF8Encoding(false)) { AutoFlush = true };

				lock (_clientsLock) { _clients[peerId] = info; }

				EmitSignal(SignalName.ServerClientConnected, peerId);
				_logs.Enqueue($"[NET][SRV] Cliente #{peerId} conectado ({socket.Client.RemoteEndPoint})");

				info.RxThread = new Thread(() => ServerReceiveLoop(info)) { IsBackground = true };
				info.RxThread.Start();
			}
		}
		catch (SocketException)
		{
			// Listener parado; salida limpia
		}
		catch (Exception e)
		{
			_logs.Enqueue("[NET][SRV][ERR Accept] " + e.Message);
		}
	}

	private void ServerReceiveLoop(ClientInfo info)
	{
		try
		{
			while (_running && info.Client.Connected)
			{
				var line = info.Reader.ReadLine(); // un JSON por línea
				if (line == null) break;

				// En servidor, encolamos (peerId del emisor) y dejamos que GameManager
				// decida si hay que broadcast (patches) o procesar (cmd_*).
				_inbox.Enqueue((line, info.PeerId));
			}
		}
		catch (IOException) { /* cliente cerró */ }
		catch (Exception e)
		{
			_logs.Enqueue($"[NET][SRV][ERR RX #{info.PeerId}] {e.Message}");
		}
		finally
		{
			// Quitar cliente
			lock (_clientsLock)
			{
				_clients.Remove(info.PeerId);
			}
			try { info.Client.Close(); } catch { }
			EmitSignal(SignalName.ServerClientDisconnected, info.PeerId);
			_logs.Enqueue($"[NET][SRV] Cliente #{info.PeerId} desconectado.");
		}
	}

	// ===== Envío desde SERVIDOR =====
	public void SendJsonToAll(object message)
	{
		var json = JsonSerializer.Serialize(message);
		SendRawJsonToAll(json);
	}

	public void SendJsonToPeer(int peerId, object message)
	{
		var json = JsonSerializer.Serialize(message);
		SendRawJsonToPeer(peerId, json);
	}

	public void SendRawJsonToAll(string json)
	{
		var payload = json + "\n";
		lock (_clientsLock)
		{
			foreach (var kv in _clients)
			{
				var w = kv.Value.Writer;
				try
				{
					lock (kv.Value.SendLock)
						w.Write(payload);
				}
				catch (Exception e)
				{
					_logs.Enqueue($"[NET][SRV][ERR TX #{kv.Key}] {e.Message}");
				}
			}
		}
	}

	public void SendRawJsonToPeer(int peerId, string json)
	{
		var payload = json + "\n";
		ClientInfo info;
		lock (_clientsLock) { _clients.TryGetValue(peerId, out info); }
		if (info == null) return;

		try
		{
			lock (info.SendLock)
				info.Writer.Write(payload);
		}
		catch (Exception e)
		{
			_logs.Enqueue($"[NET][SRV][ERR TX #{peerId}] {e.Message}");
		}
	}

	// ================== CLIENTE ==================
	public void ConnectTo(string host, int port)
	{
		StopAll();
		_role = Role.Client;
		_running = true;

		_logs.Enqueue($"[NET][CLI] Conectando a {host}:{port} ...");

		new Thread(() =>
		{
			try
			{
				_client = new TcpClient();
				_client.NoDelay = true;
				_client.Connect(host, port);

				_clientStream = _client.GetStream();
				_clientReader = new StreamReader(_clientStream, Encoding.UTF8);
				_clientWriter = new StreamWriter(_clientStream, new UTF8Encoding(false)) { AutoFlush = true };

				_pendingConnected = true;

				_clientRxThread = new Thread(ClientReceiveLoop) { IsBackground = true };
				_clientRxThread.Start();
			}
			catch (Exception e)
			{
				_logs.Enqueue("[NET][CLI][ERR] " + e.Message);
				_pendingDisconnectedReason = "connect_failed";
			}
		}) { IsBackground = true }.Start();
	}

	private void ClientReceiveLoop()
	{
		try
		{
			while (_running && _client?.Connected == true)
			{
				var line = _clientReader.ReadLine();
				if (line == null) break;
				// En cliente, el peerId no se conoce (0).
				_inbox.Enqueue((line, 0));
			}
		}
		catch (IOException) { /* server cerró */ }
		catch (Exception e)
		{
			_logs.Enqueue("[NET][CLI][ERR RX] " + e.Message);
		}
		finally
		{
			_pendingDisconnectedReason = "rx_end";
		}
	}

	// ===== Envío desde CLIENTE → SERVIDOR (incluye host auto-conectado) =====
	public void SendJson(object message)
	{
		var json = JsonSerializer.Serialize(message);
		SendRawJson(json);
	}

	public void SendRawJson(string json)
	{
		// Permitir enviar si existe conexión cliente activa (incluye host auto-conectado)
		if (_clientWriter == null) { _logs.Enqueue("[NET][CLI] No hay conexión cliente activa."); return; }

		try
		{
			_clientWriter.Write(json + "\n");
		}
		catch (Exception e)
		{
			_logs.Enqueue("[NET][CLI][ERR TX] " + e.Message);
			_pendingDisconnectedReason = "tx_error";
		}
	}

	// ================== AUTO-CONEXIÓN DEL HOST ==================
	// Conecta el host a sí mismo como cliente sin detener el servidor
	private void ConnectSelfAsClient(int port)
	{
		new Thread(() =>
		{
			try
			{
				var c = new TcpClient();
				c.NoDelay = true;
				c.Connect("127.0.0.1", port);

				_client = c;
				_clientStream = c.GetStream();
				_clientReader = new StreamReader(_clientStream, Encoding.UTF8);
				_clientWriter = new StreamWriter(_clientStream, new UTF8Encoding(false)) { AutoFlush = true };

				_pendingConnected = true;

				_clientRxThread = new Thread(ClientReceiveLoop) { IsBackground = true };
				_clientRxThread.Start();

				_logs.Enqueue("[NET][SRV] Auto-conectado como cliente local.");
			}
			catch (Exception e)
			{
				_logs.Enqueue("[NET][SRV][ERR SelfConnect] " + e.Message);
			}
		}) { IsBackground = true }.Start();
	}

	// ================== PARAR TODO ==================
	public void StopAll()
	{
		_running = false;

		// Cliente
		try { _clientStream?.Close(); } catch { }
		try { _client?.Close(); } catch { }
		try { _clientRxThread?.Join(50); } catch { }
		_clientStream = null; _client = null; _clientReader = null; _clientWriter = null; _clientRxThread = null;

		// Servidor
		try { _listener?.Stop(); } catch { }
		try { _acceptThread?.Join(50); } catch { }
		_acceptThread = null;

		lock (_clientsLock)
		{
			foreach (var kv in _clients)
			{
				try { kv.Value.Client.Close(); } catch { }
			}
			_clients.Clear();
		}

		_role = Role.None;
	}

	public override void _ExitTree() => StopAll();
}
