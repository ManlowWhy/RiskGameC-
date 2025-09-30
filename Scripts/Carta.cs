namespace Scripts
{
	public enum TipoCarta { Infanteria, Caballeria, Artilleria }

	public class Carta
	{
		public TipoCarta Tipo { get; set; }
		// Usa el nombre normalizado del Terreno (ej. "Africa1", "Sud2", "Nor3"...)
		public string TerritorioId { get; set; }

		public Carta() { }
		public Carta(TipoCarta tipo, string terrId)
		{
			Tipo = tipo;
			TerritorioId = terrId;
		}
	}
}
