using System.Collections.Generic;
using System.Linq;

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

        // ---- Efectos de items ----

        public int DefensaBonus { get; set; }         // Temporal (Casco): lo quita un Task de expiración.
        public double BonusRecoleccion { get; set; }  // Pasivo (Herramientas): +fracción por ciclo.
        public int ItemsRecogidos { get; set; }       // Contador para el informe / logs.

        public Jugador(string nombre)
        {
            Nombre = nombre;
            Oro = 100;      // Recursos iniciales para empezar la partida
            Madera = 100;
            Comida = 100;
            Unidades = new List<Unidad>();
            Edificios = new List<Edificio>();
        }

        // ---- Gestión de recursos ----

        public bool PuedePagar(int madera, int oro, int comida)
        {
            return Madera >= madera && Oro >= oro && Comida >= comida;
        }

        // Descuenta los recursos si hay saldo suficiente; si no, no modifica nada.
        public bool Gastar(int madera, int oro, int comida)
        {
            if (!PuedePagar(madera, oro, comida)) return false;
            Madera -= madera;
            Oro -= oro;
            Comida -= comida;
            return true;
        }

        public void Recibir(int madera, int oro, int comida)
        {
            Madera += madera;
            Oro += oro;
            Comida += comida;
        }

        // Vista amigable de los recursos (también usa Dictionary<string,int>).
        public Dictionary<string, int> ObtenerRecursosComoDiccionario()
        {
            return new Dictionary<string, int>
            {
                { "Oro", Oro },
                { "Madera", Madera },
                { "Comida", Comida }
            };
        }

        // ---- Gestión de unidades y edificios ----

        public void AgregarUnidad(Unidad unidad) => Unidades.Add(unidad);

        public bool EliminarUnidad(Unidad unidad) => Unidades.Remove(unidad);

        public void AgregarEdificio(Edificio edificio) => Edificios.Add(edificio);

        public bool EliminarEdificio(Edificio edificio) => Edificios.Remove(edificio);

        // ---- Consultas de estado ----

        // Condición de derrota: sin Centro Urbano operativo o sin unidades.
        public bool EstaDerrotado => !TieneCentroUrbanoOperativo || Unidades.Count == 0;

        public bool TieneCentroUrbanoOperativo =>
            Edificios.Any(e => e.Tipo == TipoEdificio.CentroUrbano && e.EstaOperativo);

        public List<Unidad> ObtenerUnidadesMilitares() =>
            Unidades.Where(u => !u.EsRecolector && u.EstaViva).ToList();

        public List<Unidad> ObtenerRecolectores() =>
            Unidades.Where(u => u.EsRecolector && u.EstaViva).ToList();

        // Todas las casillas ocupadas por este jugador (para validar solapes).
        public List<(int X, int Y)> ObtenerCasillasOcupadas()
        {
            List<(int, int)> casillas = new List<(int, int)>();
            casillas.AddRange(Unidades.Select(u => (u.PosicionX, u.PosicionY)));
            casillas.AddRange(Edificios.Select(e => (e.PosicionX, e.PosicionY)));
            return casillas;
        }
    }
}