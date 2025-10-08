using Godot;
using Scripts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;   // json
using System.Linq;        // linq

using NodoTerreno = global::Terreno;

// online
public interface IAplicaParches { void ApplyNetPatch(string json); }
public interface IProcesaComandos { void ProcessNetCommand(string json); }

public partial class MapaUI : Node2D, IAplicaParches, IProcesaComandos
{
	// neutral
	private const string NEUTRAL_ID = "NEUTRAL";
	private const int NEUTRAL_TROOPS_PER_TERR = 3;

	// jugadores
	private Jugador j1;
	private Jugador j2;
	private Jugador j3; // jugador_3
	private readonly List<Jugador> _jugList = new();
	private readonly Dictionary<string, Jugador> _playerById = new();
	private Jugador jugadorActual;

	// refuerzos
	private readonly Dictionary<string, int> _bolsaRefuerzos = new();

	// seleccion
	private NodoTerreno origenSeleccionado = null;
	private NodoTerreno destinoSeleccionado = null;

	// fases
	private string faseTurno = "refuerzo";
	private string faseDados = "";         // dados
	private string _turnOwnerId = "J1";    // turno

	// ataque/defensa
	private int tropasAtaqueSeleccionadas = 1;
	private int tropasDefensaSeleccionadas = 1;
	private readonly List<int> dadosAtq = new();
	private readonly List<int> dadosDef = new();
	private readonly RandomNumberGenerator rng = new();

	// cartas
	private Scripts.MazoCartas _mazo;
	private Scripts.FiboCounter _fibo = new();

	// online
	private bool _isOnline, _isHost, _authoritative;
	private NetworkManager _net;
	private readonly Dictionary<Jugador, bool> _recibioCartaTurno = new();

	// hud paths
	[Export] private NodePath TurnoLabelPath;
	[Export] private NodePath ResultadoDadoPath;
	[Export] private NodePath LanzarDadoPath;
	[Export] private NodePath FinalizarRefuerzosPath;
	[Export] private NodePath CantidadAtaquePath;
	[Export] private NodePath CantidadDefensaPath;
	[Export] private NodePath IrAAtaquePath;
	[Export] private NodePath FinalizarTurnoPath;
	[Export] private NodePath CartasLabelPath;
	[Export] private NodePath CanjearCartasPath;
	private Label _cartasLabel;

	// hud refs
	private Label _turnoLabel, _resultado;
	private Button _btnLanzar, _btnFinRef;
	private SpinBox _spAtk, _spDef;
	private Button _btnIrAtaque;
	private Button _btnFinTurno;
	private Button _btnCanjear;

	// defensa ui
	private Control _defensePanel;
	private Label _defenseTitle;
	private SpinBox _defenseSpin;
	private Button _defenseDiceBtn;
	private string _defenderOwnerId = "";
	private bool _esperandoDefensa = false;
	private int _turnOwnerCardsCount = 0;

	//victoria
	public enum VictoryCondition
	{
		LastPlayerStanding = 0, // Gana cuando solo queda un jugador con territorios
		FirstConquest      = 1  // Gana el primero que conquiste algún territorio
	}
	[Export] public VictoryCondition VictoryRule { get; set; } = VictoryCondition.LastPlayerStanding;
	private bool _victoryDeclared = false;
	// utils
	private static bool NodeVivo(Node n) => n != null && GodotObject.IsInstanceValid(n) && n.IsInsideTree();

	// colores
	private readonly Dictionary<string, Color> _colorPorId = new()
	{
		["J1"] = new Color(1f, 0.984f, 0f),
		["J2"] = new Color(0.78f, 0f, 0.78f),
		["J3"] = new Color(0.2f, 0.8f, 0.2f),
		["NEUTRAL"] = new Color(0.6f, 0.6f, 0.6f),
	};

	// adyacencias
	private readonly Dictionary<NodoTerreno, HashSet<string>> _ady = new();

	public override void _Ready()
	{
		// hud
		_turnoLabel = GetNodeOrNull<Label>(TurnoLabelPath ?? "HUD/TurnoLabel");
		_resultado  = GetNodeOrNull<Label>(ResultadoDadoPath ?? "HUD/ResultadoDado");
		_btnLanzar  = GetNodeOrNull<Button>(LanzarDadoPath ?? "HUD/LanzarDado");
		_btnFinRef  = GetNodeOrNull<Button>(FinalizarRefuerzosPath ?? "HUD/FinalizarRefuerzos");
		_spAtk      = GetNodeOrNull<SpinBox>(CantidadAtaquePath ?? "HUD/CantidadAtaque");
		_spDef      = GetNodeOrNull<SpinBox>(CantidadDefensaPath ?? "HUD/CantidadDefensa");
		_btnCanjear = GetNodeOrNull<Button>(CanjearCartasPath ?? "HUD/CanjearCartasBtn");

		if (NodeVivo(_btnLanzar)) { _btnLanzar.Pressed += OnLanzarDado; _btnLanzar.Visible = false; }
		if (NodeVivo(_btnFinRef)) { _btnFinRef.Pressed += OnFinalizarRefuerzos; _btnFinRef.Visible = false; }
		_spAtk?.Hide();
		_spDef?.Hide();

		_btnIrAtaque = GetNodeOrNull<Button>(IrAAtaquePath ?? "HUD/IrAAtaqueBtn");
		if (NodeVivo(_btnIrAtaque))
		{
			_btnIrAtaque.Text = "Ir a ATAQUE";
			_btnIrAtaque.Pressed += OnIrAAtaquePressed;
			_btnIrAtaque.Visible = false;
		}

		_btnFinTurno = GetNodeOrNull<Button>(FinalizarTurnoPath ?? "HUD/FinalizarTurnoBtn");
		if (NodeVivo(_btnFinTurno))
		{
			_btnFinTurno.Disabled = false;
			_btnFinTurno.Text = "Terminar turno";
			_btnFinTurno.Pressed += OnTerminarTurnoPressed;
			_btnFinTurno.Visible = false;
		}

		if (NodeVivo(_btnCanjear))
		{
			_btnCanjear.Text = "Canjear cartas";
			_btnCanjear.Pressed += OnCanjearCartasPressed;
			_btnCanjear.Visible = false;
		}

		_cartasLabel = GetNodeOrNull<Label>(CartasLabelPath ?? "HUD/CartasLabel");

		// defensa ui
		_defensePanel = GetNodeOrNull<Control>("HUD/DefensePanel");
		if (_defensePanel == null)
		{
			_defensePanel = new PanelContainer { Name = "DefensePanel", Visible = false };
			var vb = new VBoxContainer { Name = "VBox" };
			_defenseTitle   = new Label   { Name = "Title", Text = "Defensa: elige dados" };
			_defenseSpin    = new SpinBox { Name = "Spin", MinValue = 1, MaxValue = 2, Step = 1, Value = 1 };
			_defenseDiceBtn = new Button  { Name = "Btn", Text = "Lanzar defensa" };
			_defenseDiceBtn.Pressed += OnDefenderLanzaDados;
			vb.AddChild(_defenseTitle); vb.AddChild(_defenseSpin); vb.AddChild(_defenseDiceBtn);
			(_defensePanel as PanelContainer).AddChild(vb);
			GetNodeOrNull<Node>("HUD")?.AddChild(_defensePanel);
		}
		else
		{
			_defenseTitle   = _defensePanel.GetNodeOrNull<Label>("Title");
			_defenseSpin    = _defensePanel.GetNodeOrNull<SpinBox>("Spin");
			_defenseDiceBtn = _defensePanel.GetNodeOrNull<Button>("Btn");
			if (_defenseDiceBtn != null) _defenseDiceBtn.Pressed += OnDefenderLanzaDados;
		}

		var hud = GetNodeOrNull<Control>("HUD");
		if (hud != null) hud.MouseFilter = Control.MouseFilterEnum.Stop;

		// jugadores
		var gm         = GameManager.Instance;
		var numPlayers = gm?.NumPlayers ?? 2;
		var names      = gm?.PlayerNames
						?? (numPlayers == 3 ? new[] { "Jugador 1", "Guest 2", "Guest 3" }
											: new[] { "Jugador 1", "Guest 2" });

		j1 = new Jugador { Alias = names[0], Color = "Rojo",  TropasDisponibles = 40 };
		j2 = new Jugador { Alias = names[1], Color = "Azul",  TropasDisponibles = 40 };
		_jugList.Add(j1);
		_jugList.Add(j2);
		if (numPlayers == 3)
		{
			j3 = new Jugador { Alias = names[2], Color = "Verde", TropasDisponibles = 40 };
			_jugList.Add(j3);
		}
		_playerById.Clear();
		_playerById["J1"] = j1;
		_playerById["J2"] = j2;
		if (numPlayers == 3) _playerById["J3"] = j3;

		// online
		_isOnline = GameManager.Instance?.IsOnline ?? false;
		_isHost   = GameManager.Instance?.IsHost   ?? false;
		_authoritative = !_isOnline || _isHost;
		_net = GetNodeOrNull<NetworkManager>("/root/NetworkManager");

		// cartas
		if (_isHost)
		{
			var ids = new List<string>();
			var terrNodes = GetTree().GetNodesInGroup("Terreno");
			foreach (Node n in terrNodes)
			{
				if (n is NodoTerreno t)
				{
					string id = NormalizarNombre(t.Nombre ?? t.Name);
					if (!string.IsNullOrEmpty(id)) ids.Add(id);
				}
			}
			try { _mazo = new Scripts.MazoCartas(ids); }
			catch { _mazo = null; }
			_fibo?.Reset();
		}
		ActualizarHUDCartas(jugadorActual);

		// territorios
		var terrRoot = GetNodeOrNull<Node>("Territorios");
		if (terrRoot != null)
		{
			foreach (Node child in terrRoot.GetChildren())
				if (child is NodoTerreno tt && !tt.IsInGroup("Terreno")) tt.AddToGroup("Terreno");
		}

		var lista = GetTree().GetNodesInGroup("Terreno");
		foreach (Node node in lista)
		{
			if (node is NodoTerreno t)
			{
				if (string.IsNullOrWhiteSpace(t.Nombre)) t.Nombre = t.Name;
				t.Connect(NodoTerreno.SignalName.Clicked,  Callable.From<NodoTerreno>(OnTerrenoClicked));
				t.Connect(NodoTerreno.SignalName.Hovered, Callable.From<NodoTerreno, bool>(OnTerrenoHovered));
				CreateOrGetTroopLabel(t);
				ActualizarContadorTropas(t);
			}
		}

		// refuerzos
		_bolsaRefuerzos["J1"] = 0;
		_bolsaRefuerzos["J2"] = 0;
		_bolsaRefuerzos["J3"] = 0;

		// adyacencias
		ConstruirAdyacencia(lista);

		// reparto
		var todos = new List<NodoTerreno>();
		foreach (Node node in lista) if (node is NodoTerreno tt) todos.Add(tt);

		bool usarNeutral = (numPlayers == 2);
		DistribuirTerrenosInicial(todos, usarNeutral);

		rng.Randomize();

		// turno
		jugadorActual = j1;
		_turnOwnerId  = "J1";
		IniciarTurno();
		ActualizarInteractividadPorTurno();
	} // _Ready

