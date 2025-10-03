namespace Scripts
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Godot;
	using TerrenoNodo = global::Terreno;

	public class Jugador
	{
		public string Alias { get; set; }
		public string Color { get; set; }
		public int TropasDisponibles { get; set; }

		/// <summary>Lista de territorios que controla.</summary>
		public List<TerrenoNodo> Territorios { get; set; } = new List<TerrenoNodo>();

		/// <summary>Cartas del jugador.</summary>
		public List<Carta> Cartas { get; } = new List<Carta>();

		/// <summary>Flags opcionales (no estrictamente usados por MapaUI).</summary>
		public bool ConquistoEsteTurno { get; set; } = false;
		public bool RecibioCartaEsteTurno { get; set; } = false;

		// =========================
		//         CARTAS
		// =========================

		/// <summary>Devuelve true si hay un trío válido y lo entrega en 'trio'.</summary>
		public bool TieneTrioValido(out List<Carta> trio)
		{
			trio = null;
			if (Cartas == null || Cartas.Count < 3) return false;

			// Tres iguales
			var porTipo = Cartas.GroupBy(c => c.Tipo).ToDictionary(g => g.Key, g => g.ToList());
			foreach (var kv in porTipo)
			{
				if (kv.Value.Count >= 3)
				{
					trio = kv.Value.Take(3).ToList();
					return true;
				}
			}

			// Uno de cada (si existen los 3 tipos)
			if (porTipo.Keys.Contains(TipoCarta.Infanteria) &&
				porTipo.Keys.Contains(TipoCarta.Caballeria) &&
				porTipo.Keys.Contains(TipoCarta.Artilleria))
			{
				trio = new List<Carta>
				{
					porTipo[TipoCarta.Infanteria].First(),
					porTipo[TipoCarta.Caballeria].First(),
					porTipo[TipoCarta.Artilleria].First()
				};
				return true;
			}

			return false;
		}

		/// <summary>Recibe una carta.</summary>
		public void RecibirCarta(Carta c)
		{
			if (c != null) Cartas.Add(c);
		}

		/// <summary>Intercambia un trío por tropas (valor lo decide MapaUI/FiboCounter).</summary>
		public void IntercambiarCartas(List<Carta> trio, int tropasOtorgadas)
		{
			if (trio == null || trio.Count != 3) return;
			foreach (var c in trio) Cartas.Remove(c);
			TropasDisponibles += Math.Max(0, tropasOtorgadas);
		}

		/// <summary>Overload sin tropas (solo quita cartas).</summary>
		public void IntercambiarCartas(List<Carta> trio)
		{
			if (trio == null || trio.Count != 3) return;
			foreach (var c in trio) Cartas.Remove(c);
		}

		/// <summary>Conteo por tipo para HUD y lógica.</summary>
		public (int inf, int cab, int art) ConteoPorTipo()
		{
			int inf = 0, cab = 0, art = 0;
			foreach (var c in Cartas)
			{
				if (c.Tipo == TipoCarta.Infanteria) inf++;
				else if (c.Tipo == TipoCarta.Caballeria) cab++;
				else if (c.Tipo == TipoCarta.Artilleria) art++;
			}
			return (inf, cab, art);
		}

		/// <summary>Selecciona un trío determinista (prioriza uno de cada; si no, tres iguales).</summary>
		public List<Carta> ElegirTrioDeterminista()
		{
			if (Cartas == null || Cartas.Count < 3) return null;
			var (inf, cab, art) = ConteoPorTipo();

			// Uno de cada
			if (inf > 0 && cab > 0 && art > 0)
			{
				return new List<Carta>
				{
					Cartas.First(c => c.Tipo == TipoCarta.Infanteria),
					Cartas.First(c => c.Tipo == TipoCarta.Caballeria),
					Cartas.First(c => c.Tipo == TipoCarta.Artilleria)
				};
			}

			// Tres iguales del tipo más abundante
			var counts = new (TipoCarta tipo, int n)[] {
				(TipoCarta.Infanteria, inf),
				(TipoCarta.Caballeria, cab),
				(TipoCarta.Artilleria, art)
			};
			var mejor = counts.OrderByDescending(x => x.n).First();
			if (mejor.n >= 3) return Cartas.Where(c => c.Tipo == mejor.tipo).Take(3).ToList();

			return null;
		}
	}
}
