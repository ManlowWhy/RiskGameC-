namespace Scripts
{
using System.Collections.Generic;

public class Terreno
{
	public string Nombre { get; set; }
	public Jugador Dueno { get; set; }
	public int Tropas { get; set; }
	public List<Terreno> Adyacentes { get; set; } = new List<Terreno>();

	public void CambiarDueno(Jugador nuevoDueno) { }
	public void AgregarTropas(int n) { Tropas += n; }
	public void RemoverTropas(int n) { Tropas -= n; }
}
}