	// adyacencias
	private void ConstruirAdyacencia(Godot.Collections.Array<Node> lista)
	{
		_ady.Clear();
		foreach (Node node in lista)
			if (node is NodoTerreno t)
				_ady[t] = new HashSet<string>();

		InferirVecinosPorGeometria(lista, 6f);
	}

	private void InferirVecinosPorGeometria(Godot.Collections.Array<Node> lista, float margenPx)
	{
		var polys = new List<(NodoTerreno t, Vector2[] pts)>();
		foreach (Node node in lista)
		{
			if (node is not NodoTerreno t) continue;
			var poly = t.GetNodeOrNull<Polygon2D>("Polygon2D");
			if (poly == null || poly.Polygon == null || poly.Polygon.Length < 3) continue;

			var xf = poly.GetGlobalTransform();
			var src = poly.Polygon;
			var dst = new Vector2[src.Length];
			for (int i = 0; i < src.Length; i++) dst[i] = xf * src[i];

			polys.Add((t, dst));
		}

		for (int i = 0; i < polys.Count; i++)
		for (int j = i + 1; j < polys.Count; j++)
		{
			var (ta, A) = polys[i];
			var (tb, B) = polys[j];

			if (PoligonosTocan(A, B, margenPx))
			{
				var nb1 = NormalizarNombre(tb.Nombre);
				var nb2 = NormalizarNombre(tb.Name);
				if (!_ady.TryGetValue(ta, out var setA)) { setA = new HashSet<string>(); _ady[ta] = setA; }
				setA.Add(nb1); setA.Add(nb2);

				var na1 = NormalizarNombre(ta.Nombre);
				var na2 = NormalizarNombre(ta.Name);
				if (!_ady.TryGetValue(tb, out var setB)) { setB = new HashSet<string>(); _ady[tb] = setB; }
				setB.Add(na1); setB.Add(na2);
			}
		}
	}

