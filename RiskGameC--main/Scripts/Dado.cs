namespace Scripts
{
using System;
using System.Collections.Generic;

public class Dado
{
	private Random rng = new Random();

	public List<int> LanzarDados(int cantidad)
	{
		var resultados = new List<int>();
		for (int i = 0; i < cantidad; i++)
		{
			resultados.Add(rng.Next(1, 7)); // 1 a 6
		}
		return resultados;
	}
}
}
