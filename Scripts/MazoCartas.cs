using Godot;
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
			// Distribución balanceada 3 tipos (14/14/14) y baraja
			var terrList = territorios42.ToList();
			var tipos = new[] { TipoCarta.Infanteria, TipoCarta.Caballeria, TipoCarta.Artilleria };
			int i = 0;
			var temp = new List<Carta>(terrList.Count);
			foreach (var t in terrList)
			{
				var tipo = tipos[i % 3];
				temp.Add(new Carta(tipo, t));
				i++;
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

		public Carta Robar()
		{
			if (_mazo.Count == 0) return null; // En este proyecto: no se recicla descarte
			return _mazo.Pop();
		}

		public void Descartar(IEnumerable<Carta> cartas) => _descarte.AddRange(cartas);
	}
}
