using UnityEngine;

namespace Vista
{
    //  SPRITE FACTORY — arte temporal POR CODIGO (issue #5)
    //  ----------------------------------------------------------------------------
    //  Genera pixel-art 16x16 en memoria mientras el companion no tenga sprites
    //  finales en Assets/Sprites. Si la escena enlaza sprites en el Inspector,
    //  VistaTablero los respeta y estos no se usan.
    //
    //  Diseno pensado para el tinte de VistaTablero (el color multiplica al sprite):
    //   · Unidades/edificios: base CLARA + contorno oscuro -> el tinte de equipo
    //     (azul local / rojo enemigo) colorea la silueta sin ensuciarla.
    //   · Recursos: base BLANCA -> el color del recurso (verde/amarillo/...) pinta la forma.
    //   · Items: arte a COLOR completo (el tinte es blanco).
    internal static class SpriteFactory
    {
        private static readonly Color OUT = new Color(0.10f, 0.10f, 0.12f);
        private static readonly Color CLARO = new Color(0.93f, 0.93f, 0.95f);
        private static readonly Color CLARO2 = new Color(0.80f, 0.80f, 0.85f);
        private static readonly Color PIEL = new Color(1.00f, 0.87f, 0.72f);
        private static readonly Color MADERA = new Color(0.55f, 0.38f, 0.22f);
        private static readonly Color ACERO = new Color(0.72f, 0.76f, 0.84f);
        private static readonly Color HOJA = new Color(0.35f, 0.68f, 0.32f);
        private static readonly Color ROJO = new Color(0.85f, 0.28f, 0.24f);
        private static readonly Color PAJA = new Color(0.95f, 0.82f, 0.45f);
        private static readonly Color PINA = new Color(0.45f, 0.75f, 0.40f);

        // ---------------------------------------------------------------- API --

        public static Sprite[] Unidades() => new[]
        {
            Aldeano(), Soldado(), Arquero(), Caballero()
        };

        public static Sprite[] Edificios() => new[]
        {
            CentroUrbano(), Cuartel(), Torre(), Casa()
        };

        public static Sprite[] Recursos() => new[]
        {
            MaderaSp(), OroSp(), ComidaSp(), HierroSp(), PiedraSp()
        };

        public static Sprite[] Items() => new[]
        {
            Yogur(), Casco(), Espada(), Herramientas()
        };

        public static Sprite Tile()
        {
            Texture2D t = Nueva();
            Relleno(t, Color.white);
            Anillo(t, 0, 0, 16, 16, new Color(0.86f, 0.86f, 0.86f));
            Anillo(t, 1, 1, 14, 14, new Color(0.94f, 0.94f, 0.94f));
            return ASprite(t);
        }

        // ------------------------------------------------------------ unidades --

        private static Sprite Aldeano()
        {
            Texture2D t = Nueva();
            Persona(t, CLARO);
            // Sombrero de paja.
            R(t, 2, 13, 12, 1, PAJA);
            R(t, 4, 14, 8, 2, PAJA);
            Anillo(t, 4, 14, 8, 2, OUT);
            return ASprite(t);
        }

        private static Sprite Soldado()
        {
            Texture2D t = Nueva();
            Persona(t, CLARO);
            // Casco + broquel.
            R(t, 4, 13, 8, 3, ACERO);
            Anillo(t, 4, 13, 8, 3, OUT);
            R(t, 4, 12, 8, 1, OUT);
            R(t, 1, 4, 3, 6, ACERO);
            Anillo(t, 1, 4, 3, 6, OUT);
            P(t, 2, 6, ROJO);
            P(t, 2, 7, ROJO);
            return ASprite(t);
        }

        private static Sprite Arquero()
        {
            Texture2D t = Nueva();
            Persona(t, CLARO);
            // Capucha verde.
            R(t, 4, 13, 8, 3, PINA);
            Anillo(t, 4, 13, 8, 3, OUT);
            P(t, 7, 15, PINA);
            P(t, 8, 15, PINA);
            // Arco a la derecha.
            for (int y = 3; y <= 12; y++) P(t, 14, y, MADERA);
            P(t, 13, 3, MADERA);
            P(t, 13, 12, MADERA);
            for (int y = 4; y <= 11; y++) P(t, 13, y, OUT);
            return ASprite(t);
        }

        private static Sprite Caballero()
        {
            Texture2D t = Nueva();
            Persona(t, CLARO);
            // Harnadura completa + penacho y hombreras.
            R(t, 4, 10, 8, 6, ACERO);
            Anillo(t, 4, 10, 8, 6, OUT);
            R(t, 5, 12, 6, 1, OUT);
            R(t, 7, 15, 2, 1, ROJO);
            R(t, 1, 8, 3, 2, ACERO);
            R(t, 12, 8, 3, 2, ACERO);
            Anillo(t, 1, 8, 3, 2, OUT);
            Anillo(t, 12, 8, 3, 2, OUT);
            return ASprite(t);
        }

