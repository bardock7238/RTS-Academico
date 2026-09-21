using UnityEngine;
using UnityEngine.UI;
using Modelo;

namespace Vista
{
    // Modal de fin de partida: se muestra cuando InstantaneaJuego.GanadorNombre != null.
    public class PanelFinPartida : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text txtGanador;
        [SerializeField] private Button btnReintentar;

        private GestorJuego _gestor;
        private bool _construido;
        private bool _mostrado;

        public void Inicializar(GestorJuego gestor)
        {
            _gestor = gestor;
            ConstruirSiFalta();
            if (panel != null) panel.SetActive(false);
            if (btnReintentar != null)
            {
                btnReintentar.onClick.RemoveListener(Reiniciar);
                btnReintentar.onClick.AddListener(Reiniciar);
            }
        }

        public void Actualizar(InstantaneaJuego foto)
        {
            if (foto == null || foto.GanadorNombre == null) return;
            ConstruirSiFalta();
            if (_mostrado || panel == null) return;

            _mostrado = true;
            panel.SetActive(true);
            if (txtGanador != null)
            {
                bool ganoLocal = _gestor != null && _gestor.Controlador != null &&
                                 foto.GanadorNombre == _gestor.Controlador.JugadorLocal.Nombre;
                txtGanador.text = ganoLocal
                    ? $"¡VICTORIA de {foto.GanadorNombre}!"
                    : $"Derrota — gana {foto.GanadorNombre}";
            }
        }

        private void Reiniciar()
        {
            // OnDestroy del GestorJuego llama Detener(); aquí solo recargamos
            // para que AutoCrear() arme una partida nueva limpia.
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        private void ConstruirSiFalta()
        {
            if (_construido) return;
            _construido = true;
            if (panel != null) return;

            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGo = new GameObject("CanvasFin", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                canvasGo.transform.SetParent(transform, false);
                canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            panel = new GameObject("ModalFin", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)panel.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.7f);

            var caja = new GameObject("Caja", typeof(RectTransform), typeof(Image));
            caja.transform.SetParent(panel.transform, false);
            var crt = (RectTransform)caja.transform;
            crt.anchorMin = new Vector2(0.5f, 0.5f);
            crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(480, 200);
            caja.GetComponent<Image>().color = new Color(0.12f, 0.16f, 0.22f, 0.98f);

            var txtGo = new GameObject("Ganador", typeof(RectTransform), typeof(Text));
            txtGo.transform.SetParent(caja.transform, false);
            var trt = (RectTransform)txtGo.transform;
            trt.anchorMin = new Vector2(0, 0.5f);
            trt.anchorMax = new Vector2(1, 1);
            trt.offsetMin = new Vector2(16, 0);
            trt.offsetMax = new Vector2(-16, -16);
            txtGanador = txtGo.GetComponent<Text>();
            txtGanador.font = HudRecursos.RecursoFuente();
            txtGanador.fontSize = 28;
            txtGanador.alignment = TextAnchor.MiddleCenter;
            txtGanador.color = Color.white;
            txtGanador.text = "Fin de la partida";

            var btnGo = new GameObject("Reintentar", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(caja.transform, false);
            var brt = (RectTransform)btnGo.transform;
            brt.anchorMin = new Vector2(0.5f, 0);
            brt.anchorMax = new Vector2(0.5f, 0);
            brt.pivot = new Vector2(0.5f, 0);
            brt.sizeDelta = new Vector2(180, 44);
            brt.anchoredPosition = new Vector2(0, 18);
            btnGo.GetComponent<Image>().color = new Color(0.2f, 0.45f, 0.3f, 1f);
            btnReintentar = btnGo.GetComponent<Button>();

            var bTxtGo = new GameObject("Txt", typeof(RectTransform), typeof(Text));
            bTxtGo.transform.SetParent(btnGo.transform, false);
            var btrt = (RectTransform)bTxtGo.transform;
            btrt.anchorMin = Vector2.zero;
            btrt.anchorMax = Vector2.one;
            btrt.offsetMin = Vector2.zero;
            btrt.offsetMax = Vector2.zero;
            var bt = bTxtGo.GetComponent<Text>();
            bt.font = HudRecursos.RecursoFuente();
            bt.fontSize = 18;
            bt.alignment = TextAnchor.MiddleCenter;
            bt.color = Color.white;
            bt.text = "Reintentar";
            bt.raycastTarget = false;
        }
    }
}
