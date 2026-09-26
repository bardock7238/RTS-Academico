namespace Modelo
{
    // PARTIDA RTS EN TIEMPO REAL (sin turnos).
    // El tiempo lo mueve un reloj de fondo en el Controlador (concurrencia constante):
    // mientras la partida corre, TiempoJuegoSegundos sube 1 por cada segundo real.
    // AMBOS jugadores ejecutan acciones a la vez (mover, construir, entrenar, atacar).
    public class Partida
    {
        public bool EnEjecucion { get; set; }
        public int TiempoJuegoSegundos { get; set; }
        public string GanadorNombre { get; set; }     // Nombre del ganador, null si la partida sigue
        public string MotivoVictoria { get; set; }    // Causa del fin (regicidio, aniquilación...), null si sigue

        public Partida()
        {
            EnEjecucion = true;
            TiempoJuegoSegundos = 0;
            GanadorNombre = null;
            MotivoVictoria = null;
        }

        public void Finalizar(string ganador, string motivo = null)
        {
            EnEjecucion = false;
            GanadorNombre = ganador;
            MotivoVictoria = motivo;
        }
    }
}