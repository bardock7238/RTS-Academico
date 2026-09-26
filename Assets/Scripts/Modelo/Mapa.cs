using System.Collections.Generic;
using System.Linq;

namespace Modelo
{
    public class Mapa
    {
        // Mapa grande estilo RTS (vale cualquier tamaño: todo el código usa
        // Ancho/Alto, sin coordenadas quemadas).
        public const int Ancho = 100;
        public const int Alto = 100;

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

        // Una casilla está libre si no hay unidad sobre ella ni huella de
        // edificio (de ningún jugador) que la cubra.
        public bool EsCasillaLibre(int x, int y, List<Unidad> unidades, List<Edificio> edificios)
        {
            if (!EsCoordenadaValida(x, y)) return false;
            if (unidades.Any(u => u.PosicionX == x && u.PosicionY == y)) return false;
            if (edificios.Any(e => e.Ocupa(x, y))) return false;
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
            if (edificios.Any(e => e.Ocupa(x, y))) return false;
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

        // Un ÁREA Lado x Lado desde (x,y) está libre si todas sus casillas son
        // válidas y no hay en ellas unidades ni huellas de edificio.
        public bool EsAreaLibre(int x, int y, int lado, List<Unidad> unidades, List<Edificio> edificios)
        {
            for (int dx = 0; dx < lado; dx++)
                for (int dy = 0; dy < lado; dy++)
                {
                    int cx = x + dx, cy = y + dy;
                    if (!EsCoordenadaValida(cx, cy)) return false;
                    if (unidades.Any(u => u.PosicionX == cx && u.PosicionY == cy)) return false;
                    if (edificios.Any(e => e.Ocupa(cx, cy))) return false;
                }
            return true;
        }

        public bool EsAreaLibre(int x, int y, int lado, Jugador jugadorLocal, Jugador jugadorEnemigo)
        {
            var unidades = jugadorLocal.Unidades.Concat(jugadorEnemigo.Unidades).ToList();
            var edificios = jugadorLocal.Edificios.Concat(jugadorEnemigo.Edificios).ToList();
            return EsAreaLibre(x, y, lado, unidades, edificios);
        }

        // Para construir un edificio con huella: área libre + sin yacimientos.
        public bool EsAreaEdificable(int x, int y, int lado, Jugador jugadorLocal, Jugador jugadorEnemigo)
        {
            if (!EsAreaLibre(x, y, lado, jugadorLocal, jugadorEnemigo)) return false;
            for (int dx = 0; dx < lado; dx++)
                for (int dy = 0; dy < lado; dy++)
                    if (CasillaTieneRecurso(x + dx, y + dy)) return false;
            return true;
        }

        // Devuelve si dos entidades (unidad vs unidad, unidad vs edificio) se solapan.
        public bool SeSolapan(Unidad u, Edificio e)
        {
            return u != null && e != null && e.Ocupa(u.PosicionX, u.PosicionY);
        }
    }
}