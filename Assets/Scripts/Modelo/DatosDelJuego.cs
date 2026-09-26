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
        public int CostoHierro { get; set; }
        public int CostoPiedra { get; set; }
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
        public int CostoHierro { get; set; }
        public int CostoPiedra { get; set; }
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
                        CostoHierro = 20,
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
                        CostoMadera = 20,
                        CostoOro = 40,
                        CostoComida = 50,
                        CostoHierro = 10,
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
                        CostoHierro = 50,
                        TiempoEntrenamientoSegundos = 30
                    }
                },
                {
                    // Caza mayor: vida para 2 golpes de soldado; no se entrena
                    // (ningún edificio lo lista) y no ataca (PuedeAtacar=false).
                    TipoUnidad.Ciervo,
                    new UnidadConfig
                    {
                        VidaMaxima = 30,
                        Ataque = 0,
                        Defensa = 0,
                        RangoAtaque = 1
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
                        CostoPiedra = 200,
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
                        CostoHierro = 50,
                        CostoPiedra = 100,
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
                        CostoPiedra = 250,
                        TiempoConstruccionSegundos = 25
                    }
                },
                {
                    TipoEdificio.Casa,
                    new EdificioConfig
                    {
                        VidaMaxima = 250,
                        CostoMadera = 50,
                        CostoPiedra = 20,
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
                CostoHierro = c.CostoHierro,
                CostoPiedra = c.CostoPiedra,
                TiempoEntrenamientoSegundos = c.TiempoEntrenamientoSegundos,
                EsRecolector = c.EsRecolector,
                CapacidadRecoleccion = c.CapacidadRecoleccion,
                PosicionX = x,
                PosicionY = y,
                Estado = EstadoUnidad.Idle
            };
        }

        // Crea un edificio nuevo a partir del catálogo.
        // Huella lógica por tipo: el Centro ocupa 3x3, el Cuartel 2x2.
        public static int LadoSegunTipo(TipoEdificio tipo) =>
            tipo == TipoEdificio.CentroUrbano ? 3
            : tipo == TipoEdificio.Cuartel ? 2 : 1;

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
                CostoHierro = c.CostoHierro,
                CostoPiedra = c.CostoPiedra,
                TiempoConstruccionSegundos = c.TiempoConstruccionSegundos,
                UnidadesEntrenables = new List<TipoUnidad>(c.UnidadesEntrenables),
                PosicionX = x,
                PosicionY = y,
                Lado = LadoSegunTipo(tipo),
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

        // ---- FACCIONES (aldeas enemigas con nombre) ----
        // El jugador siempre es Griegos; los rivales salen de esta lista en orden.
        public static readonly string[] FaccionesEnemigas =
        {
            "Romanos", "Persas", "Egipcios", "Cartagineses", "Babilonios"
        };

        public static string FaccionEnemiga(int indice) =>
            FaccionesEnemigas[indice % FaccionesEnemigas.Length];

        // ---- BIOMAS (zonas del mapa; la Vista los dibuja, el Modelo planta) ----
        // Zona circular de bioma en fracciones 0..1 del mapa (independiente
        // del tamaño). Tipo 1 = arena, 2 = bosque. S1/S2/S3 = semillas del
        // borde ondulado (solo armónicos pares: simetría de 180° en PVP).
        public struct ZonaBioma
        {
            public double Cx, Cy, Radio;
            public int Tipo;
            public double S1, S2, S3;
            public ZonaBioma(double cx, double cy, double r, int tipo, double s1, double s2, double s3)
            { Cx = cx; Cy = cy; Radio = r; Tipo = tipo; S1 = s1; S2 = s2; S3 = s3; }
        }

        public static ZonaBioma[] ZonasBioma() => new ZonaBioma[]
        {
            new ZonaBioma(0.22, 0.78, 0.13, 1, 1.7, 4.2, 0.6),
            new ZonaBioma(0.78, 0.22, 0.13, 1, 1.7, 4.2, 0.6),
            new ZonaBioma(0.78, 0.78, 0.11, 2, 2.9, 0.3, 5.1),
            new ZonaBioma(0.22, 0.22, 0.11, 2, 2.9, 0.3, 5.1),
        };

        // Bonus de recolección dentro de biomas (0.5 = +50% por ciclo).
        public const double BonusBioma = 0.5;

        // ¿La casilla está dentro de algún bioma (disco nominal, sin ondular)?
        public static bool EnBioma(int x, int y)
        {
            foreach (ZonaBioma z in ZonasBioma())
            {
                double cx = z.Cx * Mapa.Ancho, cy = z.Cy * Mapa.Alto, r = z.Radio * Mapa.Ancho;
                double dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy <= r * r) return true;
            }
            return false;
        }

        // Distribución inicial de recursos del mapa (oro, madera, comida, hierro y piedra).
        // Mapa 100x100 simétrico: el bloque base se espeja a los 4 cuadrantes
        // (100-1-x / 100-1-y). Sin pisar las bases (huellas en y=1..3 y y=95..97).
        public static List<Recurso> CrearRecursosIniciales()
        {
            var lista = new List<Recurso>
            {
                // Zona central (los 4 centros del mapa grande)
                new Recurso(TipoRecurso.Comida, 400, 15, 15),
                new Recurso(TipoRecurso.Oro, 500, 14, 16),
                new Recurso(TipoRecurso.Madera, 500, 16, 14),
                new Recurso(TipoRecurso.Comida, 400, 84, 15),
                new Recurso(TipoRecurso.Oro, 500, 85, 16),
                new Recurso(TipoRecurso.Madera, 500, 83, 14),
                new Recurso(TipoRecurso.Comida, 400, 15, 84),
                new Recurso(TipoRecurso.Oro, 500, 14, 83),
                new Recurso(TipoRecurso.Madera, 500, 16, 85),
                new Recurso(TipoRecurso.Comida, 400, 84, 84),
                new Recurso(TipoRecurso.Oro, 500, 85, 83),
                new Recurso(TipoRecurso.Madera, 500, 83, 85),

                // Cuadrante superior-izq
                new Recurso(TipoRecurso.Madera, 500, 5, 5),
                new Recurso(TipoRecurso.Hierro, 300, 4, 10),
                new Recurso(TipoRecurso.Piedra, 300, 10, 4),

                // Cuadrante superior-der (espejo)
                new Recurso(TipoRecurso.Madera, 500, 94, 5),
                new Recurso(TipoRecurso.Hierro, 300, 95, 10),
                new Recurso(TipoRecurso.Piedra, 300, 89, 4),
                new Recurso(TipoRecurso.Oro, 500, 75, 5),
                new Recurso(TipoRecurso.Hierro, 300, 74, 10),
                new Recurso(TipoRecurso.Piedra, 300, 80, 4),
                new Recurso(TipoRecurso.Oro, 500, 24, 5),
                new Recurso(TipoRecurso.Hierro, 300, 25, 10),
                new Recurso(TipoRecurso.Piedra, 300, 19, 4),

                // Cuadrante inferior-izq (original + espejos)
                new Recurso(TipoRecurso.Oro, 400, 5, 24),
                new Recurso(TipoRecurso.Hierro, 300, 4, 19),
                new Recurso(TipoRecurso.Piedra, 300, 10, 25),
                new Recurso(TipoRecurso.Oro, 400, 5, 75),
                new Recurso(TipoRecurso.Hierro, 300, 4, 80),
                new Recurso(TipoRecurso.Piedra, 300, 10, 74),
                new Recurso(TipoRecurso.Oro, 400, 94, 24),
                new Recurso(TipoRecurso.Hierro, 300, 95, 19),
                new Recurso(TipoRecurso.Piedra, 300, 89, 25),
                new Recurso(TipoRecurso.Oro, 400, 94, 75),
                new Recurso(TipoRecurso.Hierro, 300, 95, 80),
                new Recurso(TipoRecurso.Piedra, 300, 89, 74),
                new Recurso(TipoRecurso.Madera, 500, 5, 94),
                new Recurso(TipoRecurso.Hierro, 300, 4, 89),
                new Recurso(TipoRecurso.Piedra, 300, 10, 95),
                new Recurso(TipoRecurso.Madera, 500, 94, 94),
                new Recurso(TipoRecurso.Hierro, 300, 95, 89),
                new Recurso(TipoRecurso.Piedra, 300, 89, 95),

                // Cuadrante inferior-der (original + espejos)
                new Recurso(TipoRecurso.Madera, 400, 24, 24),
                new Recurso(TipoRecurso.Hierro, 300, 25, 19),
                new Recurso(TipoRecurso.Piedra, 300, 19, 25),
                new Recurso(TipoRecurso.Madera, 400, 75, 24),
                new Recurso(TipoRecurso.Hierro, 300, 74, 19),
                new Recurso(TipoRecurso.Piedra, 300, 80, 25),
                new Recurso(TipoRecurso.Madera, 400, 24, 75),
                new Recurso(TipoRecurso.Hierro, 300, 25, 80),
                new Recurso(TipoRecurso.Piedra, 300, 19, 74),
                new Recurso(TipoRecurso.Madera, 400, 75, 75),
                new Recurso(TipoRecurso.Hierro, 300, 74, 80),
                new Recurso(TipoRecurso.Piedra, 300, 80, 74),

                // Comida accesible para cada base
                new Recurso(TipoRecurso.Comida, 300, 10, 2),
                new Recurso(TipoRecurso.Comida, 300, 89, 2),
                new Recurso(TipoRecurso.Comida, 300, 10, 97),
                new Recurso(TipoRecurso.Comida, 300, 89, 97),
                new Recurso(TipoRecurso.Comida, 300, 20, 72),
                new Recurso(TipoRecurso.Comida, 300, 79, 72),
                new Recurso(TipoRecurso.Comida, 300, 20, 27),
                new Recurso(TipoRecurso.Comida, 300, 79, 27),
                // Comida junto a cada base (arranque rápido)
                new Recurso(TipoRecurso.Comida, 300, 44, 3),
                new Recurso(TipoRecurso.Comida, 300, 55, 96)
            };
            PlantarBosques(lista);
            PlantarDesiertos(lista);
            return lista;
        }

        // BOSQUE VIVO: arboledas densas dentro de cada zona de bosque.
        // Determinista y simétrico. La caza (ciervos) la siembra Simulacion
        // como fauna neutral (ver PuestosCaza).
        private static void PlantarBosques(List<Recurso> lista)
        {
            foreach (ZonaBioma z in ZonasBioma())
            {
                if (z.Tipo != 2) continue;
                int cx = (int)(z.Cx * Mapa.Ancho), cy = (int)(z.Cy * Mapa.Alto);
                int[][] madera =
                {
                    new int[] { -4, 0 }, new int[] { 4, 0 },
                    new int[] { 0, -4 }, new int[] { 0, 4 },
                    new int[] { -3, -3 }, new int[] { 3, 3 }
                };
                foreach (int[] o in madera)
                    if (HuecoLibre(lista, cx + o[0], cy + o[1]))
                        lista.Add(new Recurso(TipoRecurso.Madera, 500, cx + o[0], cy + o[1]));
                // Roquitas del bosque: piedra y hierro grandes entre los pinos.
                int[][] rocasPiedra =
                {
                    new int[] { -5, 2 }, new int[] { 5, -2 }
                };
                foreach (int[] o in rocasPiedra)
                    if (HuecoLibre(lista, cx + o[0], cy + o[1]))
                        lista.Add(new Recurso(TipoRecurso.Piedra, 300, cx + o[0], cy + o[1]));
                int[][] rocasHierro =
                {
                    new int[] { -1, 5 }, new int[] { 1, -5 }
                };
                foreach (int[] o in rocasHierro)
                    if (HuecoLibre(lista, cx + o[0], cy + o[1]))
                        lista.Add(new Recurso(TipoRecurso.Hierro, 300, cx + o[0], cy + o[1]));
            }
        }

        // Puestos de caza: 3 por bosque para sembrar ciervos (fauna neutral).
        public static List<(int X, int Y)> PuestosCaza()
        {
            var puestos = new List<(int X, int Y)>();
            foreach (ZonaBioma z in ZonasBioma())
            {
                if (z.Tipo != 2) continue;
                int cx = (int)(z.Cx * Mapa.Ancho), cy = (int)(z.Cy * Mapa.Alto);
                int[][] o = { new int[] { -2, 2 }, new int[] { 2, -2 }, new int[] { 3, 1 } };
                foreach (int[] d in o)
                {
                    int x = cx + d[0], y = cy + d[1];
                    if (x < 0 || y < 0 || x >= Mapa.Ancho || y >= Mapa.Alto) continue;
                    puestos.Add((x, y));
                }
            }
            return puestos;
        }

        private static bool HuecoLibre(List<Recurso> lista, int x, int y)
        {
            if (x < 0 || y < 0 || x >= Mapa.Ancho || y >= Mapa.Alto) return false;
            foreach (Recurso r in lista)
                if (r.PosicionX == x && r.PosicionY == y) return false;
            return true;
        }

        // DESIERTO RICO: menas grandes de oro y hierro en cada zona de arena.
        private static void PlantarDesiertos(List<Recurso> lista)
        {
            foreach (ZonaBioma z in ZonasBioma())
            {
                if (z.Tipo != 1) continue;
                int cx = (int)(z.Cx * Mapa.Ancho), cy = (int)(z.Cy * Mapa.Alto);
                int[][] oros =
                {
                    new int[] { -4, 0 }, new int[] { 4, 0 }
                };
                foreach (int[] o in oros)
                    if (HuecoLibre(lista, cx + o[0], cy + o[1]))
                        lista.Add(new Recurso(TipoRecurso.Oro, 500, cx + o[0], cy + o[1]));
                int[][] hierros =
                {
                    new int[] { 0, -4 }, new int[] { 0, 4 }
                };
                foreach (int[] o in hierros)
                    if (HuecoLibre(lista, cx + o[0], cy + o[1]))
                        lista.Add(new Recurso(TipoRecurso.Hierro, 300, cx + o[0], cy + o[1]));
            }
        }

        // ---- ITEMS (objetos realistas del mapa: yogur, casco, espada, herramientas) ----

        public const int CuraYogur = 50;              // Cuánta vida devuelve el Yogur
        public const int BonoDefensaCasco = 10;       // +Defensa mientras dura el Casco
        public const int BonoAtaqueEspada = 5;        // +Ataque mientras la lleve una unidad
        public const double BonusRecoleccionHerramientas = 0.05; // +5% de recurso con Herramientas

        // ---- MEJORAS (herrería: 3 niveles por rama, orden madera/oro/comida/hierro/piedra)
        public static readonly int[][,] CostosMejora =
        {
            new int[,] { { 50, 100, 0, 0, 0 }, { 100, 200, 0, 25, 0 }, { 200, 350, 0, 50, 0 } }, // 0 ataque +1
            new int[,] { { 0, 80, 0, 0, 80 }, { 0, 160, 0, 0, 160 }, { 0, 300, 0, 0, 300 } },     // 1 defensa +1
            new int[,] { { 50, 0, 80, 0, 0 }, { 100, 0, 160, 0, 0 }, { 200, 0, 300, 0, 0 } },     // 2 recolección +10%
        };
        private static readonly string[] NombresRecurso = { "madera", "oro", "comida", "hierro", "piedra" };

        // Texto del siguiente nivel ("MAX" si ya está al tope).
        public static string TextoCostoMejora(int rama, int nivel)
        {
            if (rama < 0 || rama > 2 || nivel < 0 || nivel >= 3) return "MAX";
            var partes = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 5; i++)
            {
                int c = CostosMejora[rama][nivel, i];
                if (c > 0) partes.Add(c + " " + NombresRecurso[i]);
            }
            return string.Join(" + ", partes.ToArray());
        }

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