namespace Scripts
{
public enum TipoCarta { Infanteria, Caballeria, Artilleria }

public class Carta
{
	public TipoCarta Tipo { get; set; }
	public Terreno TerritorioRelacionado { get; set; }

	public void Usar() { }
}
}
