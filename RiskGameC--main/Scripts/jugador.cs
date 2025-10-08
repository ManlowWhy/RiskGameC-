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

		// TERRITORIOS
		public List<TerrenoNodo> Territorios { get; set; } = new List<TerrenoNodo>();

		// CARTAS
		public List<Carta> Cartas { get; } = new List<Carta>();

		// FLAGS
		public bool ConquistoEsteTurno { get; set; } = false;
		public bool RecibioCartaEsteTurno { get; set; } = false;

		// ===== CARTAS =====

		// TRIO VALIDO
		public bool TieneTrioValido(out List<Carta> trio)
		{
			trio = null;
			if (Cartas == null || Cartas.Count < 3) return false;

			var porTipo = Cartas.GroupBy(c => c.Tipo).ToDictionary(g => g.Key, g => g.ToList());
			foreach (var kv in porTipo)
			{
				if (kv.Value.Count >= 3)
				{
					trio = kv.Value.Take(3).ToList();
					return true;
				}
			}

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

		// RECIBIR CARTA
		public void RecibirCarta(Carta c)
		{
			if (c != null) Cartas.Add(c);
		}

		// INTERCAMBIAR + TROPAS
		public void IntercambiarCartas(List<Carta> trio, int tropasOtorgadas)
		{
			if (trio == null || trio.Count != 3) return;
			foreach (var c in trio) Cartas.Remove(c);
			TropasDisponibles += Math.Max(0, tropasOtorgadas);
		}

		// INTERCAMBIAR (SIN TROPAS)
		public void IntercambiarCartas(List<Carta> trio)
		{
			if (trio == null || trio.Count != 3) return;
			foreach (var c in trio) Cartas.Remove(c);
		}

		// CONTEO TIPOS
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

		// TRIO DETERMINISTA
		public List<Carta> ElegirTrioDeterminista()
		{
			if (Cartas == null || Cartas.Count < 3) return null;
			var (inf, cab, art) = ConteoPorTipo();

			if (inf > 0 && cab > 0 && art > 0)
			{
				return new List<Carta>
				{
					Cartas.First(c => c.Tipo == TipoCarta.Infanteria),
					Cartas.First(c => c.Tipo == TipoCarta.Caballeria),
					Cartas.First(c => c.Tipo == TipoCarta.Artilleria)
				};
			}

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
