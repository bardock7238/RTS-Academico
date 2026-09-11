namespace Modelo
{
    public class Partida
    {
        public bool EnEjecucion { get; set; }
        public int TiempoJuegoSegundos { get; set; }
        public int NumeroTurno { get; set; }
        public string JugadorActivo { get; set; }    // Nombre del jugador con turno actual
        public string GanadorNombre { get; set; }     // Nombre del ganador, null si la partida sigue

        public Partida()
        {
            EnEjecucion = true;
            TiempoJuegoSegundos = 0;
            NumeroTurno = 1;
            JugadorActivo = null;
            GanadorNombre = null;
        }

        public void AvanzarTurno(string siguienteJugador)
        {
            NumeroTurno++;
            JugadorActivo = siguienteJugador;
        }

        public void Finalizar(string ganador)
        {
            EnEjecucion = false;
            GanadorNombre = ganador;
        }
    }
}