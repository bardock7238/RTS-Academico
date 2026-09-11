using System;
using System.Collections.Generic;

namespace Modelo
{
    public class Edificio
    {
        public TipoEdificio Tipo { get; set; }
        public int VidaMaxima { get; set; }
        public int Vida { get; set; }
        public int PosicionX { get; set; }        // Casilla X en el mapa de 15x15
        public int PosicionY { get; set; }        // Casilla Y en el mapa de 15x15
        public int CostoOro { get; set; }
        public int CostoMadera { get; set; }
        public int CostoComida { get; set; }
        public int TiempoConstruccionSegundos { get; set; }
        public EstadoEdificio Estado { get; set; }
        // Unidades que este edificio puede entrenar (CentroUrbano -> Aldeano, Cuartel -> militares).
        public List<TipoUnidad> UnidadesEntrenables { get; set; } = new List<TipoUnidad>();

        public Edificio()
        {
            Estado = EstadoEdificio.EnConstruccion;
        }

        public bool EstaViva => Vida > 0;

        // Un edificio operativo está completo y puede entrenar unidades.
        public bool EstaOperativo => Estado == EstadoEdificio.Operativo && Vida > 0;

        public bool PuedeEntrenar(TipoUnidad tipoUnidad)
        {
            return EstaOperativo && UnidadesEntrenables.Contains(tipoUnidad);
        }

        public void RecibirDano(int cantidad)
        {
            if (Vida <= 0 || cantidad <= 0) return;
            Vida = Math.Max(0, Vida - Math.Max(0, cantidad - 2));
        }

        public void CompletarConstruccion()
        {
            if (Estado == EstadoEdificio.EnConstruccion)
                Estado = EstadoEdificio.Operativo;
        }

        public void Destruir()
        {
            Vida = 0;
            Estado = EstadoEdificio.Destruido;
        }
    }
}