        // Persona base: piernas, cuerpo, brazos, cabeza y ojos (16x16, y=0 abajo).
        private static void Persona(Texture2D t, Color cuerpo)
        {
            Color pantalon = new Color(0.28f, 0.28f, 0.36f);
            R(t, 5, 0, 2, 3, pantalon);
            R(t, 9, 0, 2, 3, pantalon);
            R(t, 4, 3, 8, 6, cuerpo);
            Anillo(t, 4, 3, 8, 6, OUT);
            R(t, 2, 4, 2, 5, cuerpo);
            R(t, 12, 4, 2, 5, cuerpo);
            P(t, 2, 8, OUT); P(t, 13, 8, OUT);
            R(t, 5, 9, 6, 5, PIEL);
            Anillo(t, 5, 9, 6, 5, OUT);
            P(t, 6, 11, OUT);
            P(t, 9, 11, OUT);
        }

        // ---------------------------------------------------------- edificios --

        private static Sprite CentroUrbano()
        {
            Texture2D t = Nueva();
            R(t, 1, 1, 14, 9, CLARO);
            Anillo(t, 1, 1, 14, 9, OUT);
            // Almenas.
            R(t, 1, 10, 3, 2, CLARO); Anillo(t, 1, 10, 3, 2, OUT);
            R(t, 6, 10, 4, 2, CLARO); Anillo(t, 6, 10, 4, 2, OUT);
            R(t, 12, 10, 3, 2, CLARO); Anillo(t, 12, 10, 3, 2, OUT);
            // Puerta y ventanas.
            R(t, 6, 1, 4, 5, MADERA);
            Anillo(t, 6, 1, 4, 5, OUT);
            R(t, 3, 5, 2, 2, new Color(0.45f, 0.65f, 0.9f));
            R(t, 11, 5, 2, 2, new Color(0.45f, 0.65f, 0.9f));
            // Bandera.
            for (int y = 12; y <= 15; y++) P(t, 8, y, OUT);
            R(t, 9, 14, 4, 2, ROJO);
            Anillo(t, 9, 14, 4, 2, OUT);
            return ASprite(t);
        }

        private static Sprite Casa()
        {
            Texture2D t = Nueva();
            R(t, 2, 1, 12, 7, CLARO);
            Anillo(t, 2, 1, 12, 7, OUT);
            // Tejado a dos aguas.
            R(t, 1, 8, 14, 2, ROJO);
            R(t, 3, 10, 10, 2, ROJO);
            R(t, 5, 12, 6, 2, ROJO);
            R(t, 7, 14, 2, 1, ROJO);
            Anillo(t, 1, 8, 14, 8, OUT);
            // Puerta y ventana.
            R(t, 6, 1, 4, 5, MADERA);
            Anillo(t, 6, 1, 4, 5, OUT);
            R(t, 3, 4, 2, 2, new Color(0.45f, 0.65f, 0.9f));
            return ASprite(t);
        }

        private static Sprite Cuartel()
        {
            Texture2D t = Nueva();
            R(t, 1, 1, 14, 10, CLARO2);
            Anillo(t, 1, 1, 14, 10, OUT);
            R(t, 1, 11, 14, 2, new Color(0.40f, 0.45f, 0.55f));
            Anillo(t, 1, 11, 14, 2, OUT);
            // Escudo emblema.
            R(t, 6, 5, 4, 5, ACERO);
            Anillo(t, 6, 5, 4, 5, OUT);
            R(t, 7, 7, 2, 3, ROJO);
            // Puerta doble.
            R(t, 3, 1, 3, 5, MADERA);
            R(t, 10, 1, 3, 5, MADERA);
            Anillo(t, 3, 1, 3, 5, OUT);
            Anillo(t, 10, 1, 3, 5, OUT);
            return ASprite(t);
        }

        private static Sprite Torre()
        {
            Texture2D t = Nueva();
            R(t, 4, 1, 8, 11, CLARO);
            Anillo(t, 4, 1, 8, 11, OUT);
            // Almenas de torre.
            R(t, 3, 12, 3, 3, CLARO); Anillo(t, 3, 12, 3, 3, OUT);
            R(t, 7, 12, 2, 3, CLARO); Anillo(t, 7, 12, 2, 3, OUT);
            R(t, 10, 12, 3, 3, CLARO); Anillo(t, 10, 12, 3, 3, OUT);
            // Saetera y puerta.
            R(t, 7, 7, 2, 3, OUT);
            R(t, 6, 1, 4, 4, MADERA);
            Anillo(t, 6, 1, 4, 4, OUT);
            return ASprite(t);
        }

        // ---------------------------------------------------------- recursos --
        // Base BLANCA: el tinte del recurso (VistaTablero) tiende la forma.

