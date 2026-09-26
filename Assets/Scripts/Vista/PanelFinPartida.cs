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
        [SerializeField] private Text txtStats;
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
                if (ganoLocal) SonidoJuego.Victoria();
                else SonidoJuego.Derrota();
            }
            if (txtStats != null)
            {
                int m = foto.TiempoJuegoSegundos / 60;
                int s = foto.TiempoJuegoSegundos % 60;
                int bajasL = _gestor != null && _gestor.Controlador != null
                    ? _gestor.Controlador.BajasLocal : 0;
                int bajasE = _gestor != null && _gestor.Controlador != null
                    ? _gestor.Controlador.BajasEnemigo : 0;
                txtStats.text = $"Duración {m:00}:{s:00} · Bajas tuyas {bajasL} · enemigas {bajasE}" +
                    (string.IsNullOrEmpty(foto.MotivoVictoria) ? "" : $"\n{foto.MotivoVictoria}");
            }
        }

        private void Reiniciar()
        {
            // Reintentar salta el menú inicial (vuelve directo a la partida).
            GestorJuego.SaltarMenuInicial = true;
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

            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGo = new GameObject("CanvasFin", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                canvasGo.transform.SetParent(transform, false);
                canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            panel = UiFabrica.Panel(canvas.transform, "ModalFin",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject;
            // Panel a pantalla completa: offsets a cero.
            var prt = (RectTransform)panel.transform;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.7f);

            var caja = UiFabrica.Panel(panel.transform, "Caja",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(480, 260), Vector2.zero);
            caja.GetComponent<Image>().color = new Color(0.12f, 0.16f, 0.22f, 0.98f);

            txtGanador = UiFabrica.TextoCaja(caja.transform, "Ganador", "Fin de la partida",
                new Vector2(0.5f, 0.5f), new Vector2(448, 70), new Vector2(0, 80));
            txtGanador.fontSize = 28;
            txtGanador.alignment = TextAnchor.MiddleCenter;

            txtStats = UiFabrica.TextoCaja(caja.transform, "Stats", "",
                new Vector2(0.5f, 0.5f), new Vector2(448, 54), new Vector2(0, 8), 15);
            txtStats.alignment = TextAnchor.MiddleCenter;
            txtStats.color = new Color(0.85f, 0.9f, 1f, 1f);

            btnReintentar = UiFabrica.Boton(caja.transform, "Reintentar", Reiniciar,
                new Vector2(0.5f, 0.5f), new Vector2(160, 44), new Vector2(-110, -80), 18, "verde");
            UiFabrica.Boton(caja.transform, "Menú", VolverAlMenu,
                new Vector2(0.5f, 0.5f), new Vector2(140, 44), new Vector2(50, -80), 16, "azul");
            UiFabrica.Boton(caja.transform, "Salir", MenuInicio.SalirDelJuego,
                new Vector2(0.5f, 0.5f), new Vector2(100, 44), new Vector2(170, -80), 16, "rojo");
        }

        private void VolverAlMenu()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }
    }
}
