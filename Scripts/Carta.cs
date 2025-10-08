namespace Scripts
{
	public enum TipoCarta { Infanteria, Caballeria, Artilleria }

	public class Carta
	{
		public TipoCarta Tipo { get; set; }
		public string TerritorioId { get; set; }

		public Carta() { }
		public Carta(TipoCarta tipo, string terrId)
		{
			Tipo = tipo;
			TerritorioId = terrId;
		}
	}
}
