using Godot;
using Scripts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using NodoTerreno = global::Terreno;

// ===== Interfaces para red =====
public interface IAplicaParches { void ApplyNetPatch(string json); }
public interface IProcesaComandos { void ProcessNetCommand(string json); }

public partial class MapaUI : Node2D, IAplicaParches, IProcesaComandos
{
	// === Jugadores ===
	private Jugador j1;
	private Jugador j2;
	private Jugador j3; // si hay 3 jugadores
	private readonly List<Jugador> _jugList = new();
	private readonly Dictionary<string, Jugador> _playerById = new(); // "J1"/"J2"/"J3" -> Jugador
	private Jugador jugadorActual;

	// === Selección / estado ===
	private NodoTerreno origenSeleccionado = null;
	private NodoTerreno destinoSeleccionado = null;

	private string faseTurno = "refuerzo"; // refuerzo, ataque, movimiento
	private string faseDados = "";         // atacante, defensor
	private string _turnOwnerId = "J1";    // dueño del turno (según host)

	private int tropasAtaqueSeleccionadas = 1;
	private int tropasDefensaSeleccionadas = 1;
	private readonly List<int> dadosAtq = new();
	private readonly List<int> dadosDef = new();
	private readonly RandomNumberGenerator rng = new();

	// Online
	private bool _isOnline, _isHost, _authoritative;
	private NetworkManager _net;

	// === HUD NodePaths ===
	[Export] private NodePath TurnoLabelPath;
	[Export] private NodePath ResultadoDadoPath;
	[Export] private NodePath LanzarDadoPath;
	[Export] private NodePath FinalizarRefuerzosPath;
	[Export] private NodePath FinalizarMovimientoPath;
	[Export] private NodePath CantidadAtaquePath;
	[Export] private NodePath CantidadDefensaPath;

	// === HUD refs ===
	private Label _turnoLabel, _resultado;
	private Button _btnLanzar, _btnFinRef, _btnFinMov;
	private SpinBox _spAtk, _spDef;

	// === Defensa interactiva (UI del defensor) ===
	private Control _defensePanel;
	private Label _defenseTitle;
	private SpinBox _defenseSpin;
	private Button _defenseDiceBtn;
	private string _defenderOwnerId = "";   // "J1"/"J2"/"J3"
	private bool _esperandoDefensa = false;

	// Colores por jugador-id para pintar territorios
	private readonly Dictionary<string, Color> _colorPorId = new()
	{
		["J1"] = new Color(0.9f, 0.2f, 0.2f), // rojo
		["J2"] = new Color(0.2f, 0.4f, 0.9f), // azul
		["J3"] = new Color(0.2f, 0.8f, 0.2f), // verde
	};

	// Adyacencias por nombre normalizado
	private readonly Dictionary<NodoTerreno, HashSet<string>> _ady = new();

