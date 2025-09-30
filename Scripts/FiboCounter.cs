namespace Scripts
{
	// Contador global de canjes: 2,3,5,8,13,21,34,...
	public class FiboCounter
	{
		private int _a = 1; // arranque para producir 2
		private int _b = 2; // arranque para producir 3
		public int Actual { get; private set; } = 0;

		public int Avanzar()
		{
			// Primer canje retorna 2 (si Actual==0)
			if (Actual == 0) { Actual = 2; return Actual; }
			int next = _a + _b;
			_a = _b;
			_b = next;
			Actual = _a; // secuencia visible: 2,3,5,8,13,...
			return Actual;
		}

		public void Reset()
		{
			_a = 1; _b = 2; Actual = 0;
		}
	}
}
