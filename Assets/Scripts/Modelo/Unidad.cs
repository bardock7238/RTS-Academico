using System;

namespace Modelo
{
    public class Unidad
    {
        public TipoUnidad Tipo { get; set; }
        public int VidaMaxima { get; set; }
        public int Vida { get; set; }
        public int Ataque { get; set; }
        public int Defensa { get; set; }
        public int RangoAtaque { get; set; }      // 1 = cuerpo a cuerpo; mayor = a distancia
        public int PosicionX { get; set; }        // Casilla X en el mapa de 15x15
        public int PosicionY { get; set; }        // Casilla Y en el mapa de 15x15
        public int CostoOro { get; set; }
        public int CostoMadera { get; set; }
        public int CostoComida { get; set; }
        public int TiempoEntrenamientoSegundos { get; set; }
        public bool EsRecolector { get; set; }    // True solo para el Aldeano
        public int CapacidadRecoleccion { get; set; } // Cuánto recolecta por ciclo
        public EstadoUnidad Estado { get; set; }

        // [Concurrencia] Item EQUIPABLE que lleva esta unidad (Espada). Lo asigna
        // el Controlador al recogerlo y suma ataque a partir de ese momento.
        public Item Equipado { get; set; }

        public Unidad()
        {
            Estado = EstadoUnidad.Idle;
        }

        public bool EstaViva => Vida > 0;

        // El aldeano recolecta pero no combate.
        public bool PuedeAtacar => EstaViva && !EsRecolector;

        // [Concurrencia/Items] Ataque real de la unidad, contando el item equipado.
        // Ambos jugadores lo calculan igual en su copia → el daño por red coincide.
        public int AtaqueTotal =>
            Ataque + (Equipado?.Tipo == TipoItem.Espada ? DatosDelJuego.BonoAtaqueEspada : 0);

        public void RecibirDano(int cantidad)
        {
            if (Vida <= 0 || cantidad <= 0) return;
            Vida = Math.Max(0, Vida - Math.Max(0, cantidad - Defensa));
        }

        // [Concurrencia/Items] Golpe FLANO (la defensa ya se descontó al calcular el
        // daño). Así la copia del atacante y la del rival restan lo MISMO al objetivo.
        public void RecibirGolpe(int dano)
        {
            if (Vida <= 0 || dano <= 0) return;
            Vida = Math.Max(0, Vida - dano);
        }

        public void Curarse(int cantidad)
        {
            Vida = Math.Min(VidaMaxima, Vida + Math.Max(0, cantidad));
        }

        public void MoverA(int x, int y)
        {
            PosicionX = x;
            PosicionY = y;
        }
    }
}