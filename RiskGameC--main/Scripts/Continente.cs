namespace Scripts
{
using System.Collections.Generic;

public class Continente
{
	public string Nombre { get; set; }
	public int Bonus { get; set; }
	public List<Terreno> Territorios { get; set; } = new List<Terreno>();

	public bool EsControladoPor(Jugador jugador) { return false; }
}
}
