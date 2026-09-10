namespace Modelo
{
    public class Unidad
    {
        public string Tipo { get; set; }
        public int Vida { get; set; }
        public int Ataque { get; set; }
        public int Defensa { get; set; }

        public Unidad(string tipo, int vida, int ataque, int defensa)
        {
            Tipo = tipo;
            Vida = vida;
            Ataque = ataque;
            Defensa = defensa;
        }
    }
}