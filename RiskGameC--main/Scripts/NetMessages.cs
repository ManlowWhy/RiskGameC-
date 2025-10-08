using System.Collections.Generic;

namespace Scripts
{
	// NOTA: No heredan de MsgBase para evitar conflictos
	// Solo necesitan tener la propiedad 'type'

	public class CmdExchange
	{
		public string type { get; set; }   // "cmd_exchange"
		public string actor { get; set; }  // "J1"/"J2"/"J3"
		public List<int> idx { get; set; } // índices (mano) de las 3 cartas a canjear
	}

	public class PatchCards
	{
		public string type { get; set; }   // "patch_cards"
		public string actor { get; set; }  // jugador afectado
		public int n { get; set; }         // tamaño de la mano
	}

	public class PatchExchange
	{
		public string type { get; set; }   // "patch_exchange"
		public string actor { get; set; }  // jugador que canjeó
		public int fibo { get; set; }      // tropas otorgadas por Fibonacci
	}
}