        private static Sprite MaderaSp()
        {
            Texture2D t = Nueva();
            // Tronco.
            R(t, 7, 0, 3, 6, MADERA);
            Anillo(t, 7, 0, 3, 6, OUT);
            // Copa.
            Disco(t, 8, 10, 5.4f, OUT);
            Disco(t, 8, 10, 4.5f, Color.white);
            Disco(t, 7, 11, 2.6f, new Color(0.97f, 0.97f, 0.97f));
            return ASprite(t);
        }

        private static Sprite OroSp()
        {
            Texture2D t = Nueva();
            Disco(t, 5, 4, 3.4f, OUT);
            Disco(t, 10, 4, 3.4f, OUT);
            Disco(t, 7.5f, 9, 3.8f, OUT);
            Disco(t, 5, 4, 2.5f, Color.white);
            Disco(t, 10, 4, 2.5f, Color.white);
            Disco(t, 7.5f, 9, 2.9f, Color.white);
            P(t, 7, 10, Color.white);
            P(t, 4, 5, Color.white);
            return ASprite(t);
        }

        private static Sprite ComidaSp()
        {
            Texture2D t = Nueva();
            // Fruta (manzana) con hoja.
            Disco(t, 8, 5, 5.2f, OUT);
            Disco(t, 8, 5, 4.3f, Color.white);
            Disco(t, 8, 4, 2.8f, new Color(0.96f, 0.96f, 0.96f));
            for (int y = 10; y <= 13; y++) P(t, 8, y, MADERA);
            R(t, 9, 12, 3, 2, HOJA);
            Anillo(t, 9, 12, 3, 2, OUT);
            return ASprite(t);
        }

        private static Sprite HierroSp()
        {
            Texture2D t = Nueva();
            // Dos lingotes apilados.
            R(t, 2, 1, 12, 4, Color.white);
            Anillo(t, 2, 1, 12, 4, OUT);
            R(t, 4, 6, 9, 4, new Color(0.94f, 0.94f, 0.96f));
            Anillo(t, 4, 6, 9, 4, OUT);
            R(t, 3, 2, 4, 1, new Color(0.88f, 0.88f, 0.90f));
            R(t, 5, 7, 4, 1, new Color(0.88f, 0.88f, 0.90f));
            return ASprite(t);
        }

        private static Sprite PiedraSp()
        {
            Texture2D t = Nueva();
            // Roca irregular.
            R(t, 4, 1, 9, 2, Color.white);
            R(t, 2, 3, 13, 3, Color.white);
            R(t, 3, 6, 11, 3, Color.white);
            R(t, 5, 9, 7, 2, new Color(0.96f, 0.96f, 0.96f));
            Anillo(t, 2, 1, 13, 10, OUT);
            R(t, 4, 1, 9, 1, new Color(0.82f, 0.82f, 0.84f));
            P(t, 6, 5, new Color(0.78f, 0.78f, 0.80f));
            P(t, 9, 7, new Color(0.78f, 0.78f, 0.80f));
            return ASprite(t);
        }

        // -------------------------------------------------------------- items --
        // Arte a COLOR (VistaTablero los pinta con tinte blanco).

        private static Sprite Yogur()
        {
            Texture2D t = Nueva();
            // Vaso conico.
            R(t, 5, 1, 6, 1, OUT);
            R(t, 5, 2, 6, 4, Color.white);
            Anillo(t, 5, 1, 6, 5, OUT);
            R(t, 5, 2, 6, 1, new Color(0.55f, 0.80f, 0.95f));
            R(t, 5, 4, 6, 2, new Color(0.98f, 0.92f, 0.96f));
            // Arandanos encima.
            P(t, 6, 6, new Color(0.45f, 0.35f, 0.75f));
            P(t, 8, 6, new Color(0.45f, 0.35f, 0.75f));
            P(t, 10, 6, new Color(0.45f, 0.35f, 0.75f));
            P(t, 7, 7, new Color(0.45f, 0.35f, 0.75f));
            P(t, 9, 7, new Color(0.45f, 0.35f, 0.75f));
            // Cuchara.
            for (int y = 6; y <= 13; y++) P(t, 13, y, new Color(0.85f, 0.87f, 0.90f));
            P(t, 12, 13, new Color(0.85f, 0.87f, 0.90f));
            P(t, 13, 14, new Color(0.85f, 0.87f, 0.90f));
            return ASprite(t);
        }

