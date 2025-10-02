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

		/// <summary> Lista de territorios que controla (si decides mantenerla). </summary>
		public List<TerrenoNodo> Territorios { get; set; } = new List<TerrenoNodo>();

		/// <summary> Cartas del jugador (opcional; no interfiere si aún no usas cartas). </summary>
		public List<Carta> Cartas { get; set; } = new List<Carta>();

		/// <summary> Flag para otorgar carta al final del turno si conquistó (lo maneja MapaUI). </summary>
		public bool ConquistoEsteTurno { get; set; } = false;

		/// <summary>
		/// Calcula y añade refuerzos mínimos (3 o territorios/3). 
		/// Nota: MapaUI ya hace esto en IniciarTurno(); usa esto solo si prefieres hacerlo fuera.
		/// </summary>
		public void Refuerzos()
		{
			int baseRefuerzo = Math.Max(3, Territorios.Count / 3);
			TropasDisponibles += baseRefuerzo;
		}

		// =========================
		//         CARTAS
		// =========================

		/// <summary> Devuelve true si hay un trío válido y saca el trío por out. </summary>
		public bool TieneTrioValido(out List<Carta> trio)
		{
			trio = null;
			if (Cartas == null || Cartas.Count < 3) return false;

			// Ejemplo genérico: o 3 del mismo tipo o 1 de cada tipo.
			// Ajusta a tu modelo real de Carta (Tipo/Simbolo/etc.)
			var porTipo = Cartas.GroupBy(c => c.Tipo).ToDictionary(g => g.Key, g => g.ToList());

			// 1) Tres iguales
			foreach (var kv in porTipo)
			{
				if (kv.Value.Count >= 3)
				{
					trio = kv.Value.Take(3).ToList();
					return true;
				}
			}

			// 2) Uno de cada tipo (si tienes exactamente 3 tipos)
			if (porTipo.Keys.Count >= 3)
			{
				var diferentes = porTipo.Values.Select(v => v.First()).Take(3).ToList();
				if (diferentes.Count == 3)
				{
					trio = diferentes;
					return true;
				}
			}
			return false;
		}

		/// <summary> Recibe una carta (usado por MapaUI cuando roba tras conquistar). </summary>
		public void RecibirCarta(Carta c)
		{
			if (c != null) Cartas.Add(c);
		}

		/// <summary>
		/// Intercambia un trío por tropas (el valor de tropas lo decide MapaUI/FiboCounter).
		/// </summary>
		public void IntercambiarCartas(List<Carta> trio, int tropasOtorgadas)
		{
			if (trio == null || trio.Count != 3) return;
			foreach (var c in trio) Cartas.Remove(c);
			TropasDisponibles += Math.Max(0, tropasOtorgadas);
		}

		// Overload para compatibilidad si la llamas sin tropas (no hace nada más que quitar las cartas).
		public void IntercambiarCartas(List<Carta> trio)
		{
			if (trio == null || trio.Count != 3) return;
			foreach (var c in trio) Cartas.Remove(c);
		}
	}

	/// <summary>
	/// Ejemplo mínimo de Carta para compilar. 
	/// Adáptalo a tu implementación real (Tipo, Territorio, comodines, etc.).
	/// </summary>
	
}
