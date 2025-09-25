using Godot;
using Scripts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using NodoTerreno = global::Terreno;

public partial class MapaUI : Node2D
{
	private Jugador j1;
	private Jugador j2;
	private Jugador jugadorActual;

	private NodoTerreno origenSeleccionado = null;
	private NodoTerreno destinoSeleccionado = null;

	private string faseTurno = "refuerzo"; // refuerzo, ataque, movimiento
	private string faseDados = "";         // atacante, defensor

	private int tropasAtaqueSeleccionadas = 1;
	private int tropasDefensaSeleccionadas = 1;
	private readonly List<int> dadosAtq = new();
	private readonly List<int> dadosDef = new();
	private readonly RandomNumberGenerator rng = new();

	[Export] private NodePath TurnoLabelPath;
	[Export] private NodePath ResultadoDadoPath;
	[Export] private NodePath LanzarDadoPath;
	[Export] private NodePath FinalizarRefuerzosPath;
	[Export] private NodePath FinalizarMovimientoPath;
	[Export] private NodePath CantidadAtaquePath;
	[Export] private NodePath CantidadDefensaPath;

	private Label _turnoLabel, _resultado;
	private Button _btnLanzar, _btnFinRef, _btnFinMov;
	private SpinBox _spAtk, _spDef;

	private readonly Dictionary<string, (string Alias, Color Color)> _jugadores = new()
	{
		["J1"] = ("Christopher", new Color(0.9f, 0.2f, 0.2f)),
		["J2"] = ("Gabri",        new Color(0.2f, 0.4f, 0.9f))
	};

	private readonly Dictionary<NodoTerreno, HashSet<string>> _ady = new();

	public override void _Ready()
	{
		// HUD
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

		var hud = GetNodeOrNull<Control>("HUD");
		if (hud != null) hud.MouseFilter = Control.MouseFilterEnum.Ignore;

		// Jugadores
		j1 = new Jugador { Alias = "Christopher", Color = "Rojo", TropasDisponibles = 40 };
		j2 = new Jugador { Alias = "Gabri",       Color = "Azul", TropasDisponibles = 40 };
		jugadorActual = j1;

		// Grupo y señales
		var terrRoot = GetNodeOrNull<Node>("Territorios");
		if (terrRoot != null)
		{
			foreach (Node child in terrRoot.GetChildren())
				if (child is NodoTerreno tt && !tt.IsInGroup("Terreno")) tt.AddToGroup("Terreno");
		}

		Godot.Collections.Array<Node> lista = GetTree().GetNodesInGroup("Terreno");
		foreach (Node node in lista)
		{
			if (node is NodoTerreno t)
			{
				if (string.IsNullOrWhiteSpace(t.Nombre)) t.Nombre = t.Name;
				t.Connect(NodoTerreno.SignalName.Clicked, new Callable(this, nameof(OnTerrenoClicked)));
				t.Connect(NodoTerreno.SignalName.Hovered, new Callable(this, nameof(OnTerrenoHovered)));

				// asegurar label de tropas
				CreateOrGetTroopLabel(t);
				ActualizarContadorTropas(t);
			}
		}

		// Vecindad por geometría
		ConstruirAdyacencia(lista);

		// Estado inicial
		int turn = 0;
		foreach (Node node in lista)
		{
			if (node is NodoTerreno tInit)
			{
				var ownerId = (turn++ % 2 == 0) ? "J1" : "J2";
				var color   = (ownerId == "J1") ? new Color(0.9f,0.2f,0.2f) : new Color(0.2f,0.4f,0.9f);
				tInit.SetDueno(ownerId, color);
				tInit.SetTropas(3);
				ActualizarContadorTropas(tInit);
			}
		}

		rng.Randomize();
		IniciarTurno();
	}

