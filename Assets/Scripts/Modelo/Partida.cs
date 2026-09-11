namespace Modelo
{
    public class Partida
    {
        public bool EnEjecucion { get; set; }
        public int TiempoJuegoSegundos { get; set; }

        public Partida()
        {
            EnEjecucion = true;
            TiempoJuegoSegundos = 0;
        }
    }
}