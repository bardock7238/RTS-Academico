using System;

namespace Modelo
{
    public class Recurso
    {
        public TipoRecurso Tipo { get; set; }      // "Oro", "Madera", "Comida"
        public int Cantidad { get; set; }          // Cantidad restante para recolectar
        public int CantidadMaxima { get; set; }    // Cantidad inicial (para la barra de progreso de la Vista)
        public int PosicionX { get; set; }
        public int PosicionY { get; set; }

        public Recurso(TipoRecurso tipo, int cantidad, int x, int y)
        {
            Tipo = tipo;
            Cantidad = cantidad;
            CantidadMaxima = cantidad;
            PosicionX = x;
            PosicionY = y;
        }

        public bool EstaAgotado => Cantidad <= 0;

        // Retira hasta 'cantidadSolicitada' del recurso y devuelve lo extraído.
        // El Task de recolección del Modelo llama a este método dentro de un lock.
        public int Extraer(int cantidadSolicitada)
        {
            if (Cantidad <= 0 || cantidadSolicitada <= 0) return 0;
            int extraida = Math.Min(Cantidad, cantidadSolicitada);
            Cantidad -= extraida;
            return extraida;
        }

        public float ProgresoDisponible =>
            CantidadMaxima > 0 ? (float)Cantidad / CantidadMaxima : 0f;
    }
}