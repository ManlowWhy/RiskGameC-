using Godot;
using Scripts;

public partial class MapaUI : Node2D
{
	private Jugador j1;
	private Jugador j2;
	private Jugador jugadorActual;

	private Terreno costaRica;
	private Terreno panama;

	private Terreno origenSeleccionado = null;
	private Terreno destinoSeleccionado = null;
	
	private string faseDados = ""; 
	private int resultadoAtacante = 0;
	private int resultadoDefensor = 0;
	private string faseTurno = "refuerzo";

	public override void _Ready()
{
	// Crear jugadores
	j1 = new Jugador { Alias = "Christopher", Color = "Rojo", TropasDisponibles = 40 };
	j2 = new Jugador { Alias = "Gabri", Color = "Azul", TropasDisponibles = 40 };

	jugadorActual = j1;

	// Crear territorios...
	costaRica = new Terreno { Nombre = "Costa Rica", Tropas = 5, Dueno = j1 };
	panama = new Terreno { Nombre = "Panamá", Tropas = 3, Dueno = j2 };

	// Conexiones y listas...
	costaRica.Adyacentes.Add(panama);
	panama.Adyacentes.Add(costaRica);

	j1.Territorios.Add(costaRica);
	j2.Territorios.Add(panama);

	// Conectar botones
	GetNode<Button>("CostaRica").Pressed += () => OnTerrenoClicked(costaRica);
	GetNode<Button>("Panama").Pressed += () => OnTerrenoClicked(panama);

	GetNode<Button>("LanzarDado").Pressed += OnLanzarDado;
	GetNode<Button>("FinalizarRefuerzos").Pressed += OnFinalizarRefuerzos;

	// Ocultar botones especiales al inicio
	GetNode<Button>("LanzarDado").Visible = false;
	GetNode<Button>("FinalizarRefuerzos").Visible = false;

	// 🔥 Arranca el primer turno correctamente en fase refuerzo
	IniciarTurno();
}
	
	private void OnLanzarDado()
{
	var random = new RandomNumberGenerator();
	int resultado = random.RandiRange(1, 6);

	if (faseDados == "atacante")
	{
		resultadoAtacante = resultado;
		GD.Print($"[{jugadorActual.Alias}] (Atacante) lanzó el dado y sacó {resultado}");
		GetNode<Label>("ResultadoDado").Text = $"Atacante sacó: {resultado}";

		// Cambiar a fase del defensor
		faseDados = "defensor";
	}
	else if (faseDados == "defensor")
	{
		resultadoDefensor = resultado;
		GD.Print($"[{destinoSeleccionado.Dueno.Alias}] (Defensor) lanzó el dado y sacó {resultado}");
		GetNode<Label>("ResultadoDado").Text += $" | Defensor sacó: {resultado}";

		// Determinar resultado
		if (resultadoAtacante > resultadoDefensor)
		{
			GD.Print("El atacante gana esta ronda");
			destinoSeleccionado.Tropas -= 1;
		}
		else
		{
			GD.Print("El defensor gana esta ronda");
			origenSeleccionado.Tropas -= 1;
		}

		// Actualizar UI
		ActualizarUI();

		// Pasar turno
		CambiarTurno();

		// Resetear estado
		origenSeleccionado = null;
		destinoSeleccionado = null;
		resultadoAtacante = 0;
		resultadoDefensor = 0;
		faseDados = "";
		GetNode<Button>("LanzarDado").Visible = false;
	}
}
	private void OnTerrenoClicked(Terreno t)
{
	if (faseTurno == "refuerzo")
{
	if (t.Dueno == jugadorActual && jugadorActual.TropasDisponibles > 0)
	{
		t.Tropas += 1;
		jugadorActual.TropasDisponibles -= 1;

		GD.Print($"{jugadorActual.Alias} colocó 1 tropa en {t.Nombre}. Restan {jugadorActual.TropasDisponibles}");

		if (jugadorActual.TropasDisponibles == 0)
		{
			GD.Print("Se usaron todos los refuerzos. Pasando a fase de ataque.");
			faseTurno = "ataque";
			GetNode<Button>("FinalizarRefuerzos").Visible = false;
		}

		ActualizarUI();
	}
	else
	{
		GD.Print("Solo puedes reforzar tus propios territorios.");
	}
}
	else if (faseTurno == "ataque")
	{
		// Selección de ataque → igual que ya lo tienes
		if (origenSeleccionado == null)
		{
			if (t.Dueno == jugadorActual)
			{
				origenSeleccionado = t;
				GD.Print($"[{jugadorActual.Alias}] seleccionó {t.Nombre} ({t.Tropas} tropas)");
			}
			else
			{
				GD.Print($"No puedes seleccionar {t.Nombre}, no es tu territorio.");
			}
		}
		else
		{
			if (t != origenSeleccionado && origenSeleccionado.Adyacentes.Contains(t))
			{
				destinoSeleccionado = t;
				GD.Print($"{origenSeleccionado.Nombre} está listo para atacar a {t.Nombre}");

				faseDados = "atacante";
				GetNode<Button>("LanzarDado").Visible = true;
				GetNode<Label>("ResultadoDado").Text = "Esperando ataque...";
			}
			else
			{
				GD.Print($"{t.Nombre} no es un destino válido.");
				origenSeleccionado = null;
			}
		}
	}
}

private void IniciarTurno()
{
	// Calcular refuerzos
	jugadorActual.TropasDisponibles = Mathf.Max(3, jugadorActual.Territorios.Count / 3);
	faseTurno = "refuerzo";

	GD.Print($"Ahora es turno de {jugadorActual.Alias} con {jugadorActual.TropasDisponibles} refuerzos");

	// Mostrar botón de refuerzos
	GetNode<Button>("FinalizarRefuerzos").Visible = true;

	ActualizarUI();
}

