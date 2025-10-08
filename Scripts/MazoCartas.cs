using System;
using System.Collections.Generic;
using System.Linq;

namespace Scripts
{
	public class MazoCartas
	{
		private readonly Stack<Carta> _mazo = new();
		private readonly List<Carta> _descarte = new();
		private readonly Random _rng = new();

		public int Count => _mazo.Count;

		public MazoCartas(IEnumerable<string> territorios42)
		{
			var terrList = territorios42?.ToList() ?? new List<string>();
			var tipos = new[] { TipoCarta.Infanteria, TipoCarta.Caballeria, TipoCarta.Artilleria };

			var temp = new List<Carta>(terrList.Count);
			for (int i = 0; i < terrList.Count; i++)
			{
				var tipo = tipos[i % 3];               // rotación 0,1,2,0,1,2...
				temp.Add(new Carta(tipo, terrList[i]));
			}

			Barajar(temp);
			foreach (var c in temp) _mazo.Push(c);
		}

		private void Barajar(List<Carta> list)
		{
			for (int i = list.Count - 1; i > 0; i--)
			{
				int j = _rng.Next(i + 1);
				(list[i], list[j]) = (list[j], list[i]);
			}
		}

		/// Robar la carta 
		public Carta Robar()
		{
			if (_mazo.Count == 0) return null;           
			return _mazo.Pop();
		}


		public void Descartar(IEnumerable<Carta> cartas)
		{
			if (cartas == null) return;
			_descarte.AddRange(cartas);
		}
	}
}
