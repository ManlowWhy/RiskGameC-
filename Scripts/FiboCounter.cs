namespace Scripts
{
	public class FiboCounter
	{
		private int _a = 1; 
		private int _b = 2; 
		public int Actual { get; private set; } = 0;

		public int Avanzar()
		{
			// Primer canje retorna 2 (si Actual==0)
			if (Actual == 0) { Actual = 2; return Actual; }
			int next = _a + _b;
			_a = _b;
			_b = next;
			Actual = _a;
			return Actual;
		}

		public void Reset()
		{
			_a = 1; _b = 2; Actual = 0;
		}
	}
}