	// ========= Vecindad sólo por geometría =========
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
		if (faseDados == "atacante")
		{
			tropasAtaqueSeleccionadas = (int)(_spAtk?.Value ?? 1);

			dadosAtq.Clear();
			for (int i = 0; i < tropasAtaqueSeleccionadas; i++) dadosAtq.Add(rng.RandiRange(1, 6));
			dadosAtq.Sort(); dadosAtq.Reverse();

			if (_resultado != null) _resultado.Text = $"Atacante: {string.Join(", ", dadosAtq)}";

			// ahora habilita al defensor
			_spAtk?.Hide();
			_spDef?.Show();
			faseDados = "defensor";
		}
		else if (faseDados == "defensor")
		{
			tropasDefensaSeleccionadas = (int)(_spDef?.Value ?? 1);

			dadosDef.Clear();
			for (int i = 0; i < tropasDefensaSeleccionadas; i++) dadosDef.Add(rng.RandiRange(1, 6));
			dadosDef.Sort(); dadosDef.Reverse();

			if (_resultado != null) _resultado.Text += $" | Defensor: {string.Join(", ", dadosDef)}";

			int comps = Math.Min(tropasAtaqueSeleccionadas, tropasDefensaSeleccionadas);
			int bajasDef = 0, bajasAtk = 0;
			for (int i = 0; i < comps; i++)
				if (dadosAtq[i] > dadosDef[i]) bajasDef++; else bajasAtk++;

			if (destinoSeleccionado != null)
			{
				destinoSeleccionado.SetTropas(Mathf.Max(0, destinoSeleccionado.Tropas - bajasDef));
				ActualizarContadorTropas(destinoSeleccionado);
			}
			if (origenSeleccionado != null)
			{
				origenSeleccionado.SetTropas(Mathf.Max(1, origenSeleccionado.Tropas - bajasAtk));
				ActualizarContadorTropas(origenSeleccionado);
			}

			if (destinoSeleccionado != null && destinoSeleccionado.Tropas == 0 && origenSeleccionado != null)
			{
				var nuevoDuenoId = origenSeleccionado.DuenoId;
				AsignarDueno(destinoSeleccionado, nuevoDuenoId);
				origenSeleccionado.SetTropas(origenSeleccionado.Tropas - 1);
				destinoSeleccionado.SetTropas(1);
				ActualizarContadorTropas(origenSeleccionado);
				ActualizarContadorTropas(destinoSeleccionado);
			}

			// reset / pasar turno
			_btnLanzar?.Hide();
			_spAtk?.Hide();
			_spDef?.Hide();
			faseDados = "";
			origenSeleccionado = null;
			destinoSeleccionado = null;

			OnFinalizarMovimiento();
		}
	}

	private void OnFinalizarRefuerzos()
	{
		faseTurno = "ataque";
		_btnFinRef?.Hide();
		ActualizarUI();
	}

	private void OnFinalizarMovimiento()
	{
		_btnFinMov?.Hide();
		faseTurno = "refuerzo";
		CambiarTurno();
	}

	// =========================
	//      HANDLERS MAPA
	// =========================
	private void OnTerrenoClicked(NodoTerreno t)
	{
		if (t == null) return;

		if (faseTurno == "refuerzo")
		{
			if (EsDueno(t, jugadorActual) && jugadorActual.TropasDisponibles > 0)
			{
				t.SetTropas(t.Tropas + 1);
				ActualizarContadorTropas(t);
				jugadorActual.TropasDisponibles -= 1;
				if (jugadorActual.TropasDisponibles == 0)
				{
					faseTurno = "ataque";
					_btnFinRef?.Hide();
				}
				ActualizarUI();
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
					ActualizarHUDTrasSeleccion();
				}
				return;
			}

			if (destinoSeleccionado == null)
			{
				if (t == origenSeleccionado) return;
				if (EsDueno(t, jugadorActual)) return;
				if (!SonVecinos(origenSeleccionado, t)) return;

				destinoSeleccionado = t;
				ConfigurarSpinBoxesParaBatalla();
				ActualizarHUDTrasSeleccion();
				return;
			}

			// tercer clic: reset selección
			origenSeleccionado = null;
			destinoSeleccionado = null;
			_btnLanzar?.Hide();
			_spAtk?.Hide();
			_spDef?.Hide();
			ActualizarHUDTrasSeleccion();
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

		ActualizarUI();
	}

	private void CambiarTurno()
	{
		jugadorActual = ReferenceEquals(jugadorActual, j1) ? j2 : j1;
		IniciarTurno();
	}

	private bool EsDueno(NodoTerreno t, Jugador j)
	{
		if (t == null || j == null) return false;
		string id = ReferenceEquals(j, j1) ? "J1" : "J2";
		return t.DuenoId == id;
	}

	private bool SonVecinos(NodoTerreno a, NodoTerreno b)
	{
		if (a == null || b == null) return false;
		if (!_ady.TryGetValue(a, out var set) || set == null) return false;

		var kbNombre = NormalizarNombre(b.Nombre);
		var kbName   = NormalizarNombre(b.Name);
		return set.Contains(kbNombre) || set.Contains(kbName);
	}

	private void ConfigurarSpinBoxesParaBatalla()
	{
		AjustarMaximosDados();
		faseDados = "atacante";
		_btnLanzar?.Show();

		// mostrar SOLO atacante; defensor aparecerá tras el primer lanzamiento
		_spAtk?.Show();
		_spDef?.Hide();
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
		var maxDef = Mathf.Min(2, Math.Max(0, destinoSeleccionado.Tropas));

		if (_spAtk != null)
		{
			_spAtk.MinValue = 1;
			_spAtk.MaxValue = Math.Max(1, maxAtk);
			if (_spAtk.Value > _spAtk.MaxValue) _spAtk.Value = _spAtk.MaxValue;
			if (_spAtk.Value < _spAtk.MinValue) _spAtk.Value = _spAtk.MinValue;
		}

		if (_spDef != null)
		{
			_spDef.MinValue = 1;
			_spDef.MaxValue = Math.Max(1, maxDef);
			if (_spDef.Value > _spDef.MaxValue) _spDef.Value = _spDef.MaxValue;
			if (_spDef.Value < _spDef.MinValue) _spDef.Value = _spDef.MinValue;
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
		if (_jugadores.TryGetValue(jugadorId, out var jcfg))
			t.SetDueno(jugadorId, jcfg.Color);
	}

// Busca el primer Label dentro del nodo (Terreno)
	private static Label BuscarPrimerLabel(Node n)
	{
		if (n == null) return null;
		foreach (var ch in n.GetChildren())
		{
			if (ch is Label lbl) return lbl;
			var rec = BuscarPrimerLabel(ch as Node);
			if (rec != null) return rec;
		}
		return null;
	}

// Crea (si no existe) el Label de tropas y lo centra sobre el Polygon2D (coordenadas GLOBALS)
	private Label CreateOrGetTroopLabel(NodoTerreno t)
	{
		if (t == null) return null;

		var lbl = BuscarPrimerLabel(t);
		if (lbl != null) return lbl;

		lbl = new Label
		{
			Name = "TropasLabel",
			Text = t.Tropas.ToString(),
			AutowrapMode = TextServer.AutowrapMode.Off,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			TopLevel = true // muy importante para usar GlobalPosition sin heredar transform del padre Node2D
		};
		// Que no bloquee clicks en el territorio
		lbl.MouseFilter = Control.MouseFilterEnum.Ignore;

		// Estilo para que se vea bien sobre el mapa
		lbl.AddThemeFontSizeOverride("font_size", 28);
		lbl.AddThemeColorOverride("font_color", new Color(1, 1, 1));
		lbl.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
		lbl.AddThemeConstantOverride("outline_size", 5);

		t.AddChild(lbl);

		ReposicionarLabel(t, lbl);
		return lbl;
	}
	// Centrado del label usando el CENTROIDE GLOBAL del Polygon2D
	private void ReposicionarLabel(NodoTerreno t, Label lbl)
	{
		if (t == null || lbl == null) return;

		var poly = t.GetNodeOrNull<Polygon2D>("Polygon2D");
		if (poly == null || poly.Polygon == null || poly.Polygon.Length < 3) return;

		// Centro geométrico en coordenadas LOCALES del polígono
		Vector2 cLocal = Vector2.Zero;
		foreach (var p in poly.Polygon) cLocal += p;
		cLocal /= poly.Polygon.Length;

		// A coordenadas GLOBALES del mundo 2D
		Vector2 cGlobal = poly.GetGlobalTransform() * cLocal;

		// Dimensionar al mínimo y centrar por tamaño
		Vector2 ms = lbl.GetMinimumSize();
		lbl.Size = ms;
		lbl.GlobalPosition = cGlobal - (ms * 0.5f);
	}

		// Actualiza texto y vuelve a centrar
	private void ActualizarContadorTropas(NodoTerreno t)
	{
		if (t == null) return;
		var lbl = CreateOrGetTroopLabel(t);
		if (lbl == null) return;

		lbl.Text = t.Tropas.ToString();

		// Recalcular tamaño y reposicionar (p. ej. al pasar de 9 a 10)
		Vector2 ms = lbl.GetMinimumSize();
		lbl.Size = ms;
		ReposicionarLabel(t, lbl);
	}
}
