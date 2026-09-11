namespace Modelo
{
    public class Recurso
    {
        public string Tipo { get; set; }       // "Oro", "Madera", "Comida"
        public int Cantidad { get; set; }     // Cantidad disponible para recolectar
        public int PosicionX { get; set; }
        public int PosicionY { get; set; }

        public Recurso(string tipo, int cantidad, int x, int y)
        {
            Tipo = tipo;
            Cantidad = cantidad;
            PosicionX = x;
            PosicionY = y;
        }
    }
}