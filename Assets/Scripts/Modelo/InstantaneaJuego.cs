using System.Collections.Generic;

namespace Modelo
{
    // [Concurrencia] INSTANTÁNEA (foto) del mundo para que la Vista pueda PINTAR sin
    // tocar las listas vivas mientras los Tasks las modifican.
    //
    // Problema que resuelve: la Vista recorre `Jugador.Unidades`, `Edificios`, items...
    // en cada frame, y al mismo tiempo un Task de fondo puede estar agregando/quitando
    // bajo lock(Candado). Si la Vista iterara la lista ORIGINAL sin candado, saltaría
    // "Collection was modified" o dibujaría a medias.
    //
    // Solución: `Simulacion.Instantanea()` copia las LISTAS bajo el candado (una sola
    // vez, rápido) y devuelve este objeto. La Vista ya itera copias seguras.
    //
    // Nota: se copian las LISTAS (no los objetos). Los objetos Unidad/Edificio siguen
    // siendo los del Modelo; sus campos pueden cambiar entre frames, pero eso es lo
    // normal en un RTS: la Vista se redibuja cada frame.
    public class InstantaneaJuego
    {
        public List<Unidad> UnidadesLocal { get; }
        public List<Unidad> UnidadesEnemigo { get; }
        public List<Edificio> EdificiosLocal { get; }
        public List<Edificio> EdificiosEnemigo { get; }
        public List<Recurso> Recursos { get; }
        public List<Item> Items { get; }

        public int Oro { get; }
        public int Madera { get; }
        public int Comida { get; }
        public int TiempoJuegoSegundos { get; }
        public bool EnEjecucion { get; }
        public string GanadorNombre { get; }

        public InstantaneaJuego(
            List<Unidad> unidadesLocal,
            List<Unidad> unidadesEnemigo,
            List<Edificio> edificiosLocal,
            List<Edificio> edificiosEnemigo,
            List<Recurso> recursos,
            List<Item> items,
            int oro, int madera, int comida,
            int tiempoJuegoSegundos,
            bool enEjecucion,
            string ganadorNombre)
        {
            UnidadesLocal = unidadesLocal;
            UnidadesEnemigo = unidadesEnemigo;
            EdificiosLocal = edificiosLocal;
            EdificiosEnemigo = edificiosEnemigo;
            Recursos = recursos;
            Items = items;
            Oro = oro;
            Madera = madera;
            Comida = comida;
            TiempoJuegoSegundos = tiempoJuegoSegundos;
            EnEjecucion = enEjecucion;
            GanadorNombre = ganadorNombre;
        }
    }
}
