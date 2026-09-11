namespace Modelo
{
    public class Edificio
    {
        public string Tipo { get; set; }        // Ej: "CentroUrbano", "Cuartel"
        public int Vida { get; set; }          // Puntos de salud del edificio
        public int PosicionX { get; set; }     // Casilla X en el mapa de 15x15
        public int PosicionY { get; set; }     // Casilla Y en el mapa de 15x15
        public int CostoMadera { get; set; }   // Costo de recolección/construcción

        public Edificio(string tipo, int vida, int x, int y, int costoMadera)
        {
            Tipo = tipo;
            Vida = vida;
            PosicionX = x;
            PosicionY = y;
            CostoMadera = costoMadera;
        }
    }
}
