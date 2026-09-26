using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Vista
{
    // Fábrica UI compartida: paneles, textos y botones con el mismo estilo.
    // Sustituye a los helpers duplicados que tenía cada componente de la Vista.
    // Solo crea objetos visuales; cero lógica de juego.
    public static class UiFabrica
    {
        // Cachés: la fuente y los sprites se cargan una sola vez (si no, cada
        // botón/texto los recarga y rompe el batching).
        private static Font _fuente;
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> _sprites =
            new System.Collections.Generic.Dictionary<string, Sprite>();

        public static Font Fuente()
        {
            if (_fuente != null) return _fuente;
            // Arial builtin de Unity; si falla, cualquier fuente del proyecto.
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (f == null)
            {
                try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            }
            if (f == null)
            {
                f = Font.CreateDynamicFontFromOSFont("Arial", 16);
            }
            _fuente = f;
            return _fuente;
        }

        public static Sprite SpriteUi(string estilo, bool pulsado)
        {
            string clave = estilo + (pulsado ? "_pulsado" : "");
            if (_sprites.TryGetValue(clave, out Sprite sp)) return sp;
            sp = Resources.Load<Sprite>("Sprites/UI/boton_" + clave);
            _sprites[clave] = sp;
            return sp;
        }

        public static RectTransform Panel(Transform padre, string nombre,
            Vector2 anclaMin, Vector2 anclaMax, Vector2 pivot, Vector2 tamano, Vector2 pos)
        {
            var go = new GameObject(nombre, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(padre, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anclaMin;
            rt.anchorMax = anclaMax;
            rt.pivot = pivot;
            rt.sizeDelta = tamano;
            rt.anchoredPosition = pos;
            return rt;
        }

        public static void Fondo(GameObject go, Color c)
        {
            var img = go.GetComponent<Image>();
            if (img == null) img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
        }

        // Texto anclado a la izquierda (barra de recursos).
        public static Text Texto(RectTransform padre, string nombre, string valor,
            Vector2 anclaIzq, Vector2 tamano, Vector2 pos,
            int tamanoFuente = 16, TextAnchor alineacion = TextAnchor.MiddleLeft)
        {
            var go = new GameObject(nombre, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(padre, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0, anclaIzq.y - 0.5f);
            rt.anchorMax = new Vector2(0, anclaIzq.y + 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = tamano;
            rt.anchoredPosition = pos;

            var t = go.GetComponent<Text>();
            t.font = Fuente();
            t.fontSize = tamanoFuente;
            t.alignment = alineacion;
            t.color = Color.white;
            t.text = valor;
            t.raycastTarget = false;
            return t;
        }

        // Texto en caja centrada (modales).
        public static Text TextoCaja(Transform padre, string nombre, string valor,
            Vector2 ancla, Vector2 tamano, Vector2 pos, int tamanoFuente = 16)
        {
            var go = new GameObject(nombre, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(padre, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = ancla;
            rt.anchorMax = ancla;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = tamano;
            rt.anchoredPosition = pos;
            var t = go.GetComponent<Text>();
            t.font = Fuente();
            t.fontSize = tamanoFuente;
            t.alignment = TextAnchor.MiddleLeft;
            t.color = Color.white;
            t.text = valor;
            t.raycastTarget = false;
            return t;
        }

        public static Button Boton(Transform padre, string etiqueta, UnityAction accion,
            Vector2 ancla, Vector2 tamano, Vector2 pos, int tamanoFuente = 16, string estiloUi = "azul")
        {
            var go = new GameObject("Btn_" + etiqueta, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(padre, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = ancla;
            rt.anchorMax = ancla;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = tamano;
            rt.anchoredPosition = pos;
            var img = go.GetComponent<Image>();
            var btn = go.GetComponent<Button>();
            // Botones con sprite 9-slice (Texturas/PixelUI); si falta, color plano.
            Sprite fondo = SpriteUi(estiloUi, false);
            if (fondo != null)
            {
                img.sprite = fondo;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
                btn.transition = Selectable.Transition.SpriteSwap;
                var st = btn.spriteState;
                st.pressedSprite = SpriteUi(estiloUi, true);
                btn.spriteState = st;
            }
            else
            {
                img.color = new Color(0.2f, 0.4f, 0.55f, 1f);
            }
            btn.onClick.AddListener(accion);

            var txtGo = new GameObject("Txt", typeof(RectTransform), typeof(Text));
            txtGo.transform.SetParent(go.transform, false);
            Llenar((RectTransform)txtGo.transform);
            var t = txtGo.GetComponent<Text>();
            t.font = Fuente();
            t.fontSize = tamanoFuente;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.text = etiqueta;
            t.raycastTarget = false;
            return btn;
        }

        // Icono cuadrado (p. ej. recursos del HUD). Null si falta el sprite.
        public static Image Icono(RectTransform padre, string nombre, Sprite sp, Vector2 pos, float lado = 24f)
        {
            if (sp == null) return null;
            var go = new GameObject(nombre, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(padre, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0, 0.5f);
            rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(lado, lado);
            rt.anchoredPosition = pos;
            var img = go.GetComponent<Image>();
            img.sprite = sp;
            img.color = Color.white;
            img.raycastTarget = false;
            return img;
        }

        // Rellena todo el rectángulo del padre (textos dentro de botones/inputs).
        public static void Llenar(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // Botón de estructura/unidad estilo RTS: icono a la izquierda, nombre
        // arriba y detalle (costo o pista) abajo. Sin icono queda centrado.
        public static Button BotonEstructura(Transform padre, string nombre, string detalle,
            UnityAction accion, Vector2 ancla, Vector2 tamano, Vector2 pos, Sprite icono,
            int tamanoNombre = 13, int tamanoDetalle = 11, string estiloUi = "azul")
        {
            var go = new GameObject("Btn_" + nombre, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(padre, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = ancla;
            rt.anchorMax = ancla;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = tamano;
            rt.anchoredPosition = pos;
            var img = go.GetComponent<Image>();
            var btn = go.GetComponent<Button>();
            Sprite fondo = SpriteUi(estiloUi, false);
            if (fondo != null)
            {
                img.sprite = fondo;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
                btn.transition = Selectable.Transition.SpriteSwap;
                var st = btn.spriteState;
                st.pressedSprite = SpriteUi(estiloUi, true);
                btn.spriteState = st;
            }
            else
            {
                img.color = new Color(0.2f, 0.4f, 0.55f, 1f);
            }
            btn.onClick.AddListener(accion);

            float textoX = 8f;
            TextAnchor alin = TextAnchor.MiddleCenter;
            if (icono != null)
            {
                var iconGo = new GameObject("Icono", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(go.transform, false);
                var irt = (RectTransform)iconGo.transform;
                irt.anchorMin = new Vector2(0, 0.5f);
                irt.anchorMax = new Vector2(0, 0.5f);
                irt.pivot = new Vector2(0, 0.5f);
                irt.sizeDelta = new Vector2(36, 36);
                irt.anchoredPosition = new Vector2(4, 0);
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.sprite = icono;
            iconImg.color = Color.white;
            iconImg.raycastTarget = false;
            iconImg.preserveAspect = true;
                textoX = 44f;
                alin = TextAnchor.MiddleLeft;
            }

            var nomGo = new GameObject("Nombre", typeof(RectTransform), typeof(Text));
            nomGo.transform.SetParent(go.transform, false);
            var nrt = (RectTransform)nomGo.transform;
            nrt.anchorMin = new Vector2(0, 1f);
            nrt.anchorMax = new Vector2(1f, 1f);
            nrt.pivot = new Vector2(0, 1f);
            nrt.offsetMin = new Vector2(textoX, -22f);
            nrt.offsetMax = new Vector2(-4f, -2f);
            var nom = nomGo.GetComponent<Text>();
            nom.font = Fuente();
            nom.fontSize = tamanoNombre;
            nom.alignment = alin;
            nom.color = Color.white;
            nom.text = nombre;
            nom.raycastTarget = false;

            var detGo = new GameObject("Detalle", typeof(RectTransform), typeof(Text));
            detGo.transform.SetParent(go.transform, false);
            var drt = (RectTransform)detGo.transform;
            drt.anchorMin = new Vector2(0, 0f);
            drt.anchorMax = new Vector2(1f, 0f);
            drt.pivot = new Vector2(0, 0f);
            drt.offsetMin = new Vector2(textoX, 2f);
            drt.offsetMax = new Vector2(-4f, 20f);
            var det = detGo.GetComponent<Text>();
            det.font = Fuente();
            det.fontSize = tamanoDetalle;
            det.alignment = alin;
            det.color = new Color(1f, 0.85f, 0.4f, 1f);
            det.text = detalle;
            det.raycastTarget = false;
            return btn;
        }
    }
}