	private void CambiarTurno()
{
	jugadorActual = (jugadorActual == j1) ? j2 : j1;
	IniciarTurno();
}

private void OnFinalizarRefuerzos()
{
	GD.Print($"{jugadorActual.Alias} terminó refuerzos con {jugadorActual.TropasDisponibles} sin usar");
	faseTurno = "ataque";

	// Ocultar el botón
	GetNode<Button>("FinalizarRefuerzos").Visible = false;

	ActualizarUI();
}

private Color GetColorJugador(Jugador jugador)
{
	GD.Print($"[DEBUG] Jugador {jugador.Alias} tiene Color='{jugador.Color}'");

	return jugador.Color switch
	{
		"Rojo" => new Color(1, 0, 0),
		"Azul" => new Color(0, 0, 1),
		"Verde" => new Color(0, 1, 0),
		_ => new Color(0.7f, 0.7f, 0.7f)
	};
}

	private void SetButtonStyle(Button button, Jugador dueno)
{
	// Siempre crear un StyleBoxFlat nuevo para este botón
	var style = new StyleBoxFlat
	{
		BgColor = GetColorJugador(dueno)
	};

	// Aplicar a todos los estados
	button.AddThemeStyleboxOverride("normal", style);
	button.AddThemeStyleboxOverride("hover", style);
	button.AddThemeStyleboxOverride("pressed", style);
	button.AddThemeStyleboxOverride("disabled", style);

	button.Flat = false;

	// Texto blanco
	button.AddThemeColorOverride("font_color", Colors.White);
	button.AddThemeColorOverride("font_hover_color", Colors.White);
	button.AddThemeColorOverride("font_pressed_color", Colors.White);
	button.AddThemeColorOverride("font_focus_color", Colors.White);

	button.QueueRedraw();
}

	private void ActualizarUI()
	{
		var btnCR = GetNode<Button>("CostaRica");
		var btnPA = GetNode<Button>("Panama");

		btnCR.Text = $"{costaRica.Nombre}\nTropas: {costaRica.Tropas}";
		btnPA.Text = $"{panama.Nombre}\nTropas: {panama.Tropas}";

		SetButtonStyle(btnCR, costaRica.Dueno);
		SetButtonStyle(btnPA, panama.Dueno);

		// Actualizar el label de turno
		var turnoLabel = GetNode<Label>("TurnoLabel");
		turnoLabel.Text = $"Turno: {jugadorActual.Alias} ({jugadorActual.Color})";
	}
}
