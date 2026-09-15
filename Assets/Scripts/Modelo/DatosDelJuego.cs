using System;
using System.Collections.Generic;

namespace Modelo
{
    // Configuración base de un tipo de unidad (usada por DatosDelJuego).
    public class UnidadConfig
    {
        public int VidaMaxima { get; set; }
        public int Ataque { get; set; }
        public int Defensa { get; set; }
        public int RangoAtaque { get; set; }
        public int CostoOro { get; set; }
        public int CostoMadera { get; set; }
        public int CostoComida { get; set; }
        public int TiempoEntrenamientoSegundos { get; set; }
        public bool EsRecolector { get; set; }
        public int CapacidadRecoleccion { get; set; }   // Cuánto recolecta por ciclo (0 = no recolecta)
    }

    // Configuración base de un tipo de edificio (usada por DatosDelJuego).
    public class EdificioConfig
    {
        public int VidaMaxima { get; set; }
        public int CostoOro { get; set; }
        public int CostoMadera { get; set; }
        public int CostoComida { get; set; }
        public int TiempoConstruccionSegundos { get; set; }
        public List<TipoUnidad> UnidadesEntrenables { get; set; } = new List<TipoUnidad>();
    }

    // Catálogo central de costos y estadísticas del juego.
    // Es la única fuente de verdad para crear unidades, edificios y recursos,
    // de modo que el Controlador valida costos aquí y la Vista conoce cada tipo.
    public static class DatosDelJuego
    {
        public static readonly Dictionary<TipoUnidad, UnidadConfig> UnidadesBase =
            new Dictionary<TipoUnidad, UnidadConfig>
            {
                {
                    TipoUnidad.Aldeano,
                    new UnidadConfig
                    {
                        VidaMaxima = 60,
                        Ataque = 3,
                        Defensa = 0,
                        RangoAtaque = 1,
                        CostoComida = 50,
                        TiempoEntrenamientoSegundos = 10,
                        EsRecolector = true,
                        CapacidadRecoleccion = 2
                    }
                },
                {
                    TipoUnidad.Soldado,
                    new UnidadConfig
                    {
                        VidaMaxima = 100,
                        Ataque = 15,
                        Defensa = 3,
                        RangoAtaque = 1,
                        CostoOro = 30,
                        CostoComida = 60,
                        TiempoEntrenamientoSegundos = 20
                    }
                },
                {
                    TipoUnidad.Arquero,
                    new UnidadConfig
                    {
                        VidaMaxima = 75,
                        Ataque = 12,
                        Defensa = 1,
                        RangoAtaque = 4,
                        CostoOro = 40,
                        CostoComida = 50,
                        TiempoEntrenamientoSegundos = 25
                    }
                },
                {
                    TipoUnidad.Caballero,
                    new UnidadConfig
                    {
                        VidaMaxima = 130,
                        Ataque = 20,
                        Defensa = 5,
                        RangoAtaque = 1,
                        CostoOro = 60,
                        CostoComida = 80,
                        TiempoEntrenamientoSegundos = 30
                    }
                }
            };

        public static readonly Dictionary<TipoEdificio, EdificioConfig> EdificiosBase =
            new Dictionary<TipoEdificio, EdificioConfig>
            {
                {
                    TipoEdificio.CentroUrbano,
                    new EdificioConfig
                    {
                        VidaMaxima = 600,
                        CostoMadera = 350,
                        TiempoConstruccionSegundos = 40,
                        UnidadesEntrenables = new List<TipoUnidad> { TipoUnidad.Aldeano }
                    }
                },
                {
                    TipoEdificio.Cuartel,
                    new EdificioConfig
                    {
                        VidaMaxima = 500,
                        CostoMadera = 200,
                        CostoOro = 50,
                        TiempoConstruccionSegundos = 30,
                        UnidadesEntrenables = new List<TipoUnidad>
                        {
                            TipoUnidad.Soldado,
                            TipoUnidad.Arquero,
                            TipoUnidad.Caballero
                        }
                    }
                },
                {
                    TipoEdificio.Torre,
                    new EdificioConfig
                    {
                        VidaMaxima = 400,
                        CostoMadera = 150,
                        CostoOro = 100,
                        TiempoConstruccionSegundos = 25
                    }
                },
                {
                    TipoEdificio.Casa,
                    new EdificioConfig
                    {
                        VidaMaxima = 250,
                        CostoMadera = 50,
                        TiempoConstruccionSegundos = 15
                    }
                }
            };

