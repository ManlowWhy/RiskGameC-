using System.Collections.Generic;
using System.Linq;
using Godot;
using Scripts;                       // Para el tipo Jugador
using NodoTerreno = global::Terreno; // Tu script de Godot adjunto a Area2D

// Métodos de extensión para mantener compatibilidad con el código antiguo
public static class TerrenoCompat
{
	/// <summary>
	/// Devuelve los territorios adyacentes resolviendo los nombres en Vecinos
	/// usando el grupo "Terreno" de la escena actual.
	/// Permite llamar:  territorio.Adyacentes()
	/// </summary>
	public static IEnumerable<NodoTerreno> Adyacentes(this NodoTerreno t)
	{
		var tree = Engine.GetMainLoop() as SceneTree;
		if (tree == null) yield break;

		// Todos los territorios de la escena
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

	/// <summary>
	/// Reemplazo de CambiarDueno(jugador).
	/// Pinta el territorio y asigna DuenoId en base al Alias del jugador.
	/// </summary>
	public static void CambiarDueno(this NodoTerreno t, Jugador j)
	{
		// Mapea el color string del jugador a Godot.Color
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
