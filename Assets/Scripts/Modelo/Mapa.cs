using System.Collections.Generic;

namespace Modelo
{
    public class Mapa
    {
        public const int Ancho = 15;
        public const int Alto = 15;

        // Lista de recursos colocados en casillas del mapa
        public List<Recurso> RecursosEnMapa { get; set; }

        public Mapa()
        {
            RecursosEnMapa = new List<Recurso>();
            InicializarRecursosBasicos();
        }

        private void InicializarRecursosBasicos()
        {
            // Ubicación inicial de recursos dentro de la matriz
            RecursosEnMapa.Add(new Recurso("Madera", 500, 3, 3));
            RecursosEnMapa.Add(new Recurso("Oro", 500, 11, 11));
            RecursosEnMapa.Add(new Recurso("Comida", 300, 7, 7));
        }

        public bool EsCoordenadaValida(int x, int y)
        {
            return x >= 0 && x < Ancho && y >= 0 && y < Alto;
        }
    }
}