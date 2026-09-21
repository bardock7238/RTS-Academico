using System.Collections.Generic;
using System.Linq;

namespace Modelo
{
    public class Mapa
    {
        public const int Ancho = 30;
        public const int Alto = 30;

        // Lista de recursos colocados en casillas del mapa
        public List<Recurso> RecursosEnMapa { get; set; }

        public Mapa()
        {
            RecursosEnMapa = DatosDelJuego.CrearRecursosIniciales();
        }

        public bool EsCoordenadaValida(int x, int y)
        {
            return x >= 0 && x < Ancho && y >= 0 && y < Alto;
        }

        public Recurso ObtenerRecursoEn(int x, int y)
        {
            return RecursosEnMapa.FirstOrDefault(r => r.PosicionX == x && r.PosicionY == y);
        }

        public bool CasillaTieneRecurso(int x, int y)
        {
            return ObtenerRecursoEn(x, y) != null;
        }

        // Una casilla está libre si no hay unidad ni edificio (de ningún jugador) sobre ella.
        public bool EsCasillaLibre(int x, int y, List<Unidad> unidades, List<Edificio> edificios)
        {
            if (!EsCoordenadaValida(x, y)) return false;
            if (unidades.Any(u => u.PosicionX == x && u.PosicionY == y)) return false;
            if (edificios.Any(e => e.PosicionX == x && e.PosicionY == y)) return false;
            return true;
        }

        // Sobrecarga cómoda que junta las unidades y edificios de ambos jugadores.
        public bool EsCasillaLibre(int x, int y, Jugador jugadorLocal, Jugador jugadorEnemigo)
        {
            var unidades = jugadorLocal.Unidades.Concat(jugadorEnemigo.Unidades).ToList();
            var edificios = jugadorLocal.Edificios.Concat(jugadorEnemigo.Edificios).ToList();
            return EsCasillaLibre(x, y, unidades, edificios);
        }

        // [Apilamiento] Una casilla es TRANSITABLE si está en el mapa y no hay un
        // edificio encima. Las UNIDADES no bloquean: pueden apilarse en la misma
        // casilla (tropas épicas, aldeanos amontonados en el yacimiento) y así
        // nadie se queda sin camino por un amigo de paso.
        public bool EsTransitable(int x, int y, List<Edificio> edificios)
        {
            if (!EsCoordenadaValida(x, y)) return false;
            if (edificios.Any(e => e.PosicionX == x && e.PosicionY == y)) return false;
            return true;
        }

        public bool EsTransitable(int x, int y, Jugador jugadorLocal, Jugador jugadorEnemigo)
        {
            var edificios = jugadorLocal.Edificios.Concat(jugadorEnemigo.Edificios).ToList();
            return EsTransitable(x, y, edificios);
        }

        // Para construir: además de estar libre, no debe haber un yacimiento de recursos.
        public bool EsCasillaEdificable(int x, int y, Jugador jugadorLocal, Jugador jugadorEnemigo)
        {
            return EsCasillaLibre(x, y, jugadorLocal, jugadorEnemigo) && !CasillaTieneRecurso(x, y);
        }

        // Devuelve si dos entidades (unidad vs unidad, unidad vs edificio) se solapan.
        public bool SeSolapan(Unidad u, Edificio e)
        {
            return u.PosicionX == e.PosicionX && u.PosicionY == e.PosicionY;
        }
    }
}