        private static Sprite Casco()
        {
            Texture2D t = Nueva();
            // Cupula.
            R(t, 3, 5, 10, 5, ACERO);
            R(t, 4, 10, 8, 2, ACERO);
            R(t, 5, 12, 6, 1, ACERO);
            R(t, 6, 13, 4, 1, ACERO);
            Anillo(t, 3, 5, 10, 9, OUT);
            // Rebaba + agallas (guardias faciales).
            R(t, 2, 4, 12, 1, OUT);
            R(t, 3, 1, 3, 4, ACERO);
            R(t, 10, 1, 3, 4, ACERO);
            Anillo(t, 3, 1, 3, 4, OUT);
            Anillo(t, 10, 1, 3, 4, OUT);
            // Visera.
            R(t, 6, 6, 4, 2, OUT);
            P(t, 4, 7, OUT);
            P(t, 11, 7, OUT);
            // Remache.
            P(t, 8, 11, Color.white);
            return ASprite(t);
        }

        private static Sprite Espada()
        {
            Texture2D t = Nueva();
            // Hoja diagonal (de (4,3) a (12,11)), con filo claro y sombra.
            for (int i = 0; i <= 8; i++)
            {
                P(t, 3 + i, 3 + i, OUT);
                P(t, 4 + i, 3 + i, Color.white);
                P(t, 5 + i, 3 + i, new Color(0.78f, 0.80f, 0.85f));
                P(t, 6 + i, 3 + i, OUT);
            }
            P(t, 13, 12, Color.white);
            P(t, 12, 12, OUT);
            P(t, 13, 11, OUT);
            // Guardamanos.
            for (int i = 0; i <= 4; i++) P(t, 2 + i, 7 - i, MADERA);
            P(t, 1, 7, OUT); P(t, 6, 3, OUT);
            P(t, 2, 6, OUT); P(t, 5, 2, OUT);
            // Mando y pomo.
            P(t, 1, 8, MADERA); P(t, 0, 9, MADERA);
            P(t, 1, 9, OUT); P(t, 0, 10, new Color(0.85f, 0.70f, 0.30f));
            P(t, 0, 8, OUT);
            return ASprite(t);
        }

        private static Sprite Herramientas()
        {
            Texture2D t = Nueva();
            // Martillo (cabeza arriba-izquierda, mango diagonal).
            R(t, 2, 10, 7, 4, ACERO);
            Anillo(t, 2, 10, 7, 4, OUT);
            for (int i = 0; i <= 6; i++) P(t, 3 + i, 3 + i, MADERA);
            for (int i = 0; i <= 6; i++) P(t, 4 + i, 3 + i, new Color(0.42f, 0.28f, 0.16f));
            P(t, 2, 4, OUT); P(t, 9, 4, OUT);
            // Llave inglesa a la derecha.
            R(t, 11, 1, 3, 10, new Color(0.82f, 0.84f, 0.88f));
            Anillo(t, 11, 1, 3, 10, OUT);
            R(t, 10, 11, 5, 4, new Color(0.82f, 0.84f, 0.88f));
            Anillo(t, 10, 11, 5, 4, OUT);
            R(t, 12, 13, 1, 2, Color.clear); // mordaza
            P(t, 12, 14, Color.clear);
            P(t, 11, 15, Color.clear);
            P(t, 13, 15, Color.clear);
            P(t, 12, 15, Color.clear);
            return ASprite(t);
        }

        // ------------------------------------------------------------ helpers --

        private static Texture2D Nueva()
        {
            var t = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            t.filterMode = FilterMode.Point;
            t.wrapMode = TextureWrapMode.Clamp;
            var limpio = new Color[16 * 16];
            t.SetPixels(limpio);
            return t;
        }

        private static Sprite ASprite(Texture2D t)
        {
            t.Apply();
            // PPU = 16 -> el sprite mide 1 unidad de mundo (= 1 casilla a escala 1).
            return Sprite.Create(t, new Rect(0, 0, t.width, t.height),
                new Vector2(0.5f, 0.5f), t.width);
        }

        private static void P(Texture2D t, int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= 16 || y >= 16) return;
            t.SetPixel(x, y, c);
        }

        private static void R(Texture2D t, int x, int y, int w, int h, Color c)
        {
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                    P(t, x + i, y + j, c);
        }

        private static void Anillo(Texture2D t, int x, int y, int w, int h, Color c)
        {
            for (int i = 0; i < w; i++) { P(t, x + i, y, c); P(t, x + i, y + h - 1, c); }
            for (int j = 0; j < h; j++) { P(t, x, y + j, c); P(t, x + w - 1, y + j, c); }
        }

        private static void Disco(Texture2D t, float cx, float cy, float r, Color c)
        {
            int x0 = Mathf.FloorToInt(cx - r - 1), x1 = Mathf.CeilToInt(cx + r + 1);
            int y0 = Mathf.FloorToInt(cy - r - 1), y1 = Mathf.CeilToInt(cy + r + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    if (dx * dx + dy * dy <= r * r) P(t, x, y, c);
                }
        }

        private static void Relleno(Texture2D t, Color c)
        {
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                    P(t, x, y, c);
        }
    }
}
