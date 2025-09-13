namespace Scripts
{
	using System;
	using System.Collections.Generic;
	using Godot;

	public class Jugador
	{
		public string Alias { get; set; }
		public string Color { get; set; }
		public int TropasDisponibles { get; set; }
		public List<Terreno> Territorios { get; set; } = new List<Terreno>();
		public List<Carta> Cartas { get; set; } = new List<Carta>();

		public void Refuerzos()
		{
			int refuerzo = Math.Max(3, Territorios.Count / 3);
			TropasDisponibles += refuerzo;
		}

		public void Atacar(Terreno origen, Terreno destino)
		{
			if (origen.Tropas < 2 || !origen.Adyacentes.Contains(destino))
			{
				GD.Print("No se puede atacar");
				return;
			}

			// Lanzar dados según tropas disponibles
			Dado dado = new Dado();
			var atacante = dado.LanzarDados(Math.Min(3, origen.Tropas - 1));
			var defensor = dado.LanzarDados(Math.Min(2, destino.Tropas));

			atacante.Sort(); atacante.Reverse();
			defensor.Sort(); defensor.Reverse();

			GD.Print($"{origen.Nombre} ({origen.Tropas}) ataca a {destino.Nombre} ({destino.Tropas})");
			GD.Print($"Dados atacante: {string.Join(",", atacante)} | Dados defensor: {string.Join(",", defensor)}");

			int comparaciones = Math.Min(atacante.Count, defensor.Count);
			for (int i = 0; i < comparaciones; i++)
			{
				if (atacante[i] > defensor[i])
				{
					destino.Tropas--;
					GD.Print($"Defensor pierde 1 → {destino.Nombre} tropas: {destino.Tropas}");
				}
				else
				{
					origen.Tropas--;
					GD.Print($"Atacante pierde 1 → {origen.Nombre} tropas: {origen.Tropas}");
				}
			}

			// Conquista
			if (destino.Tropas <= 0)
			{
				GD.Print($"{destino.Nombre} ha sido conquistado por {Alias}!");

				destino.CambiarDueno(this);

				// Mover al menos tantas tropas como dados atacó (o 1 mínimo)
				int tropasMover = Math.Min(origen.Tropas - 1, atacante.Count);
				if (tropasMover < 1) tropasMover = 1;

				destino.Tropas = tropasMover;
				origen.Tropas -= tropasMover;

				Territorios.Add(destino);

				GD.Print($"{Alias} mueve {tropasMover} tropas a {destino.Nombre}");
			}
		}

		public void Planear() { }
		public void IntercambiarCartas(List<Carta> trio) { }
	}
}