	private static bool PoligonosTocan(Vector2[] A, Vector2[] B, float m)
	{
		if (!AABBInflada(A, m).Intersects(AABBInflada(B, m))) return false;
		if (HaySegmentosQueCruzan(A, B)) return true;
		if (DistanciaMinimaEntrePoligonos(A, B) <= m) return true;
		if (Geometry2D.IsPointInPolygon(A[0], B) || Geometry2D.IsPointInPolygon(B[0], A)) return true;
		return false;
	}
	private static Rect2 AABBInflada(Vector2[] P, float m)
	{
		var r = new Rect2(P[0], Vector2.Zero);
		for (int i = 1; i < P.Length; i++) r = r.Expand(P[i]);
		r.Position -= new Vector2(m, m);
		r.Size += new Vector2(2*m, 2*m);
		return r;
	}
	private static bool HaySegmentosQueCruzan(Vector2[] A, Vector2[] B)
	{
		for (int i = 0; i < A.Length; i++)
		{
			var a1 = A[i]; var a2 = A[(i+1)%A.Length];
			for (int j = 0; j < B.Length; j++)
			{
				var b1 = B[j]; var b2 = B[(j+1)%B.Length];
				var hit = Geometry2D.SegmentIntersectsSegment(a1, a2, b1, b2);
				if (hit.VariantType != Variant.Type.Nil) return true;
			}
		}
		return false;
	}
	private static float DistanciaMinimaEntrePoligonos(Vector2[] A, Vector2[] B)
	{
		float best = float.PositiveInfinity;
		for (int i = 0; i < A.Length; i++)
		{
			var a1 = A[i]; var a2 = A[(i+1)%A.Length];
			for (int j = 0; j < B.Length; j++)
			{
				var b1 = B[j]; var b2 = B[(j+1)%B.Length];
				best = Mathf.Min(best, DistanciaMinimaEntreSegmentos(a1, a2, b1, b2));
				if (best == 0) return 0;
			}
		}
		return best;
	}
	private static float DistanciaMinimaEntreSegmentos(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
	{
		float d = float.PositiveInfinity;
		d = Mathf.Min(d, DistanciaPuntoASegmento(a1, b1, b2));
		d = Mathf.Min(d, DistanciaPuntoASegmento(a2, b1, b2));
		d = Mathf.Min(d, DistanciaPuntoASegmento(b1, a1, a2));
		d = Mathf.Min(d, DistanciaPuntoASegmento(b2, a1, a2));
		return d;
	}
	private static float DistanciaPuntoASegmento(Vector2 p, Vector2 s1, Vector2 s2)
	{
		var seg = s2 - s1;
		float l2 = seg.LengthSquared();
		if (l2 == 0) return p.DistanceTo(s1);
		float t = Mathf.Clamp(((p - s1).Dot(seg)) / l2, 0, 1);
		var proy = s1 + t * seg;
		return p.DistanceTo(proy);
	}

	private static string NormalizarNombre(string s)
	{
		if (string.IsNullOrWhiteSpace(s)) return "";
		s = s.ToUpperInvariant();
		var sbNoWs = new StringBuilder(s.Length);
		foreach (var ch in s) if (!char.IsWhiteSpace(ch)) sbNoWs.Append(ch);
		s = sbNoWs.ToString();
		var norm = s.Normalize(NormalizationForm.FormD);
		var sb = new StringBuilder(norm.Length);
		foreach (var ch in norm)
		{
			var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
			if (uc != UnicodeCategory.NonSpacingMark) sb.Append(ch);
		}
		return sb.ToString().Normalize(NormalizationForm.FormC);
	}

	// hud
	private void OnLanzarDado()
	{
		if (origenSeleccionado == null || destinoSeleccionado == null) return;

		// movimiento
		if (faseTurno == "movimiento")
		{
			int n = (int)(_spAtk?.Value ?? 1);
			n = Mathf.Clamp(n, 1, Math.Max(0, (origenSeleccionado?.Tropas ?? 1) - 1));

			if (!_authoritative)
			{
				var myId = GameManager.Instance?.MyId;
				if (string.IsNullOrEmpty(myId) || myId != _turnOwnerId) return;

				var cmd = new
				{
					type   = "cmd_move",
					actor  = myId,
					n      = n,
					origen = NormalizarNombre(origenSeleccionado?.Nombre ?? ""),
					destino= NormalizarNombre(destinoSeleccionado?.Nombre ?? "")
				};
				GameManager.Instance.SendCmd(cmd);
				return;
			}

			AplicarMovimiento(origenSeleccionado, destinoSeleccionado, n);
			return;
		}

		// ataque
		if (!_authoritative)
		{
			var myId = GameManager.Instance?.MyId;
			if (string.IsNullOrEmpty(myId) || myId != _turnOwnerId) return;

			int atk = (int)(_spAtk?.Value ?? 1);
			atk = Mathf.Clamp(atk, 1, 3);

			var cmd = new {
				type   = "cmd_attack",
				actor  = myId,
				atk    = atk,
				origen = NormalizarNombre(origenSeleccionado?.Nombre ?? ""),
				destino= NormalizarNombre(destinoSeleccionado?.Nombre ?? "")
			};
			GameManager.Instance.SendCmd(cmd);
			return;
		}

		// defensa_inicio
		tropasAtaqueSeleccionadas = (int)(_spAtk?.Value ?? 1);
		tropasAtaqueSeleccionadas = Mathf.Clamp(tropasAtaqueSeleccionadas, 1, Mathf.Min(3, Math.Max(0, origenSeleccionado.Tropas - 1)));

		_defenderOwnerId = destinoSeleccionado.DuenoId;

		// defensa_neutral
		if (_defenderOwnerId == NEUTRAL_ID)
		{
			ResolverDefensaNeutralAuto();
			_btnLanzar?.Hide();
			_spAtk?.Hide();
			_spDef?.Hide();
			ActualizarInteractividadPorTurno();
			return;
		}

		// defensa_humano
		var maxDef = Mathf.Clamp(destinoSeleccionado.Tropas, 1, 2);
		var startDef = new
		{
			type = "start_defense",
			defender = _defenderOwnerId,
			max = maxDef,
			origen = NormalizarNombre(origenSeleccionado.Nombre),
			destino = NormalizarNombre(destinoSeleccionado.Nombre),
			atk = tropasAtaqueSeleccionadas
		};
		GameManager.Instance.BroadcastPatch(startDef);
		_esperandoDefensa = true;

		_btnLanzar?.Hide();
		_spAtk?.Hide();
		_spDef?.Hide();
		ActualizarInteractividadPorTurno();
	}

	private void OnIrAAtaquePressed()
	{
		if (!_authoritative)
		{
			GameManager.Instance?.SendCmd(new { type = "cmd_end_phase" });
			return;
		}

		if (faseTurno != "movimiento") return;

		faseTurno = "ataque";
		ResetSeleccionYHUD();
		BroadcastPhase();
		ActualizarUI();
		ActualizarInteractividadPorTurno();
	}

	private void OnTerminarTurnoPressed()
	{
		if (!_authoritative)
		{
			GameManager.Instance?.SendCmd(new { type = "cmd_end_phase" });
			return;
		}

		if (faseTurno == "ataque")
		{
			OnFinalizarMovimiento();
		}
	}

	private void OnCanjearCartasPressed()
	{
		if (jugadorActual == null) return;

		// cartas_cliente
		if (!_authoritative)
		{
			GameManager.Instance?.SendCmd(new { type = "cmd_exchange" });
			return;
		}

		// cartas_host
		if (!jugadorActual.TieneTrioValido(out _))
		{
			if (_resultado != null)
				_resultado.Text = "No tienes un trío válido (3 iguales o 1 de cada).";

			var pid = GetOwnerId(jugadorActual);
			var pxInv = new { type = "patch_exchange_invalid", actor = pid, reason = "no_trio" };
			GameManager.Instance.BroadcastPatch(pxInv);
			return;
		}

		AplicarAutocanjeYSumarFibo(jugadorActual);
		ActualizarInteractividadPorTurno();
	}

	// movimiento
	private void AplicarMovimiento(NodoTerreno orig, NodoTerreno dest, int n)
	{
		if (orig == null || dest == null) return;
		if (faseTurno != "movimiento") return;
		if (GetOwnerId(jugadorActual) != _turnOwnerId) return;
		if (orig.DuenoId != GetOwnerId(jugadorActual)) return;
		if (dest.DuenoId != GetOwnerId(jugadorActual)) return;
		if (!SonVecinos(orig, dest)) return;
		if (orig.Tropas <= 1) return;

		n = Mathf.Clamp(n, 1, Math.Max(0, orig.Tropas - 1));

		orig.SetTropas(orig.Tropas - n);
		dest.SetTropas(dest.Tropas + n);
		ActualizarContadorTropas(orig);
		ActualizarContadorTropas(dest);
		BroadcastTerr(orig);
		BroadcastTerr(dest);

		origenSeleccionado = null;
		destinoSeleccionado = null;
		_spAtk?.Hide();
		_btnLanzar?.Hide();
		ActualizarHUDTrasSeleccion();
		ActualizarInteractividadPorTurno();
	}

	// fase_refuerzo_fin
	private void OnFinalizarRefuerzos()
	{
		if (!_authoritative)
		{
			GameManager.Instance.SendCmd(new { type = "cmd_end_phase" });
			return;
		}

		faseTurno = "movimiento";
		_btnFinRef?.Hide();
		ResetSeleccionYHUD();
		BroadcastPhase();
		ActualizarUI();
		ActualizarInteractividadPorTurno();
	}

	// fase_movimiento_fin
	private void OnFinalizarMovimiento()
	{
		if (!_authoritative)
		{
			GameManager.Instance.SendCmd(new { type = "cmd_end_phase" });
			return;
		}

		if (faseTurno == "movimiento")
		{
			faseTurno = "ataque";
			ResetSeleccionYHUD();
			BroadcastPhase();
			ActualizarUI();
			ActualizarInteractividadPorTurno();
			return;
		}

		faseTurno = "refuerzo";
		ResetSeleccionYHUD();
		BroadcastPhase();
		CambiarTurno();
		ActualizarInteractividadPorTurno();
	}

	// mapa_click
	private void OnTerrenoClicked(NodoTerreno t)
	{
		if (t == null) return;

		// cliente
		if (!_authoritative)
		{
			var myId = GameManager.Instance?.MyId;
			if (string.IsNullOrEmpty(myId) || myId != _turnOwnerId) return;

			var terrId = NormalizarNombre(t.Nombre);
			var cmd = new CmdClick { type = "cmd_click", terr = terrId, actor = myId };
			GameManager.Instance.SendCmd(cmd);

			// cliente_movimiento
			if (faseTurno == "movimiento")
			{
				if (origenSeleccionado == null)
				{
					if (t.DuenoId == myId && t.Tropas > 1)
					{
						origenSeleccionado = t;
						destinoSeleccionado = null;
						_spDef?.Hide();
						AjustarMaximosDados();
						ActualizarHUDTrasSeleccion();
						ActualizarInteractividadPorTurno();
					}
					return;
				}

				if (destinoSeleccionado == null)
				{
					if (t != origenSeleccionado && t.DuenoId == myId && SonVecinos(origenSeleccionado, t))
					{
						destinoSeleccionado = t;
						AjustarMaximosDados();
						ActualizarHUDTrasSeleccion();
						ActualizarInteractividadPorTurno();
					}
					return;
				}

				origenSeleccionado = null; destinoSeleccionado = null;
				_btnLanzar?.Hide();
				_spAtk?.Hide();
				_spDef?.Hide();
				ActualizarHUDTrasSeleccion();
				ActualizarInteractividadPorTurno();
				return;
			}

			// cliente_ataque
			if (faseTurno == "ataque")
			{
				if (origenSeleccionado == null)
				{
					if (t.DuenoId == myId && t.Tropas > 1)
					{
						origenSeleccionado = t;
						_spAtk?.Hide();
						_spDef?.Hide();
						ActualizarHUDTrasSeleccion();
						ActualizarInteractividadPorTurno();
					}
					return;
				}

				if (destinoSeleccionado == null)
				{
					if (t != origenSeleccionado && t.DuenoId != myId && SonVecinos(origenSeleccionado, t))
					{
						destinoSeleccionado = t;
						AjustarMaximosDados();
						ActualizarHUDTrasSeleccion();
						ActualizarInteractividadPorTurno();
					}
					return;
				}

				origenSeleccionado = null; destinoSeleccionado = null;
				_btnLanzar?.Hide();
				_spAtk?.Hide();
				_spDef?.Hide();
				ActualizarHUDTrasSeleccion();
				ActualizarInteractividadPorTurno();
				return;
			}

			return;
		}

		// host_refuerzo
		if (faseTurno == "refuerzo")
		{
			var pid = GetOwnerId(jugadorActual);
			if (string.IsNullOrEmpty(pid)) return;

			int bolsa = GetPool(pid);

			if (EsDueno(t, jugadorActual) && bolsa > 0)
			{
				t.SetTropas(t.Tropas + 1);
				ActualizarContadorTropas(t);
				BroadcastTerr(t);

				SetPool(pid, bolsa - 1);
				EnviarPatchBolsa(pid);

				if (GetPool(pid) == 0)
				{
					faseTurno = "movimiento";
					_btnFinRef?.Hide();
				}

				BroadcastPhase();
				ActualizarUI();
				ActualizarInteractividadPorTurno();
			}
			return;
		}

		// host_ataque
		if (faseTurno == "ataque")
		{
			if (origenSeleccionado == null)
			{
				if (EsDueno(t, jugadorActual) && t.Tropas > 1)
				{
					origenSeleccionado = t;
					_spAtk?.Hide();
					_spDef?.Hide();
					_btnLanzar?.Show();
					ActualizarHUDTrasSeleccion();
					ActualizarInteractividadPorTurno();
				}
				return;
			}

			if (destinoSeleccionado == null)
			{
				if (t == origenSeleccionado) return;
				if (EsDueno(t, jugadorActual)) return;
				if (!SonVecinos(origenSeleccionado, t)) return;

				destinoSeleccionado = t;
				AjustarMaximosDados();
				_spAtk?.Show();
				_btnLanzar?.Show();
				_spDef?.Hide();
				ActualizarHUDTrasSeleccion();
				ActualizarInteractividadPorTurno();
				return;
			}

			origenSeleccionado = null; destinoSeleccionado = null;
			_btnLanzar?.Hide();
			_spAtk?.Hide();
			_spDef?.Hide();
			ActualizarHUDTrasSeleccion();
			ActualizarInteractividadPorTurno();
			return;
		}

		// host_movimiento
		if (faseTurno == "movimiento")
		{
			if (origenSeleccionado == null)
			{
				if (EsDueno(t, jugadorActual) && t.Tropas > 1)
				{
					origenSeleccionado = t;
					destinoSeleccionado = null;
					if (_btnLanzar != null) { _btnLanzar.Text = "Mover"; _btnLanzar.Hide(); }
					_spDef?.Hide();
					AjustarMaximosDados();
					ActualizarHUDTrasSeleccion();
					ActualizarInteractividadPorTurno();
				}
				return;
			}

			if (destinoSeleccionado == null)
			{
				if (t != origenSeleccionado && EsDueno(t, jugadorActual) && SonVecinos(origenSeleccionado, t))
				{
					destinoSeleccionado = t;
					AjustarMaximosDados();
					if (_btnLanzar != null) { _btnLanzar.Text = "Mover"; _btnLanzar.Show(); }
					ActualizarHUDTrasSeleccion();
					ActualizarInteractividadPorTurno();
				}
				return;
			}

			origenSeleccionado = null; destinoSeleccionado = null;
			_btnLanzar?.Hide();
			_spAtk?.Hide();
			_spDef?.Hide();
			ActualizarHUDTrasSeleccion();
			ActualizarInteractividadPorTurno();
			return;
		}
	}

	private void OnTerrenoHovered(NodoTerreno t, bool entered)
	{
		var poly = t.GetNodeOrNull<Polygon2D>("Polygon2D");
		if (poly == null) return;
		poly.Modulate = entered ? new Color(0.9f, 0.9f, 0.9f) : new Color(1, 1, 1);
	}

	// turno
	private void IniciarTurno()
	{
		// cartas_reset
		if (jugadorActual != null)
		{
			jugadorActual.ConquistoEsteTurno = false;
			jugadorActual.RecibioCartaEsteTurno = false;
			ActualizarHUDCartas(jugadorActual);
		}

		// cartas_auto
		if (_isHost && jugadorActual != null && jugadorActual.Cartas.Count >= 6)
			AplicarAutocanjeYSumarFibo(jugadorActual);

		// refuerzos_inicio
		var pid = GetOwnerId(jugadorActual);
		int baseReinf = Math.Max(3, (jugadorActual?.Territorios?.Count ?? 0) / 3);
		int bonusCont = CalcularBonusContinente(jugadorActual);
		int aSumar    = baseReinf + bonusCont;

		SetPool(pid, GetPool(pid) + aSumar);

		// hud_reflejo
		jugadorActual.TropasDisponibles = GetPool(pid);

		// fase_ui
		faseTurno = "refuerzo";
		_btnFinRef?.Show();

		_btnLanzar?.Hide();
		_spAtk?.Hide();
		_spDef?.Hide();
		_recibioCartaTurno[jugadorActual] = false;
		_turnOwnerCardsCount = jugadorActual?.Cartas?.Count ?? 0;

		origenSeleccionado = null;
		destinoSeleccionado = null;
		if (_resultado != null) _resultado.Text = "";

		// red_sync
		BroadcastPhase();
		EnviarPatchBolsa(pid);
		ActualizarUI();
		ActualizarInteractividadPorTurno();
	}

	private void CambiarTurno()
	{
		int idx = _jugList.IndexOf(jugadorActual);
		idx = (idx + 1) % _jugList.Count;
		jugadorActual = _jugList[idx];

		_turnOwnerId = GetOwnerId(jugadorActual);

		BroadcastPhase();
		IniciarTurno();
	}

	// helpers_propiedad
	private bool EsDueno(NodoTerreno t, Jugador j)
	{
		if (t == null || j == null) return false;
		string id = GetOwnerId(j);
		return t.DuenoId == id;
	}

	private string GetOwnerId(Jugador j)
	{
		if (ReferenceEquals(j, j1)) return "J1";
		if (ReferenceEquals(j, j2)) return "J2";
		if (ReferenceEquals(j, j3)) return "J3";
		return "";
	}

	// vecinos
	private bool SonVecinos(NodoTerreno a, NodoTerreno b)
	{
		if (a == null || b == null) return false;
		if (!_ady.TryGetValue(a, out var set) || set == null) return false;

		var kbNombre = NormalizarNombre(b.Nombre);
		var kbName   = NormalizarNombre(b.Name);
		return set.Contains(kbNombre) || set.Contains(kbName);
	}

	// dados_max
	private void AjustarMaximosDados()
	{
		bool listo = (origenSeleccionado != null && destinoSeleccionado != null);
		if (!listo)
		{
			_spAtk?.Hide();
			_spDef?.Hide();
			if (_spAtk != null) _spAtk.MaxValue = 0;
			if (_spDef != null) _spDef.MaxValue = 0;
			return;
		}

		if (faseTurno == "movimiento")
		{
			var maxMove = Math.Max(0, origenSeleccionado.Tropas - 1);
			if (_spAtk != null)
			{
				_spAtk.MinValue = 1;
				_spAtk.MaxValue = Math.Max(1, maxMove);
				if (_spAtk.Value > _spAtk.MaxValue) _spAtk.Value = _spAtk.MaxValue;
				if (_spAtk.Value < _spAtk.MinValue) _spAtk.Value = _spAtk.MinValue;
				_spAtk.Show();
			}
			_spDef?.Hide();
			return;
		}

		var maxAtk = Mathf.Min(3, Math.Max(0, origenSeleccionado.Tropas - 1));
		if (_spAtk != null)
		{
			_spAtk.MinValue = 1;
			_spAtk.MaxValue = Math.Max(1, maxAtk);
			if (_spAtk.Value > _spAtk.MaxValue) _spAtk.Value = _spAtk.MaxValue;
			if (_spAtk.Value < _spAtk.MinValue) _spAtk.Value = _spAtk.MinValue;
		}
	}

	// hud_texto
	private void ActualizarUI()
	{
		if (_turnoLabel != null)
			_turnoLabel.Text = $"Turno: {jugadorActual.Alias} · Fase: {faseTurno.ToUpper()} · Refuerzos: {jugadorActual.TropasDisponibles}";
	}

	private void IrAMovimiento()
	{
		faseTurno = "movimiento";
		origenSeleccionado = null;
		destinoSeleccionado = null;
		_btnLanzar?.Hide();
		_spAtk?.Hide();
		_spDef?.Hide();
		ActualizarHUDTrasSeleccion();

		BroadcastPhase();
		ActualizarUI();
		ActualizarInteractividadPorTurno();
	}

	private void ActualizarHUDTrasSeleccion()
	{
		var origenLbl  = GetNodeOrNull<Label>("HUD/OrigenLabel");
		var destinoLbl = GetNodeOrNull<Label>("HUD/DestinoLabel");
		if (origenLbl != null)  origenLbl.Text  = origenSeleccionado?.Nombre  ?? "—";
		if (destinoLbl != null) destinoLbl.Text = destinoSeleccionado?.Nombre ?? "—";
		AjustarMaximosDados();
	}

	private void AsignarDueno(NodoTerreno t, string jugadorId)
	{
		if (_colorPorId.TryGetValue(jugadorId, out var color))
			t.SetDueno(jugadorId, color);
	}

	// reparto
	private void DistribuirTerrenosInicial(List<NodoTerreno> todos, bool conNeutral)
	{
		Mezclar(todos);

		var owners = conNeutral
			? new List<string> { "J1", "J2", NEUTRAL_ID }
			: new List<string> { "J1", "J2", "J3" };

		int nOwners = owners.Count;
		int total   = todos.Count;

		int cuotaBase = total / nOwners;
		var cuotas  = Enumerable.Repeat(cuotaBase, nOwners).ToArray();
		int resto   = total - (cuotaBase * nOwners);
		for (int i = 0; i < resto; i++) cuotas[i]++;

		var asignados = new int[nOwners];
		int p = 0;

		foreach (var t in todos)
		{
			int intentos = 0;
			while (asignados[p] >= cuotas[p] && intentos < nOwners)
			{
				p = (p + 1) % nOwners;
				intentos++;
			}

			string owner = owners[p];
			var col = _colorPorId.TryGetValue(owner, out var c) ? c : new Color(0.6f, 0.6f, 0.6f);

			t.SetDueno(owner, col);
			t.SetTropas(3);
			ActualizarContadorTropas(t);
			if (_authoritative) BroadcastTerr(t);

			asignados[p]++;
			p = (p + 1) % nOwners;
		}
	}

	// combate
	private void RollAndResolveBattle(int atkDice, int defDice)
	{
		if (!_authoritative || _victoryDeclared) return;
		if (origenSeleccionado == null || destinoSeleccionado == null) return;

		atkDice = Mathf.Clamp(atkDice, 1, Math.Min(3, Math.Max(0, origenSeleccionado.Tropas - 1)));
		defDice = Mathf.Clamp(defDice, 1, Math.Min(2, destinoSeleccionado.Tropas));

		dadosAtq.Clear();
		for (int i = 0; i < atkDice; i++) dadosAtq.Add(rng.RandiRange(1, 6));
		dadosAtq.Sort(); dadosAtq.Reverse();

		dadosDef.Clear();
		for (int i = 0; i < defDice; i++) dadosDef.Add(rng.RandiRange(1, 6));
		dadosDef.Sort(); dadosDef.Reverse();

		int comps = Math.Min(atkDice, defDice);
		int bajasDef = 0, bajasAtk = 0;
		for (int i = 0; i < comps; i++)
		{
			if (dadosAtq[i] > dadosDef[i]) bajasDef++;
			else bajasAtk++;
		}

		if (destinoSeleccionado != null)
		{
			destinoSeleccionado.SetTropas(Mathf.Max(0, destinoSeleccionado.Tropas - bajasDef));
			ActualizarContadorTropas(destinoSeleccionado);
			BroadcastTerr(destinoSeleccionado);
		}
		if (origenSeleccionado != null)
		{
			origenSeleccionado.SetTropas(Mathf.Max(1, origenSeleccionado.Tropas - bajasAtk));
			ActualizarContadorTropas(origenSeleccionado);
			BroadcastTerr(origenSeleccionado);
		}

		// ===== Conquista =====
		string? conquistadorIdParaVictoria = null;
		if (destinoSeleccionado != null && destinoSeleccionado.Tropas == 0 && origenSeleccionado != null)
		{
			var nuevoDuenoId = origenSeleccionado.DuenoId;
			AsignarDueno(destinoSeleccionado, nuevoDuenoId);

			int mover = Math.Max(1, atkDice);
			mover = Math.Min(mover, Math.Max(1, origenSeleccionado.Tropas - 1));
			origenSeleccionado.SetTropas(origenSeleccionado.Tropas - mover);
			destinoSeleccionado.SetTropas(mover);

			ActualizarContadorTropas(origenSeleccionado);
			ActualizarContadorTropas(destinoSeleccionado);
			BroadcastTerr(origenSeleccionado);
			BroadcastTerr(destinoSeleccionado);

			var jConquistador = (nuevoDuenoId == "J1") ? j1 : (nuevoDuenoId == "J2" ? j2 : j3);
			OtorgarCartaUnaVezPorTurno(jConquistador);
			ActualizarHUDCartas(jConquistador);

			// <- Señal para la regla "FirstConquest"
			conquistadorIdParaVictoria = nuevoDuenoId;
		}

		var br = new { type = "battle_result", atk = dadosAtq, def = dadosDef, bajasA = bajasAtk, bajasD = bajasDef };
		GameManager.Instance.BroadcastPatch(br);

		_esperandoDefensa = false;
		_defenderOwnerId = "";
		faseDados = "";
		origenSeleccionado = null;
		destinoSeleccionado = null;

		// Victoria check
		CheckVictoriaHost(conquistadorIdParaVictoria);
		if (_victoryDeclared) return; // si ya hay ganador, no continuamos flujo de turnos

		// flujo normal de turnos
		faseTurno = "refuerzo";
		ResetSeleccionYHUD();
		CambiarTurno();
		ActualizarInteractividadPorTurno();
	}
	private void ResolverDefensaNeutralAuto()
	{
		int defMax = (destinoSeleccionado != null) ? Mathf.Clamp(destinoSeleccionado.Tropas, 1, 2) : 1;
		int atk = tropasAtaqueSeleccionadas > 0 ? tropasAtaqueSeleccionadas : 1;

		OcultarPanelDefensa();
		RollAndResolveBattle(atk, defMax);
	}

	// tropas_label
	private Label CreateOrGetTroopLabel(NodoTerreno t)
	{
		if (!NodeVivo(t)) return null;

		var lbl = t.GetNodeOrNull<Label>("TropasLabel");
		if (lbl == null)
		{
			lbl = new Label
			{
				Name = "TropasLabel",
				Text = t.Tropas.ToString(),
				AutowrapMode = TextServer.AutowrapMode.Off,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				TopLevel = false
			};
			lbl.MouseFilter = Control.MouseFilterEnum.Ignore;
			lbl.ZIndex = 200;
			lbl.AddThemeFontSizeOverride("font_size", 28);
			lbl.AddThemeColorOverride("font_color", new Color(1, 1, 1));
			lbl.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
			lbl.AddThemeConstantOverride("outline_size", 5);
			t.AddChild(lbl);
		}

		foreach (Node ch in t.GetChildren())
			if (ch is Label l && l != lbl) l.QueueFree();

		ReposicionarLabel(t, lbl);
		return lbl;
	}

	// cartas_random
	private Scripts.Carta NuevaCartaAleatoria()
	{
		var rngLocal = new System.Random();
		var tipo = (Scripts.TipoCarta)rngLocal.Next(0, 3);
		string terrId = "NA";
		var lista = GetTree().GetNodesInGroup("Terreno");
		foreach (Node n in lista)
		{
			if (n is NodoTerreno t) { terrId = NormalizarNombre(t.Nombre ?? t.Name); break; }
		}
		return new Scripts.Carta(tipo, terrId);
	}

	// cartas_robar
	private Scripts.Carta RobarCarta()
	{
		if (_mazo != null)
		{
			var c = _mazo.Robar();
			if (c != null) return c;
		}
		return NuevaCartaAleatoria();
	}

	// cartas_otorgar
	private void OtorgarCartaUnaVezPorTurno(Jugador j)
	{
		if (j == null || j.RecibioCartaEsteTurno) return;
		var carta = RobarCarta();
		j.RecibirCarta(carta);
		j.RecibioCartaEsteTurno = true;

		var patchCards = new Scripts.PatchCards { type = "patch_cards", actor = GetOwnerId(j), n = j.Cartas.Count };
		GameManager.Instance.BroadcastPatch(patchCards);
		ActualizarHUDCartas(j);
	}

	// cartas_canje_fibo
	private void AplicarAutocanjeYSumarFibo(Jugador j)
	{
		if (j == null) return;
		if (!j.TieneTrioValido(out var trio)) return;

		int bonus = _fibo?.Avanzar() ?? 0;
		j.IntercambiarCartas(trio, bonus);

		var px = new Scripts.PatchExchange { type = "patch_exchange", actor = GetOwnerId(j), fibo = bonus };
		GameManager.Instance.BroadcastPatch(px);
		var pc = new Scripts.PatchCards { type = "patch_cards", actor = GetOwnerId(j), n = j.Cartas.Count };
		GameManager.Instance.BroadcastPatch(pc);
		ActualizarHUDCartas(j);
		ActualizarUI();
	}

	// cartas_hud
	private void ActualizarHUDCartas(Jugador j)
	{
		if (_cartasLabel == null || j == null) return;
		var (inf, cab, art) = j.ConteoPorTipo();
		_cartasLabel.Text = $"Inf x{inf} | Cab x{cab} | Art x{art}   ({j.Cartas.Count}/5)";
	}

	private void ActualizarHUDCartasPorNumero(int total)
	{
		if (_cartasLabel == null) return;
		_cartasLabel.Text = $"Cartas: {total}/5";
	}

	// label_pos
	private void ReposicionarLabel(NodoTerreno t, Label lbl)
	{
		if (!NodeVivo(t) || !NodeVivo(lbl)) return;

		var poly = t.GetNodeOrNull<Polygon2D>("Polygon2D");
		if (!NodeVivo(poly) || poly.Polygon == null || poly.Polygon.Length < 3) return;

		Vector2 cLocal = Vector2.Zero;
		foreach (var p in poly.Polygon) cLocal += p;
		cLocal /= poly.Polygon.Length;

		Vector2 cGlobal = poly.GetGlobalTransform() * cLocal;
		Vector2 ms = lbl.GetMinimumSize();
		lbl.Size = ms;
		lbl.GlobalPosition = cGlobal - (ms * 0.5f);
	}

	// tropas_ui
	private void ActualizarContadorTropas(NodoTerreno t)
	{
		if (!NodeVivo(t)) return;
		var lbl = CreateOrGetTroopLabel(t);
		if (!NodeVivo(lbl)) return;

		lbl.Text = t.Tropas.ToString();
		Vector2 ms = lbl.GetMinimumSize();
		lbl.Size = ms;
		ReposicionarLabel(t, lbl);
	}

	// parches_host
	private void BroadcastTerr(NodoTerreno t)
	{
		if (!_authoritative) return;
		var patch = new PatchTerr
		{
			type = "patch_terr",
			terr = NormalizarNombre(t.Nombre),
			ownerId = t.DuenoId,
			tropas = t.Tropas
		};
		GameManager.Instance.BroadcastPatch(patch);
	}
	private void BroadcastPhase()
	{
		if (!_authoritative) return;
		var patch = new PatchPhase
		{
			type = "patch_phase",
			fase  = faseTurno,
			turno = GetOwnerId(jugadorActual),
			refuerzos = jugadorActual?.TropasDisponibles ?? 0
		};
		GameManager.Instance.BroadcastPatch(patch);
	}
	private void EnviarPatchBolsa(string playerId)
	{
		if (!_authoritative) return;
		int n = GetPool(playerId);
		var patch = new { type = "patch_reinf_pool", actor = playerId, n = n };
		GameManager.Instance.BroadcastPatch(patch);
	}
	
	private void BroadcastVictory(string winnerId, string nombreGanador)
	{
		if (!_authoritative || _victoryDeclared) return;
		_victoryDeclared = true;

		var patch = new { type = "patch_victory", winner = winnerId, name = nombreGanador };
		GameManager.Instance.BroadcastPatch(patch);
	}

	// Regla: "queda un solo jugador con territorios"
	private string? EvaluateLastPlayerStandingWinner()
	{
		var cuenta = new Dictionary<string, int> { ["J1"]=0, ["J2"]=0, ["J3"]=0 };
		var lista = GetTree().GetNodesInGroup("Terreno");
		foreach (Node n in lista)
		{
			if (n is NodoTerreno t)
			{
				if (t.DuenoId == "J1") cuenta["J1"]++;
				else if (t.DuenoId == "J2") cuenta["J2"]++;
				else if (t.DuenoId == "J3") cuenta["J3"]++;
			}
		}

		var vivos = cuenta.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
		if (vivos.Count == 1) return vivos[0];
		return null;
	}

// Regla: "gana el primero que conquiste un territorio"
	private string? EvaluateFirstConquestWinner(string? conquerorId)
	{
		// Si hubo conquista, ese es el ganador inmediato
		if (!string.IsNullOrEmpty(conquerorId)) return conquerorId;
		return null;
	}

// Punto único de chequeo de victoria (llamar tras eventos clave)
	private void CheckVictoriaHost(string? conquerorId = null)
	{
		if (!_authoritative || _victoryDeclared) return;

		string? winnerId = null;
		switch (VictoryRule)
		{
			case VictoryCondition.LastPlayerStanding:
				winnerId = EvaluateLastPlayerStandingWinner();
				break;
			case VictoryCondition.FirstConquest:
				winnerId = EvaluateFirstConquestWinner(conquerorId);
				break;
		}

		if (!string.IsNullOrEmpty(winnerId))
		{
			var nombreGanador = _playerById.TryGetValue(winnerId, out var pj)
				? (pj?.Alias ?? winnerId)
				: winnerId;

			BroadcastVictory(winnerId, nombreGanador);
		}
	}

	
	// parches_apply
	public void ApplyNetPatch(string json)
	{
		try
		{
			using var doc = System.Text.Json.JsonDocument.Parse(json);
			var t = doc.RootElement.GetProperty("type").GetString();

			if (t == "patch_terr")
			{
				var terrId = doc.RootElement.GetProperty("terr").GetString();
				var owner  = doc.RootElement.GetProperty("ownerId").GetString();
				var tropas = doc.RootElement.GetProperty("tropas").GetInt32();

				NodoTerreno terr = null;
				var lista = GetTree().GetNodesInGroup("Terreno");
				foreach (Node n in lista)
				{
					if (n is NodoTerreno cand)
					{
						var id1 = NormalizarNombre(cand.Nombre);
						var id2 = NormalizarNombre(cand.Name);
						if (id1 == terrId || id2 == terrId) { terr = cand; break; }
					}
				}
				if (terr == null) return;

				if (_colorPorId.TryGetValue(owner, out var col))
					terr.SetDueno(owner, col);
				else
					terr.SetDueno(owner, new Color(0.8f, 0.8f, 0.8f));

				terr.SetTropas(tropas);

				_ = CreateOrGetTroopLabel(terr);
				ActualizarContadorTropas(terr);
				ActualizarInteractividadPorTurno();
			}
			else if (t == "patch_phase")
			{
				var faseAnterior = faseTurno;

				faseTurno = doc.RootElement.GetProperty("fase").GetString();
				var turnoId = doc.RootElement.GetProperty("turno").GetString();
				int refz = doc.RootElement.GetProperty("refuerzos").GetInt32();

				jugadorActual = turnoId == "J1" ? j1 : (turnoId == "J2" ? j2 : j3);
				if (jugadorActual != null) jugadorActual.TropasDisponibles = refz;

				_turnOwnerId = turnoId;

				if (!string.Equals(faseAnterior, faseTurno, StringComparison.OrdinalIgnoreCase))
					ResetSeleccionYHUD();

				ActualizarUI();
				ActualizarInteractividadPorTurno();
			}
			else if (t == "patch_reinf_pool")
			{
				// bolsa_sync
				string actor = doc.RootElement.GetProperty("actor").GetString();
				int n = doc.RootElement.GetProperty("n").GetInt32();
				_bolsaRefuerzos[actor] = n;

				if (jugadorActual != null && GetOwnerId(jugadorActual) == actor)
				{
					jugadorActual.TropasDisponibles = n;
					ActualizarUI();
				}
			}
			else if (t == "patch_exchange_invalid")
			{
				// cartas_feedback
				var actor = doc.RootElement.GetProperty("actor").GetString();
				var myId = GameManager.Instance?.MyId;

				bool soyDestino = _authoritative
					? (jugadorActual != null && GetOwnerId(jugadorActual) == actor)
					: (!string.IsNullOrEmpty(myId) && myId == actor);

				if (soyDestino && _resultado != null)
					_resultado.Text = "No tienes un trío válido (3 iguales o 1 de cada).";
			}
			else if (t == "start_defense")
			{
				// defensa_ui
				var defender = doc.RootElement.GetProperty("defender").GetString();
				int max = doc.RootElement.GetProperty("max").GetInt32();
				var myId = GameManager.Instance?.MyId;

				if (!string.IsNullOrEmpty(myId) && myId == defender)
					MostrarPanelDefensa(Mathf.Clamp(max, 1, 2));
				else
					OcultarPanelDefensa();

				if (!_authoritative)
				{
					origenSeleccionado = null;
					destinoSeleccionado = null;
					_btnLanzar?.Hide();
					_spAtk?.Hide();
					_spDef?.Hide();
					ActualizarHUDTrasSeleccion();
				}

				ActualizarInteractividadPorTurno();
			}
			else if (t == "battle_result")
			{
				// combate_resultado
				try
				{
					dadosAtq.Clear();
					dadosDef.Clear();
					foreach (var e in doc.RootElement.GetProperty("atk").EnumerateArray()) dadosAtq.Add(e.GetInt32());
					foreach (var e in doc.RootElement.GetProperty("def").EnumerateArray()) dadosDef.Add(e.GetInt32());
					int bajasA = doc.RootElement.GetProperty("bajasA").GetInt32();
					int bajasD = doc.RootElement.GetProperty("bajasD").GetInt32();
					if (_resultado != null)
						_resultado.Text = $"Atacante: {string.Join(", ", dadosAtq)} | Defensor: {string.Join(", ", dadosDef)} — Bajas A:{bajasA} D:{bajasD}";
				}
				catch { if (_resultado != null) _resultado.Text = "Combate resuelto."; }

				OcultarPanelDefensa();

				origenSeleccionado = null;
				destinoSeleccionado = null;
				_btnLanzar?.Hide();
				_spAtk?.Hide();
				_spDef?.Hide();
				ActualizarHUDTrasSeleccion();
				ActualizarInteractividadPorTurno();
			}
			
			else if (t == "patch_victory")
			{
				var winnerId = doc.RootElement.GetProperty("winner").GetString();
				var name = doc.RootElement.GetProperty("name").GetString();

				_victoryDeclared = true; // bloquear más acciones locales

				// Diálogo simple
				var dlg = new AcceptDialog { Title = "¡Fin de la partida!", DialogText = $"Ganador: {name}" };
				GetTree().Root.AddChild(dlg);
				dlg.PopupCentered();

				// Deshabilitar botones básicos
				_btnLanzar?.SetDeferred("disabled", true);
				_btnFinRef?.SetDeferred("disabled", true);
				_btnCanjear?.SetDeferred("disabled", true);
				_btnIrAtaque?.SetDeferred("disabled", true);
				_btnFinTurno?.SetDeferred("disabled", true);
			}

			// cartas_patch
			else if (t == "patch_cards")
			{
				var actorIdCards = doc.RootElement.GetProperty("actor").GetString();
				int n = doc.RootElement.GetProperty("n").GetInt32();

				if (actorIdCards == _turnOwnerId)
				{
					_turnOwnerCardsCount = n;

					if (_authoritative)
					{
						var jHost = actorIdCards == "J1" ? j1 : (actorIdCards == "J2" ? j2 : j3);
						ActualizarHUDCartas(jHost);
					}
					else
					{
						int inf, cab, art;
						if (doc.RootElement.TryGetProperty("inf", out var infEl) &&
							doc.RootElement.TryGetProperty("cab", out var cabEl) &&
							doc.RootElement.TryGetProperty("art", out var artEl))
						{
							inf = infEl.GetInt32();
							cab = cabEl.GetInt32();
							art = artEl.GetInt32();
							if (_cartasLabel != null)
								_cartasLabel.Text = $"Inf x{inf} | Cab x{cab} | Art x{art}   ({n}/5)";
						}
						else
						{
							ActualizarHUDCartasPorNumero(n);
						}
					}

					ActualizarInteractividadPorTurno();
				}
			}
			else if (t == "patch_exchange")
			{
				// cartas_exchange
				var actorIdEx = doc.RootElement.GetProperty("actor").GetString();
				int fibo = doc.RootElement.GetProperty("fibo").GetInt32();
				var j = actorIdEx == "J1" ? j1 : (actorIdEx == "J2" ? j2 : j3);
				if (j != null)
				{
					if (actorIdEx == _turnOwnerId && _resultado != null)
						_resultado.Text = $"Intercambio aplicado: +{fibo} tropas (Fibonacci)";
					ActualizarHUDCartas(j);
					ActualizarUI();
				}
			}
		}
		catch (Exception e)
		{
			GD.PushWarning("[MapaUI] ApplyNetPatch error: " + e.Message);
		}
	}

	// comandos_host
	public void ProcessNetCommand(string json)
	{
		if (!_authoritative) return;

		using var doc = System.Text.Json.JsonDocument.Parse(json);
		var t = doc.RootElement.GetProperty("type").GetString();

		if (t == "cmd_click")
		{
			var terrId = doc.RootElement.GetProperty("terr").GetString();
			var actor  = doc.RootElement.GetProperty("actor").GetString();
			if (actor != GetOwnerId(jugadorActual)) return;

			var actorJ = actor == "J1" ? j1 : (actor == "J2" ? j2 : j3);

			NodoTerreno terr = null;
			var lista = GetTree().GetNodesInGroup("Terreno");
			foreach (Node n in lista)
			{
				if (n is NodoTerreno tt)
				{
					var id1 = NormalizarNombre(tt.Nombre);
					var id2 = NormalizarNombre(tt.Name);
					if (id1 == terrId || id2 == terrId) { terr = tt; break; }
				}
			}
			if (terr == null) return;

			var oldJugador = jugadorActual;
			jugadorActual = actorJ;
			OnTerrenoClicked(terr);
			jugadorActual = oldJugador;
		}
		else if (t == "cmd_attack")
		{
			var actor   = doc.RootElement.GetProperty("actor").GetString();
			int atkReq  = doc.RootElement.GetProperty("atk").GetInt32();
			var origId  = (doc.RootElement.TryGetProperty("origen", out var oEl) ? oEl.GetString() : "") ?? "";
			var destId  = (doc.RootElement.TryGetProperty("destino", out var dEl) ? dEl.GetString() : "") ?? "";

			if (actor != GetOwnerId(jugadorActual)) return;
			if (faseTurno != "ataque") return;

			NodoTerreno orig = null, dest = null;
			var lista = GetTree().GetNodesInGroup("Terreno");
			foreach (Node n in lista)
			{
				if (n is NodoTerreno tt)
				{
					var id1 = NormalizarNombre(tt.Nombre);
					var id2 = NormalizarNombre(tt.Name);
					if (orig == null && (id1 == origId || id2 == origId)) orig = tt;
					if (dest == null && (id1 == destId || id2 == destId)) dest = tt;
					if (orig != null && dest != null) break;
				}
			}
			if (orig == null || dest == null) return;

			if (orig.DuenoId != actor) return;
			if (dest.DuenoId == actor) return;
			if (!SonVecinos(orig, dest)) return;
			if (orig.Tropas <= 1) return;

			origenSeleccionado  = orig;
			destinoSeleccionado = dest;

			tropasAtaqueSeleccionadas = Mathf.Clamp(
				atkReq,
				1,
				Mathf.Min(3, Math.Max(0, origenSeleccionado.Tropas - 1))
			);

			_defenderOwnerId = destinoSeleccionado.DuenoId;

			// defensa_neutral
			if (_defenderOwnerId == NEUTRAL_ID)
			{
				ResolverDefensaNeutralAuto();
				return;
			}

			// defensa_humano
			var maxDef = Mathf.Clamp(destinoSeleccionado.Tropas, 1, 2);
			var startDef = new {
				type    = "start_defense",
				defender= _defenderOwnerId,
				max     = maxDef,
				origen  = NormalizarNombre(origenSeleccionado.Nombre),
				destino = NormalizarNombre(destinoSeleccionado.Nombre),
				atk     = tropasAtaqueSeleccionadas
			};
			GameManager.Instance.BroadcastPatch(startDef);
			_esperandoDefensa = true;
		}
		else if (t == "cmd_move")
		{
			var actor = doc.RootElement.GetProperty("actor").GetString();
			int n     = doc.RootElement.GetProperty("n").GetInt32();
			var origId= (doc.RootElement.TryGetProperty("origen",  out var oEl) ? oEl.GetString() : "") ?? "";
			var destId= (doc.RootElement.TryGetProperty("destino", out var dEl) ? dEl.GetString() : "") ?? "";

			if (actor != GetOwnerId(jugadorActual)) return;
			if (faseTurno != "movimiento") return;

			NodoTerreno orig = null, dest = null;
			var lista = GetTree().GetNodesInGroup("Terreno");
			foreach (Node nnode in lista)
			{
				if (nnode is NodoTerreno tt)
				{
					var id1 = NormalizarNombre(tt.Nombre);
					var id2 = NormalizarNombre(tt.Name);
					if (orig == null && (id1 == origId || id2 == origId)) orig = tt;
					if (dest == null && (id1 == destId || id2 == destId)) dest = tt;
					if (orig != null && dest != null) break;
				}
			}
			if (orig == null || dest == null) return;

			AplicarMovimiento(orig, dest, n);
		}
		else if (t == "cmd_end_phase")
		{
			if (GetOwnerId(jugadorActual) != _turnOwnerId) return;

			if (faseTurno == "refuerzo")        OnFinalizarRefuerzos();
			else if (faseTurno == "movimiento") OnFinalizarMovimiento();
			else                                OnFinalizarMovimiento();
		}
		else if (t == "cmd_defense_choice")
		{
			var actor = doc.RootElement.GetProperty("actor").GetString();
			var dice  = doc.RootElement.GetProperty("dice").GetInt32();

			if (!_esperandoDefensa || actor != _defenderOwnerId) return;

			tropasDefensaSeleccionadas = Mathf.Clamp(dice, 1, 2);

			RollAndResolveBattle(tropasAtaqueSeleccionadas, tropasDefensaSeleccionadas);
		}
		else if (t == "cmd_exchange")
		{
			if (!_authoritative) return;
			var actor = doc.RootElement.GetProperty("actor").GetString();
			var j = actor == "J1" ? j1 : (actor == "J2" ? j2 : j3);
			if (j == null) return;
			if (!ReferenceEquals(jugadorActual, j)) return;

			if (!j.TieneTrioValido(out var trio))
			{
				var pxInv = new { type = "patch_exchange_invalid", actor = actor, reason = "no_trio" };
				GameManager.Instance.BroadcastPatch(pxInv);
				return;
			}

			int bonus = _fibo?.Avanzar() ?? 0;
			j.IntercambiarCartas(trio, bonus);

			SetPool(actor, GetPool(actor) + bonus);
			EnviarPatchBolsa(actor);

			var px = new Scripts.PatchExchange { type = "patch_exchange", actor = actor, fibo = bonus };
			GameManager.Instance.BroadcastPatch(px);

			var pc = new Scripts.PatchCards { type = "patch_cards", actor = actor, n = j.Cartas.Count };
			GameManager.Instance.BroadcastPatch(pc);

			ActualizarHUDCartas(j);
			ActualizarUI();
		}
	}

	// shuffle
	private void Mezclar<T>(List<T> list)
	{
		for (int i = list.Count - 1; i > 0; i--)
		{
			int j = (int)rng.RandiRange(0, i);
			(list[i], list[j]) = (list[j], list[i]);
		}
	}

	// defensa_ui_helpers
	private void MostrarPanelDefensa(int maxDados)
	{
		if (_defensePanel == null) return;
		_defenseTitle.Text = "Tu turno de DEFENDER";
		_defenseSpin.MinValue = 1;
		_defenseSpin.MaxValue = Math.Max(1, maxDados);
		_defenseSpin.Value = Math.Min(2, maxDados);
		_defensePanel.Visible = true;
		_defensePanel.MouseFilter = Control.MouseFilterEnum.Pass;
	}
	private void OcultarPanelDefensa()
	{
		if (_defensePanel == null) return;
		_defensePanel.Visible = false;
	}
	private void OnDefenderLanzaDados()
	{
		var myId = GameManager.Instance?.MyId;
		if (string.IsNullOrEmpty(myId)) return;

		int dados = (int)_defenseSpin.Value;
		dados = Mathf.Clamp(dados, 1, 2);

		var cmd = new { type = "cmd_defense_choice", actor = myId, dice = dados };
		GameManager.Instance.SendCmd(cmd);

		if (_resultado != null)
			_resultado.Text = "Enviada tu elección de defensa...";

		OcultarPanelDefensa();
	}

	// ui_estado
	private void ActualizarInteractividadPorTurno()
	{
		var myId = GameManager.Instance?.MyId;
		bool miTurno = !_isOnline || string.IsNullOrEmpty(myId) || myId == _turnOwnerId;

		if (_btnFinRef != null) _btnFinRef.Visible = miTurno && faseTurno == "refuerzo";

		if (_btnIrAtaque != null)
			_btnIrAtaque.Visible = miTurno && faseTurno == "movimiento";
		if (_btnFinTurno != null)
			_btnFinTurno.Visible = miTurno && faseTurno == "ataque";

		// ataque
		bool puedeAtacar = miTurno && faseTurno == "ataque";
		if (_btnLanzar != null && faseTurno == "ataque")
			_btnLanzar.Visible = puedeAtacar && origenSeleccionado != null && destinoSeleccionado != null;
		if (_spAtk != null && faseTurno == "ataque")
			_spAtk.Visible = puedeAtacar && origenSeleccionado != null && destinoSeleccionado != null;

		// cartas_btn
		int cartasConteo = _authoritative ? (jugadorActual?.Cartas?.Count ?? 0) : _turnOwnerCardsCount;
		bool puedeIntentarCanje = (faseTurno == "refuerzo") && (cartasConteo >= 3);
		if (_btnCanjear != null)
		{
			_btnCanjear.Visible  = miTurno && (faseTurno == "refuerzo");
			_btnCanjear.Disabled = !puedeIntentarCanje;
			_btnCanjear.TooltipText = (cartasConteo < 3)
				? "Necesitas al menos 3 cartas."
				: "Intentar canje (el host valida: 3 iguales o 1 de cada).";
		}

		if (faseTurno == "refuerzo")
		{
			if (_authoritative)
				puedeIntentarCanje = (jugadorActual != null) && jugadorActual.TieneTrioValido(out _);
			else
				puedeIntentarCanje = cartasConteo >= 3;
		}

		if (_btnCanjear != null)
		{
			_btnCanjear.Visible  = miTurno && (faseTurno == "refuerzo");
			_btnCanjear.Disabled = !puedeIntentarCanje;
			_btnCanjear.TooltipText = puedeIntentarCanje
				? "Canjear 3 cartas (trío válido)."
				: "Necesitas un trío válido (3 iguales o 1 de cada).";
		}

		// movimiento
		bool puedeMover = miTurno && faseTurno == "movimiento";
		if (_btnLanzar != null && faseTurno == "movimiento")
		{
			_btnLanzar.Text = "Mover";
			_btnLanzar.Visible = puedeMover && origenSeleccionado != null && destinoSeleccionado != null;
		}
		if (_spAtk != null && faseTurno == "movimiento")
			_spAtk.Visible = puedeMover && origenSeleccionado != null && destinoSeleccionado != null;
	}

	private void ResetSeleccionYHUD()
	{
		origenSeleccionado = null;
		destinoSeleccionado = null;

		_btnLanzar?.Hide();
		_spAtk?.Hide();
		_spDef?.Hide();

		ActualizarHUDTrasSeleccion();
	}

	// continentes
	private static readonly Dictionary<string, (string nombre, int bonus)> _contBonus = new()
	{
		{ "Africa", ("África", 3) },
		{ "EU",     ("Europa", 5) },
		{ "AS",     ("Asia", 7) },
		{ "Nor",    ("Norteamérica", 5) },
		{ "Sud",    ("Sudamérica", 2) },
		{ "OC",     ("Oceanía", 2) },
	};
	private static string PrefijoDe(string nombreNodo)
	{
		if (string.IsNullOrEmpty(nombreNodo)) return "";
		int i = 0;
		while (i < nombreNodo.Length && char.IsLetter(nombreNodo[i])) i++;
		return (i > 0) ? nombreNodo.Substring(0, i) : "";
	}
	private Dictionary<string, int> ContarTotalesPorPrefijoEnEscena()
	{
		var tot = new Dictionary<string, int>();
		var terrRoot = GetNodeOrNull<Node>("Territorios");
		if (terrRoot == null) return tot;
		foreach (Node child in terrRoot.GetChildren())
		{
			if (child is not NodoTerreno t) continue;
			var pref = PrefijoDe(t.Name ?? "");
			if (string.IsNullOrEmpty(pref)) continue;
			tot[pref] = tot.GetValueOrDefault(pref) + 1;
		}
		return tot;
	}
	private int CalcularBonusContinente(Jugador j)
	{
		if (j == null) return 0;
		var totPorPrefijo = ContarTotalesPorPrefijoEnEscena();
		var tienePorPrefijo = new Dictionary<string, int>();
		foreach (var t in j.Territorios ?? Enumerable.Empty<NodoTerreno>())
		{
			var pref = PrefijoDe(t?.Name ?? "");
			if (string.IsNullOrEmpty(pref)) continue;
			tienePorPrefijo[pref] = tienePorPrefijo.GetValueOrDefault(pref) + 1;
		}
		int total = 0;

		foreach (var kv in _contBonus)
		{
			var pref = kv.Key;
			var bonus = kv.Value.bonus;
			if (totPorPrefijo.TryGetValue(pref, out int tot) && tot > 0 && tienePorPrefijo.GetValueOrDefault(pref) == tot)
			{
				total += bonus;
			}
		}
		return total;
	}

	// refuerzos_bolsa
	private int GetPool(string playerId)
	{
		if (string.IsNullOrEmpty(playerId)) return 0;
		return _bolsaRefuerzos.TryGetValue(playerId, out var n) ? n : 0;
	}
	private void SetPool(string playerId, int value)
	{
		if (string.IsNullOrEmpty(playerId)) return;
		_bolsaRefuerzos[playerId] = Math.Max(0, value);

		if (jugadorActual != null && GetOwnerId(jugadorActual) == playerId)
			jugadorActual.TropasDisponibles = _bolsaRefuerzos[playerId];
	}
}