	public override void _Ready()
	{
		// --- HUD ---
		_turnoLabel = GetNodeOrNull<Label>(TurnoLabelPath ?? "HUD/TurnoLabel");
		_resultado  = GetNodeOrNull<Label>(ResultadoDadoPath ?? "HUD/ResultadoDado");
		_btnLanzar  = GetNodeOrNull<Button>(LanzarDadoPath ?? "HUD/LanzarDado");
		_btnFinRef  = GetNodeOrNull<Button>(FinalizarRefuerzosPath ?? "HUD/FinalizarRefuerzos");
		_btnFinMov  = GetNodeOrNull<Button>(FinalizarMovimientoPath ?? "HUD/FinalizarMovimiento");
		_spAtk      = GetNodeOrNull<SpinBox>(CantidadAtaquePath ?? "HUD/CantidadAtaque");
		_spDef      = GetNodeOrNull<SpinBox>(CantidadDefensaPath ?? "HUD/CantidadDefensa");

		if (_btnLanzar != null) { _btnLanzar.Pressed += OnLanzarDado; _btnLanzar.Visible = false; }
		if (_btnFinRef != null) { _btnFinRef.Pressed += OnFinalizarRefuerzos; _btnFinRef.Visible = false; }
		if (_btnFinMov != null) { _btnFinMov.Pressed += OnFinalizarMovimiento; _btnFinMov.Visible = false; }
		_spAtk?.Hide();
		_spDef?.Hide();

		// Panel de defensa (si no existe en escena, se crea)
		_defensePanel = GetNodeOrNull<Control>("HUD/DefensePanel");
		if (_defensePanel == null)
		{
			_defensePanel = new PanelContainer { Name = "DefensePanel", Visible = false };
			var vb = new VBoxContainer { Name = "VBox" };
			_defenseTitle = new Label { Name = "Title", Text = "Defensa: elige dados" };
			_defenseSpin  = new SpinBox { Name = "Spin", MinValue = 1, MaxValue = 2, Step = 1, Value = 1 };
			_defenseDiceBtn = new Button { Name = "Btn", Text = "Lanzar defensa" };
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
		if (hud != null) hud.MouseFilter = Control.MouseFilterEnum.Ignore;

		// --- Jugadores desde GameManager ---
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

		// --- Online flags ---
		_isOnline = GameManager.Instance?.IsOnline ?? false;
		_isHost   = GameManager.Instance?.IsHost   ?? false;
		_authoritative = !_isOnline || _isHost;
		_net = GetNodeOrNull<NetworkManager>("/root/NetworkManager");
		// Importante: NO nos enganchamos a _net.MessageReceived aquí,
		// GameManager ya reenvía a ApplyNetPatch / ProcessNetCommand.

		// --- Grupo y señales de territorios ---
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
				t.Connect(NodoTerreno.SignalName.Clicked, new Callable(this, nameof(OnTerrenoClicked)));
				t.Connect(NodoTerreno.SignalName.Hovered, new Callable(this, nameof(OnTerrenoHovered)));
				CreateOrGetTroopLabel(t);
				ActualizarContadorTropas(t);
			}
		}

		// --- Adyacencia ---
		ConstruirAdyacencia(lista);

		// --- Estado Inicial: repartir territorios J1/J2(/J3) ---
		int turn = 0;
		foreach (Node node in lista)
		{
			if (node is NodoTerreno tInit)
			{
				int idx = turn++ % numPlayers; // 0,1,(2),0,1,(2)...
				string ownerId = idx == 0 ? "J1" : (idx == 1 ? "J2" : "J3");
				var color = _colorPorId[ownerId];
				tInit.SetDueno(ownerId, color);
				tInit.SetTropas(3);
				ActualizarContadorTropas(tInit);
			}
		}

		rng.Randomize();

