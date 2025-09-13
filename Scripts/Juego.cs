namespace Scripts
{
	using System.Collections.Generic;

public class Juego
{
	public List<Jugador> Jugadores { get; set; }
	public List<Continente> Continentes { get; set; }
	public List<Terreno> Terrenos { get; set; }
	public int ContadorIntercambio { get; set; }

	public void IniciarPartida() { }
	public void AsignarTerritorios() { }
	public void TurnoJugador(Jugador jugador) { }
	public bool VerificarVictoria(Jugador jugador) { return false; }
}
}