        // Crea una unidad nueva a partir del catálogo, lista para colocarse.
        public static Unidad CrearUnidad(TipoUnidad tipo, int x, int y)
        {
            UnidadConfig c = UnidadesBase[tipo];
            return new Unidad
            {
                Tipo = tipo,
                VidaMaxima = c.VidaMaxima,
                Vida = c.VidaMaxima,
                Ataque = c.Ataque,
                Defensa = c.Defensa,
                RangoAtaque = c.RangoAtaque,
                CostoOro = c.CostoOro,
                CostoMadera = c.CostoMadera,
                CostoComida = c.CostoComida,
                TiempoEntrenamientoSegundos = c.TiempoEntrenamientoSegundos,
                EsRecolector = c.EsRecolector,
                CapacidadRecoleccion = c.CapacidadRecoleccion,
                PosicionX = x,
                PosicionY = y,
                Estado = EstadoUnidad.Idle
            };
        }

        // Crea un edificio nuevo a partir del catálogo.
        public static Edificio CrearEdificio(TipoEdificio tipo, int x, int y, bool operativo = false)
        {
            EdificioConfig c = EdificiosBase[tipo];
            return new Edificio
            {
                Tipo = tipo,
                VidaMaxima = c.VidaMaxima,
                Vida = c.VidaMaxima,
                CostoOro = c.CostoOro,
                CostoMadera = c.CostoMadera,
                CostoComida = c.CostoComida,
                TiempoConstruccionSegundos = c.TiempoConstruccionSegundos,
                UnidadesEntrenables = new List<TipoUnidad>(c.UnidadesEntrenables),
                PosicionX = x,
                PosicionY = y,
                Estado = operativo ? EstadoEdificio.Operativo : EstadoEdificio.EnConstruccion
            };
        }

        // Convierte un texto ("Cuartel", "cuartel") en el enum correspondiente.
        public static TipoEdificio ObtenerTipoEdificio(string nombre)
        {
            if (Enum.TryParse(nombre, true, out TipoEdificio tipo))
                return tipo;
            return TipoEdificio.CentroUrbano;
        }

        // Lista de unidades que un edificio puede entrenar.
        public static List<TipoUnidad> UnidadesEntrenablesDe(TipoEdificio edificio)
        {
            if (EdificiosBase.TryGetValue(edificio, out EdificioConfig config))
                return new List<TipoUnidad>(config.UnidadesEntrenables);
            return new List<TipoUnidad>();
        }

        // Crea el Centro Urbano inicial ya operativo, para la fase de preparación.
        public static Edificio CrearCentroUrbano(int x, int y)
        {
            return CrearEdificio(TipoEdificio.CentroUrbano, x, y, operativo: true);
        }

        // Distribución inicial de recursos del mapa (AOE: oro, madera y comida).
        public static List<Recurso> CrearRecursosIniciales()
        {
            return new List<Recurso>
            {
                new Recurso(TipoRecurso.Madera, 500, 3, 3),
                new Recurso(TipoRecurso.Oro, 500, 11, 11),
                new Recurso(TipoRecurso.Comida, 300, 7, 7),
                new Recurso(TipoRecurso.Madera, 400, 12, 3),
                new Recurso(TipoRecurso.Oro, 400, 3, 12),
                new Recurso(TipoRecurso.Comida, 300, 11, 6)
            };
        }

        // ---- ITEMS (objetos realistas del mapa: yogur, casco, espada, herramientas) ----

        public const int CuraYogur = 50;              // Cuánta vida devuelve el Yogur
        public const int BonoDefensaCasco = 10;       // +Defensa mientras dura el Casco
        public const int BonoAtaqueEspada = 5;        // +Ataque mientras la lleva la unidad
        public const double BonusRecoleccionHerramientas = 0.05; // +5% de recurso con Herramientas

        // Nombre para logs y pantalla (el enum es técnico; esto es el "cartelito").
        public static string NombreDe(TipoItem tipo)
        {
            switch (tipo)
            {
                case TipoItem.Yogur: return "Yogur";
                case TipoItem.Casco: return "Casco";
                case TipoItem.Espada: return "Espada";
                case TipoItem.Herramientas: return "Herramientas";
                default: return tipo.ToString();
            }
        }
    }
}