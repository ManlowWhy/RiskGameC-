using System.Collections.Generic;
using System.Linq;
using Godot;
using Scripts;              
using NodoTerreno = global::Terreno;

public static class TerrenoCompat
{
	public static IEnumerable<NodoTerreno> Adyacentes(this NodoTerreno t)
	{
		var tree = Engine.GetMainLoop() as SceneTree;
		if (tree == null) yield break;

		// Todos los territorios
		var todos = tree.GetNodesInGroup("Terreno")
						.OfType<NodoTerreno>();

		// Resolver por nombre
		foreach (var nombre in t.Vecinos)
		{
			var vecino = todos.FirstOrDefault(x => x.Nombre == nombre);
			if (vecino != null)
				yield return vecino;
		}
	}

	public static void CambiarDueno(this NodoTerreno t, Jugador j)
	{
		var color = j.Color switch
		{
			"Rojo"  => new Color(1, 0, 0),
			"Azul"  => new Color(0, 0, 1),
			"Verde" => new Color(0, 1, 0),
			_       => new Color(0.7f, 0.7f, 0.7f)
		};

		t.SetDueno(j.Alias, color);
	}
}
