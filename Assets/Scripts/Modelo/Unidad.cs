namespace Modelo
{
    public class Unidad
    {
        public string Tipo { get; set; }
        public int Vida { get; set; }
        public int Ataque { get; set; }
        public int Defensa { get; set; }
        public int PosicionX { get; set; }     // Casilla X en el mapa de 15x15
        public int PosicionY { get; set; }     // Casilla Y en el mapa de 15x15

        public Unidad(string tipo, int vida, int ataque, int defensa)
        {
            Tipo = tipo;
            Vida = vida;
            Ataque = ataque;
            Defensa = defensa;
        }
    }
}