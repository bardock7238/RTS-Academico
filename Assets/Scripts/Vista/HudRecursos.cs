using UnityEngine;
using UnityEngine.UI;
using Modelo;

namespace Vista
{
    // Barra superior: 5 recursos + tiempo + estado de partida/IA/mensajes.
    // Solo lee InstantaneaJuego y el gestor; no toca el Modelo.
    public class HudRecursos : MonoBehaviour
    {
        [SerializeField] private Text txtOro;
        [SerializeField] private Text txtMadera;
        [SerializeField] private Text txtComida;
        [SerializeField] private Text txtHierro;
        [SerializeField] private Text txtPiedra;
        [SerializeField] private Text txtTiempo;
        [SerializeField] private Text txtEstado;
        [SerializeField] private Text txtMensaje;

        private GestorJuego _gestor;
        private bool _construido;

        public void Inicializar(GestorJuego gestor)
        {
            _gestor = gestor;
            ConstruirSiFalta();
        }

        public void Actualizar(InstantaneaJuego foto, GestorJuego gestor)
        {
            if (foto == null) return;
            ConstruirSiFalta();

            if (txtOro != null) txtOro.text = $"Oro {foto.Oro}";
            if (txtMadera != null) txtMadera.text = $"Madera {foto.Madera}";
            if (txtComida != null) txtComida.text = $"Comida {foto.Comida}";
            if (txtHierro != null) txtHierro.text = $"Hierro {foto.Hierro}";
            if (txtPiedra != null) txtPiedra.text = $"Piedra {foto.Piedra}";

            if (txtTiempo != null)
            {
                int m = foto.TiempoJuegoSegundos / 60;
                int s = foto.TiempoJuegoSegundos % 60;
                txtTiempo.text = $"{m:00}:{s:00}";
            }

            if (txtEstado != null && gestor != null && gestor.Controlador != null)
            {
                bool ia = gestor.Controlador.IAActiva;
                bool red = gestor.Controlador.RedPartida != null && gestor.Controlador.RedPartida.EstaConectado;
                txtEstado.text = ia ? "Modo: VS MAQUINA" : (red ? "Modo: RED" : "Modo: LOCAL");
            }

            if (txtMensaje != null)
                txtMensaje.text = gestor != null ? gestor.MensajeEstado : null;
        }

        private void ConstruirSiFalta()
        {
            if (_construido) return;
            _construido = true;

            // Si el compañero ya montó los Text en la escena, no hacemos nada más.
            if (txtOro != null && txtMadera != null && txtComida != null &&
                txtHierro != null && txtPiedra != null && txtTiempo != null)
                return;

            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                // Sin Canvas padre (bootstrap): crear uno raíz y quedar debajo.
                var canvasGo = new GameObject("CanvasHud", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            var panel = CrearPanel("BarraRecursos", transform, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(0, 1), new Vector2(980, 48), new Vector2(8, -8));
            Fondo(panel.gameObject, new Color(0f, 0f, 0f, 0.55f));

            txtOro = CrearTexto(panel, "Oro", "Oro 0", new Vector2(0, 0.5f), new Vector2(120, 24), new Vector2(10, 6));
            txtMadera = CrearTexto(panel, "Madera", "Madera 0", new Vector2(0, 0.5f), new Vector2(110, 24), new Vector2(130, 6));
            txtComida = CrearTexto(panel, "Comida", "Comida 0", new Vector2(0, 0.5f), new Vector2(110, 24), new Vector2(250, 6));
            txtHierro = CrearTexto(panel, "Hierro", "Hierro 0", new Vector2(0, 0.5f), new Vector2(100, 24), new Vector2(370, 6));
            txtPiedra = CrearTexto(panel, "Piedra", "Piedra 0", new Vector2(0, 0.5f), new Vector2(100, 24), new Vector2(480, 6));
            txtTiempo = CrearTexto(panel, "Tiempo", "00:00", new Vector2(0, 0.5f), new Vector2(80, 24), new Vector2(600, 6));
            txtEstado = CrearTexto(panel, "Estado", "Modo: ...", new Vector2(0, 0.5f), new Vector2(200, 24), new Vector2(700, 6));
            txtMensaje = CrearTexto(panel, "Mensaje", "", new Vector2(0, 0.5f), new Vector2(960, 24), new Vector2(10, -22));
        }

        private static RectTransform CrearPanel(string nombre, Transform padre, Vector2 anclaMin, Vector2 anclaMax,
            Vector2 pivot, Vector2 tamano, Vector2 posicion)
        {
            var go = new GameObject(nombre, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(padre, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anclaMin;
            rt.anchorMax = anclaMax;
            rt.pivot = pivot;
            rt.sizeDelta = tamano;
            rt.anchoredPosition = posicion;
            return rt;
        }

        private static void Fondo(GameObject go, Color c)
        {
            var img = go.GetComponent<Image>();
            if (img == null) img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
        }

        private static Text CrearTexto(RectTransform padre, string nombre, string valor,
            Vector2 anclaIzq, Vector2 tamano, Vector2 pos)
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
            t.font = RecursoFuente();
            t.fontSize = 16;
            t.alignment = TextAnchor.MiddleLeft;
            t.color = Color.white;
            t.text = valor;
            t.raycastTarget = false;
            return t;
        }

        internal static Font RecursoFuente()
        {
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
            return f;
        }
    }
}
