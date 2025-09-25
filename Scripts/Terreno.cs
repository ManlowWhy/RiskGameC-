using Godot;

public partial class Terreno : Area2D
{
	[Export] public string Nombre = "";
	[Export] public int Tropas = 1;
	[Export] public string DuenoId = "";
	[Export] public Godot.Collections.Array<string> Vecinos = new();

	[Signal] public delegate void ClickedEventHandler(Terreno who);
	[Signal] public delegate void HoveredEventHandler(Terreno who, bool entered);

	private Polygon2D _poly;
	private Label _lbl; // <-- NUEVO

	public override void _Ready()
	{
		_poly = GetNodeOrNull<Polygon2D>("Polygon2D");

		// Label de tropas
		_lbl = new Label
		{
			Name = "TropasLabel",
			Text = Tropas.ToString(),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment   = VerticalAlignment.Center
		};
		AddChild(_lbl);
		_lbl.ZIndex = 1000;          // que quede encima
		_lbl.MouseFilter = Control.MouseFilterEnum.Ignore; // no bloquee clics

		InputPickable = true;
		MouseEntered += () => EmitSignal(SignalName.Hovered, this, true);
		MouseExited  += () => EmitSignal(SignalName.Hovered, this, false);
		InputEvent   += OnInputEvent;

		// Aviso si falta colisión
		var col = GetNodeOrNull<CollisionPolygon2D>("CollisionPolygon2D");
		if (col == null)
			GD.PrintErr($"[WARN] {Name} no tiene CollisionPolygon2D");

		UpdateLabel(); // <-- coloca y colorea el label
	}

	private void OnInputEvent(Node viewport, InputEvent e, long shapeIdx)
	{
		if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
			EmitSignal(SignalName.Clicked, this);
	}

	public void SetDueno(string nuevoDuenoId, Color colorDueño)
	{
		DuenoId = nuevoDuenoId;
		UpdateColor(colorDueño);
	}

	public void SetTropas(int n)
	{
		Tropas = Mathf.Max(0, n);
		UpdateLabel(); // <-- actualizar texto
	}

	public void UpdateColor(Color c)
	{
		if (_poly != null) _poly.Color = c;
		UpdateLabel(); // <-- ajustar contraste del texto
	}

	// ---------- Helpers UI ----------
	private void UpdateLabel()
	{
		if (_lbl == null) return;

		_lbl.Text = Tropas.ToString();

		// Posicionar en el centro del polígono
		_lbl.Position = ComputeCentroid();

		// Color del texto con buen contraste sobre el fill del polígono
		var baseCol = _poly != null ? _poly.Color : Colors.Gray;
		float luma = 0.2126f * baseCol.R + 0.7152f * baseCol.G + 0.0722f * baseCol.B;
		_lbl.Modulate = (luma < 0.5f) ? Colors.White : Colors.Black;
	}

	private Vector2 ComputeCentroid()
	{
		if (_poly == null || _poly.Polygon == null || _poly.Polygon.Length == 0)
			return Vector2.Zero;

		var pts = _poly.Polygon;
		Vector2 sum = Vector2.Zero;
		foreach (var p in pts) sum += p;
		return sum / pts.Length; // centroide simple
	}
}
