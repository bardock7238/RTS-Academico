namespace Modelo
{
    // TODO PRÓXIMA SESIÓN (prioridad) — dejado como nota por el equipo:
    //   Eliminar el sistema POR TURNOS y pasar a CONCURRENCIA TOTAL estilo RTS
    //   (nada de NumeroTurno/JugadorActivo: todos los jugadores juegan a la vez).
    //   Implica mucha más concurrencia: acciones simultáneas de ambos jugadores
    //   (mover, construir, entrenar, recolectar, atacar) sincronizadas por red,
    //   y los ITEMS del equipo (pasivos, temporales, consumibles, equipables).
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