		// **Jugador inicial**
		jugadorActual = j1;
		_turnOwnerId  = "J1";
		IniciarTurno();
		ActualizarInteractividadPorTurno();
	}

	// ========= Vecindad por geometría =========
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

	// =========================
	//       HANDLERS HUD
	// =========================
	private void OnLanzarDado()
	{
		if (origenSeleccionado == null || destinoSeleccionado == null) return;

		// CLIENTE: enviar cmd_attack con origen/destino
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

		// HOST: solo prepara y pide defensa
		tropasAtaqueSeleccionadas = (int)(_spAtk?.Value ?? 1);
		tropasAtaqueSeleccionadas = Mathf.Clamp(tropasAtaqueSeleccionadas, 1, Mathf.Min(3, Math.Max(0, origenSeleccionado.Tropas - 1)));

		_defenderOwnerId = destinoSeleccionado.DuenoId;
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

	private void OnFinalizarRefuerzos()
	{
		if (!_authoritative)
		{
			var myId = GameManager.Instance?.MyId;
			if (string.IsNullOrEmpty(myId) || myId != _turnOwnerId) return;
			GameManager.Instance.SendCmd(new CmdEndPhase { type = "cmd_end_phase" });
			return;
		}

		faseTurno = "ataque";
		_btnFinRef?.Hide();
		BroadcastPhase();
		ActualizarUI();
		ActualizarInteractividadPorTurno();
	}

	private void OnFinalizarMovimiento()
	{
		if (!_authoritative)
		{
			var myId = GameManager.Instance?.MyId;
			if (string.IsNullOrEmpty(myId) || myId != _turnOwnerId) return;
			GameManager.Instance.SendCmd(new CmdEndPhase { type = "cmd_end_phase" });
			return;
		}

		_btnFinMov?.Hide();
		faseTurno = "refuerzo";
		BroadcastPhase();
		CambiarTurno();
		ActualizarInteractividadPorTurno();
	}

	// =========================
	//      HANDLERS MAPA
	// =========================
	private void OnTerrenoClicked(NodoTerreno t)
	{
		if (t == null) return;

		if (!_authoritative)
		{
			// Sólo si es mi turno
			var myId = GameManager.Instance?.MyId;
			if (string.IsNullOrEmpty(myId) || myId != _turnOwnerId) return;

			var terrId = NormalizarNombre(t.Nombre);
			var cmd = new CmdClick { type = "cmd_click", terr = terrId, actor = myId };
			GameManager.Instance.SendCmd(cmd);

			// ECO local para mostrar botón/spin
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
				// tercer clic: reset local
				origenSeleccionado = null;
				destinoSeleccionado = null;
				_btnLanzar?.Hide();
				_spAtk?.Hide();
				_spDef?.Hide();
				ActualizarHUDTrasSeleccion();
				ActualizarInteractividadPorTurno();
			}
			return;
		}

		// === HOST ===
		if (faseTurno == "refuerzo")
		{
			if (EsDueno(t, jugadorActual) && jugadorActual.TropasDisponibles > 0)
			{
				t.SetTropas(t.Tropas + 1);
				ActualizarContadorTropas(t);
				BroadcastTerr(t);

				jugadorActual.TropasDisponibles -= 1;
				if (jugadorActual.TropasDisponibles == 0)
				{
					faseTurno = "ataque";
					_btnFinRef?.Hide();
				}
				BroadcastPhase();
				ActualizarUI();
				ActualizarInteractividadPorTurno();
			}
			return;
		}

		if (faseTurno == "ataque")
		{
			if (origenSeleccionado == null)
			{
				if (EsDueno(t, jugadorActual) && t.Tropas > 1)
				{
					origenSeleccionado = t;
					_spAtk?.Hide();
					_spDef?.Hide();
					_spAtk?.Show(); _btnLanzar?.Show();
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
				_spAtk?.Show(); _btnLanzar?.Show();
				_spDef?.Hide();
				ActualizarHUDTrasSeleccion();
				ActualizarInteractividadPorTurno();
				return;
			}

			// tercer clic: reset selección
			origenSeleccionado = null;
			destinoSeleccionado = null;
			_btnLanzar?.Hide();
			_spAtk?.Hide();
			_spDef?.Hide();
			ActualizarHUDTrasSeleccion();
			ActualizarInteractividadPorTurno();
		}
	}

	private void OnTerrenoHovered(NodoTerreno t, bool entered)
	{
		var poly = t.GetNodeOrNull<Polygon2D>("Polygon2D");
		if (poly == null) return;
		poly.Modulate = entered ? new Color(0.9f, 0.9f, 0.9f) : new Color(1, 1, 1);
	}

	// =========================
	//        LÓGICA
	// =========================
	private void IniciarTurno()
	{
		jugadorActual.TropasDisponibles = Math.Max(3,
			(jugadorActual.Territorios != null) ? jugadorActual.Territorios.Count / 3 : 3);

		faseTurno = "refuerzo";
		_btnFinRef?.Show();

		_btnLanzar?.Hide();
		_spAtk?.Hide();
		_spDef?.Hide();

		origenSeleccionado = null;
		destinoSeleccionado = null;
		if (_resultado != null) _resultado.Text = "";

		BroadcastPhase();
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

	private bool SonVecinos(NodoTerreno a, NodoTerreno b)
	{
		if (a == null || b == null) return false;
		if (!_ady.TryGetValue(a, out var set) || set == null) return false;

		var kbNombre = NormalizarNombre(b.Nombre);
		var kbName   = NormalizarNombre(b.Name);
		return set.Contains(kbNombre) || set.Contains(kbName);
	}

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

		var maxAtk = Mathf.Min(3, Math.Max(0, origenSeleccionado.Tropas - 1));
		if (_spAtk != null)
		{
			_spAtk.MinValue = 1;
			_spAtk.MaxValue = Math.Max(1, maxAtk);
			if (_spAtk.Value > _spAtk.MaxValue) _spAtk.Value = _spAtk.MaxValue;
			if (_spAtk.Value < _spAtk.MinValue) _spAtk.Value = _spAtk.MinValue;
		}
	}

	private void ActualizarUI()
	{
		if (_turnoLabel == null)
			_turnoLabel = GetNodeOrNull<Label>(TurnoLabelPath ?? "HUD/TurnoLabel");

		if (_turnoLabel != null)
			_turnoLabel.Text = $"Turno: {jugadorActual.Alias} · Fase: {faseTurno.ToUpper()} · Refuerzos: {jugadorActual.TropasDisponibles}";
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

	// ===== Busca/crea label de tropas =====
	private Label CreateOrGetTroopLabel(NodoTerreno t)
	{
		if (!NodeVivo(t)) return null;

		// usar siempre "TropasLabel"
		var lbl = t.GetNodeOrNull<Label>("TropasLabel");

		// crear si no existe
		if (lbl == null)
		{
			lbl = new Label
			{
				Name = "TropasLabel",
				Text = t.Tropas.ToString(),
				AutowrapMode = TextServer.AutowrapMode.Off,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				TopLevel = false // ¡sin TopLevel!
			};
			lbl.MouseFilter = Control.MouseFilterEnum.Ignore;
			lbl.ZIndex = 200;
			lbl.AddThemeFontSizeOverride("font_size", 28);
			lbl.AddThemeColorOverride("font_color", new Color(1, 1, 1));
			lbl.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
			lbl.AddThemeConstantOverride("outline_size", 5);
			t.AddChild(lbl);
		}

		// eliminar duplicados (labels viejos pegados al Terreno)
		foreach (Node ch in t.GetChildren())
			if (ch is Label l && l != lbl) l.QueueFree();

		ReposicionarLabel(t, lbl);
		return lbl;
	}

	private void ReposicionarLabel(NodoTerreno t, Label lbl)
	{
		if (!NodeVivo(t) || !NodeVivo(lbl)) return;

		var poly = t.GetNodeOrNull<Polygon2D>("Polygon2D");
		if (!NodeVivo(poly) || poly.Polygon == null || poly.Polygon.Length < 3) return;

		Vector2 cLocal = Vector2.Zero;
		foreach (var p in poly.Polygon) cLocal += p;
		cLocal /= poly.Polygon.Length;

		// centro global y colocación del control
		Vector2 cGlobal = poly.GetGlobalTransform() * cLocal;
		Vector2 ms = lbl.GetMinimumSize();
		lbl.Size = ms;
		lbl.GlobalPosition = cGlobal - (ms * 0.5f);
	}

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

	// ======= Emisión de parches (host) =======
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

	// ======= Aplicar parches (todos) =======
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
					terr.SetDueno(owner, new Color(0.8f, 0.8f, 0.8f)); // fallback

				terr.SetTropas(tropas);

				_ = CreateOrGetTroopLabel(terr);
				ActualizarContadorTropas(terr);
				ActualizarInteractividadPorTurno();
			}
			else if (t == "patch_phase")
			{
				faseTurno = doc.RootElement.GetProperty("fase").GetString();
				var turnoId = doc.RootElement.GetProperty("turno").GetString();
				int refz = doc.RootElement.GetProperty("refuerzos").GetInt32();

				jugadorActual = turnoId == "J1" ? j1 : (turnoId == "J2" ? j2 : j3);
				if (jugadorActual != null) jugadorActual.TropasDisponibles = refz;

				_turnOwnerId = turnoId;

				if (faseTurno != "ataque")
				{
					origenSeleccionado = null;
					destinoSeleccionado = null;
					_btnLanzar?.Hide();
					_spAtk?.Hide();
					_spDef?.Hide();
					ActualizarHUDTrasSeleccion();
				}

				ActualizarUI();
				ActualizarInteractividadPorTurno();
			}
			else if (t == "start_defense")
			{
				var defender = doc.RootElement.GetProperty("defender").GetString();
				int max = doc.RootElement.GetProperty("max").GetInt32();
				var myId = GameManager.Instance?.MyId;

				// Mostrar panel SOLO al defensor (host o cliente)
				if (!string.IsNullOrEmpty(myId) && myId == defender)
					MostrarPanelDefensa(Mathf.Clamp(max, 1, 2));
				else
					OcultarPanelDefensa();

				// ⚠️ Importante:
				// NO limpiar selección en el host (autoridad), porque necesita origen/destino
				// para aplicar las bajas cuando llegue cmd_defense_choice.
				if (!_authoritative)
				{
					// En clientes no autoritativos (incluido el atacante), limpiar UI local.
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
		}
		catch (Exception e)
		{
			GD.PushWarning("[MapaUI] ApplyNetPatch error: " + e.Message);
		}
	}

	// ======= Procesar comandos (solo host) =======
	public void ProcessNetCommand(string json)
	{
		if (!_authoritative) return; // solo host

		using var doc = System.Text.Json.JsonDocument.Parse(json);
		var t = doc.RootElement.GetProperty("type").GetString();

		if (t == "cmd_click")
		{
			var terrId = doc.RootElement.GetProperty("terr").GetString();
			var actor  = doc.RootElement.GetProperty("actor").GetString(); // "J1"/"J2"/"J3"
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
		else if (t == "cmd_end_phase")
		{
			if (GetOwnerId(jugadorActual) != _turnOwnerId) return;
			if (faseTurno == "refuerzo") OnFinalizarRefuerzos();
			else                         OnFinalizarMovimiento();
		}
		else if (t == "cmd_defense_choice")
		{
			var actor = doc.RootElement.GetProperty("actor").GetString(); // "J1"/"J2"/"J3"
			var dice  = doc.RootElement.GetProperty("dice").GetInt32();

			if (!_esperandoDefensa || actor != _defenderOwnerId) return;

			tropasDefensaSeleccionadas = Mathf.Clamp(dice, 1, 2);

			// Tiradas y resolución
			dadosAtq.Clear();
			for (int i = 0; i < tropasAtaqueSeleccionadas; i++) dadosAtq.Add(rng.RandiRange(1, 6));
			dadosAtq.Sort(); dadosAtq.Reverse();

			dadosDef.Clear();
			for (int i = 0; i < tropasDefensaSeleccionadas; i++) dadosDef.Add(rng.RandiRange(1, 6));
			dadosDef.Sort(); dadosDef.Reverse();

			int comps = Math.Min(tropasAtaqueSeleccionadas, tropasDefensaSeleccionadas);
			int bajasDef = 0, bajasAtk = 0;
			for (int i = 0; i < comps; i++) if (dadosAtq[i] > dadosDef[i]) bajasDef++; else bajasAtk++;

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

			if (destinoSeleccionado != null && destinoSeleccionado.Tropas == 0 && origenSeleccionado != null)
			{
				var nuevoDuenoId = origenSeleccionado.DuenoId;
				AsignarDueno(destinoSeleccionado, nuevoDuenoId);
				origenSeleccionado.SetTropas(origenSeleccionado.Tropas - 1);
				destinoSeleccionado.SetTropas(1);
				ActualizarContadorTropas(origenSeleccionado);
				ActualizarContadorTropas(destinoSeleccionado);
				BroadcastTerr(origenSeleccionado);
				BroadcastTerr(destinoSeleccionado);
			}

			var br = new { type = "battle_result", atk = dadosAtq, def = dadosDef, bajasA = bajasAtk, bajasD = bajasDef };
			GameManager.Instance.BroadcastPatch(br);

			_esperandoDefensa = false;
			_defenderOwnerId = "";
			faseDados = "";
			origenSeleccionado = null;
			destinoSeleccionado = null;

			// Diseño actual: turno termina tras cada batalla
			OnFinalizarMovimiento();
		}
	}

	// ======= Defensa: UI helpers =======
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

	// ======= UI: visibilidad por turno/fase =======
	private void ActualizarInteractividadPorTurno()
	{
		var myId = GameManager.Instance?.MyId;
		bool miTurno = !_isOnline || string.IsNullOrEmpty(myId) || myId == _turnOwnerId;

		if (_btnFinRef != null) _btnFinRef.Visible = miTurno && faseTurno == "refuerzo";
		if (_btnFinMov != null) _btnFinMov.Visible = miTurno && faseTurno == "movimiento";

		bool puedeAtacar = miTurno && faseTurno == "ataque";
		if (_btnLanzar != null) _btnLanzar.Visible = puedeAtacar && origenSeleccionado != null && destinoSeleccionado != null;
		if (_spAtk != null) _spAtk.Visible = puedeAtacar && origenSeleccionado != null && destinoSeleccionado != null;
	}

	private static bool NodeVivo(Node n)
	{
		return n != null && GodotObject.IsInstanceValid(n) && n.IsInsideTree();
	}
}
