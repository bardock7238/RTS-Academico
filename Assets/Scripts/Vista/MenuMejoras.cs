using UnityEngine;
using UnityEngine.UI;
using Modelo;

namespace Vista
{
    // Herrería (solo Vista + API del Controlador): 3 ramas x 3 niveles.
    // Ataque +1/tropa, defensa +1, recolección +10% por nivel. Tecla Y.
    public class MenuMejoras : MonoBehaviour
    {
        private GestorJuego _gestor;
        private bool _construido;
        private GameObject _panel;
        private readonly Button[] _btnTracks = new Button[3];
        private static readonly string[] Nombres = { "ATAQUE", "DEFENSA", "RECOLECCIÓN" };

        public bool Abierto => _panel != null && _panel.activeSelf;

        public void Inicializar(GestorJuego gestor)
        {
            _gestor = gestor;
            ConstruirSiFalta();
            Cerrar();
        }

        public void Alternar()
        {
            ConstruirSiFalta();
            if (_panel == null) return;
            if (_panel.activeSelf) Cerrar();
            else Abrir();
        }

        public void Abrir()
        {
            ConstruirSiFalta();
            Refrescar();
            if (_panel != null) _panel.SetActive(true);
        }

        public void Cerrar()
        {
            if (_panel != null) _panel.SetActive(false);
        }

        private void Comprar(int rama)
        {
            var ctrl = _gestor?.Controlador;
            if (ctrl == null) return;
            bool ok = rama == 0 ? ctrl.MejorarAtaque()
                : rama == 1 ? ctrl.MejorarDefensa() : ctrl.MejorarRecoleccion();
            if (ok) _gestor.MostrarMensaje($"{Nombres[rama]} mejorada");
            else _gestor.AccionRechazada($"mejorar {Nombres[rama]}: sin fondos o al máximo");
            Refrescar();
        }

        private void Refrescar()
        {
            var j = _gestor?.Controlador?.JugadorLocal;
            if (j == null) return;
            int[] niveles = { j.MejoraAtaque, j.MejoraDefensa, j.MejoraRecoleccion };
            for (int i = 0; i < 3; i++)
            {
                if (_btnTracks[i] == null) continue;
                var t = _btnTracks[i].transform.Find("Txt")?.GetComponent<Text>();
                if (t != null)
                    t.text = niveles[i] >= 3
                        ? $"{Nombres[i]} nv3 MAX"
                        : $"{Nombres[i]} nv{niveles[i]} → {DatosDelJuego.TextoCostoMejora(i, niveles[i])}";
            }
        }

        private void ConstruirSiFalta()
        {
            if (_construido) return;
            _construido = true;
            if (_panel != null) return;

            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGo = new GameObject("CanvasMejoras", typeof(Canvas),
                    typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                canvasGo.transform.SetParent(transform, false);
                canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            _panel = UiFabrica.Panel(canvas.transform, "ModalMejoras",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject;
            var prt = (RectTransform)_panel.transform;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            _panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.72f);

            var caja = UiFabrica.Panel(_panel.transform, "Caja",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(440, 300), Vector2.zero);
            caja.GetComponent<Image>().color = new Color(0.12f, 0.16f, 0.22f, 0.98f);

            var titulo = UiFabrica.TextoCaja(caja.transform, "Titulo", "HERRERÍA (mejoras)",
                new Vector2(0.5f, 0.5f), new Vector2(400, 36), new Vector2(0, 112), 20);
            titulo.alignment = TextAnchor.MiddleCenter;
            titulo.color = new Color(1f, 0.85f, 0.4f, 1f);

            for (int i = 0; i < 3; i++)
            {
                int k = i;
                _btnTracks[i] = UiFabrica.Boton(caja.transform, Nombres[i], () => Comprar(k),
                    new Vector2(0.5f, 0.5f), new Vector2(400, 40), new Vector2(0, 64 - i * 48), 14, "verde");
            }
            UiFabrica.Boton(caja.transform, "Cerrar", Cerrar,
                new Vector2(0.5f, 0.5f), new Vector2(200, 36), new Vector2(0, -112));
            Refrescar();
        }
    }
}
