namespace Modelo
{
    public class Jugador
    {
        public string Nombre { get; set; }
        public int Oro { get; set; }
        public int Madera { get; set; }
        public int Comida { get; set; }
        public List<Unidad> Unidades { get; set; }
        public List<Edificio> Edificios { get; set; } // Lista de estructuras del jugador
        public Jugador(string nombre)
        {
            Nombre = nombre;
            Oro = 100;      // Recursos iniciales para empezar la partida
            Madera = 100;
            Comida = 100;
        }
    }
}