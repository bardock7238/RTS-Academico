using System;
using System.Collections.Generic;
using UnityEngine;
using Modelo;

namespace Vista
{
    // Carga automática de los sprites descargados (Assets/Resources/Sprites).
    // El orden de cada lista es el orden de su enum en Modelo/Tipos.cs.
    // Si falta algún archivo, devuelve null y VistaTablero usa el
    // SpriteFactory temporal. Lo enlazado en el Inspector sigue mandando.
    // (public para que las pruebas del Editor puedan verificar la carga.)
    public static class ArteRecursos
    {
        // TipoEdificio: CentroUrbano, Cuartel, Torre, Casa.
        private static readonly string[] Edificios =
            { "centro_urbano", "cuartel", "torre", "casa" };

        // TipoRecurso: Madera, Oro, Comida, Hierro, Piedra.
        private static readonly string[] Recursos =
            { "madera", "oro", "comida", "hierro", "piedra" };

        public static Sprite[] CargarEdificios() => Cargar("Sprites/Edificios", Edificios);
        public static Sprite[] CargarRecursos() => Cargar("Sprites/Recursos", Recursos);
        public static Sprite CargarTile() => Resources.Load<Sprite>("Sprites/Terreno/cesped");
        // Variante con flores para salpicar el césped (puede faltar: se usa la base).
        public static Sprite CargarTileAlt() => Resources.Load<Sprite>("Sprites/Terreno/cesped_alt");
        // Icono individual de recurso para el HUD (misma carpeta que el mapa).
        public static Sprite CargarSpriteRecurso(TipoRecurso t) =>
            Resources.Load<Sprite>("Sprites/Recursos/" + Recursos[(int)t]);
        // Ciervo de caza (Comida de alto rendimiento en bosques).
        public static Sprite CargarSpriteCiervo() =>
            Resources.Load<Sprite>("Sprites/Recursos/ciervo");
        // Agua del río decorativo.
        public static Sprite CargarSpriteAgua() =>
            Resources.Load<Sprite>("Sprites/Terreno/agua");

        // BIOMAS visuales (solo Vista, deterministas): pesos 0..1 de arena y
        // bosque por casilla (0 = pradera pura). Manchas orgánicas con borde
        // suave; la geometría sale de DatosDelJuego.ZonasBioma (fuente única).
        public static void PesoBiomas(int x, int y, out float arena, out float bosque)
        {
            arena = 0f;
            bosque = 0f;
            int ancho = Mapa.Ancho, alto = Mapa.Alto;
            foreach (DatosDelJuego.ZonaBioma z in DatosDelJuego.ZonasBioma())
            {
                float w = PesoDisco(x, y, z.Cx * ancho, z.Cy * alto, z.Radio * ancho, z.S1, z.S2, z.S3);
                if (z.Tipo == 1 && w > arena) arena = w;
                else if (z.Tipo == 2 && w > bosque) bosque = w;
            }
        }

        private static float PesoDisco(int x, int y, double cx, double cy, double r,
            double s1, double s2, double s3)
        {
            double dx = x - cx, dy = y - cy;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            double ang = Math.Atan2(dy, dx);
            double borde = r * (0.80 + 0.15 * Math.Sin(2 * ang + s1)
                              + 0.08 * Math.Sin(4 * ang + s2) + 0.05 * Math.Cos(2 * ang + s3));
            double d = (dist - borde) / 3.0;
            if (d <= -1) return 1f;
            if (d >= 1) return 0f;
            return (float)(0.5 + 0.5 * Math.Cos((d + 1) * Math.PI / 2.0));
        }

        // Río decorativo (solo Vista): serpentea vertical; no bloquea ni da nada.
        public static bool EsRio(int x, int y)
        {
            int ancho = Mapa.Ancho, alto = Mapa.Alto;
            double centro = ancho * 0.30 + System.Math.Sin(y * 0.18) * ancho * 0.10
                          + System.Math.Sin(y * 0.05 + 1.3) * ancho * 0.06;
            return System.Math.Abs(x - centro) <= 1.0 && y >= 0 && y < alto;
        }

        private static Sprite[] Cargar(string carpeta, string[] nombres)
        {
            var arr = new Sprite[nombres.Length];
            for (int i = 0; i < nombres.Length; i++)
            {
                arr[i] = Resources.Load<Sprite>(carpeta + "/" + nombres[i]);
                if (arr[i] == null) return null;
            }
            return arr;
        }

        // Color propio de cada facción (tropas, edificios y minimapa).
        public static Color ColorFaccion(string faccion)
        {
            switch (faccion)
            {
                case "Romanos": return new Color(0.9f, 0.2f, 0.2f);
                case "Persas": return new Color(0.7f, 0.3f, 0.9f);
                case "Egipcios": return new Color(0.95f, 0.75f, 0.2f);
                case "Cartagineses": return new Color(0.95f, 0.45f, 0.15f);
                case "Vikingos": return new Color(0.3f, 0.7f, 0.8f);
                default: return new Color(1f, 0.35f, 0.3f);
            }
        }

        // Facción del centro enemigo más cercano a una tropa (para pintarla
        // de su color). Null si no hay centros con facción.
        public static string FaccionDeTropa(Unidad u, List<Edificio> centrosEnemigos)
        {
            if (u == null || centrosEnemigos == null) return null;
            string mejor = null;
            int mejorD = int.MaxValue;
            foreach (Edificio e in centrosEnemigos)
            {
                if (e == null || e.Tipo != TipoEdificio.CentroUrbano || !e.EstaViva) continue;
                if (string.IsNullOrEmpty(e.Faccion)) continue;
                int d = System.Math.Abs(u.PosicionX - e.PosicionX) + System.Math.Abs(u.PosicionY - e.PosicionY);
                if (d < mejorD) { mejorD = d; mejor = e.Faccion; }
            }
            return mejor;
        }
    }
}
