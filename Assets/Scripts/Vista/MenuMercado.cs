using UnityEngine;
using UnityEngine.UI;
using Modelo;

namespace Vista
{
    // Mercado de trueque (solo Vista + API del Controlador): vende 100 de un
    // recurso por 60 de oro, o compra 100 por 75 de oro. Tecla T.
    public class MenuMercado : MonoBehaviour
    {
        private GestorJuego _gestor;
        private bool _construido;
        private GameObject _panel;

        private static readonly TipoRecurso[] Recursos =
            { TipoRecurso.Madera, TipoRecurso.Comida, TipoRecurso.Hierro, TipoRecurso.Piedra };

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
            if (_panel != null) _panel.SetActive(true);
        }

        public void Cerrar()
        {
            if (_panel != null) _panel.SetActive(false);
        }

        private void Vender(TipoRecurso t)
        {
            var ctrl = _gestor?.Controlador;
            if (ctrl == null) return;
            if (ctrl.VenderRecurso(t)) _gestor.MostrarMensaje($"Vendidos 100 de {t} (+60 oro)");
            else _gestor.AccionRechazada($"vender {t}: necesitas 100");
        }

        private void Comprar(TipoRecurso t)
        {
            var ctrl = _gestor?.Controlador;
            if (ctrl == null) return;
            if (ctrl.ComprarRecurso(t)) _gestor.MostrarMensaje($"Comprados 100 de {t} (-75 oro)");
            else _gestor.AccionRechazada($"comprar {t}: necesitas 75 de oro");
        }

        private void ConstruirSiFalta()
        {
            if (_construido) return;
            _construido = true;
            if (_panel != null) return;

            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGo = new GameObject("CanvasMercado", typeof(Canvas),
                    typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                canvasGo.transform.SetParent(transform, false);
                canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            _panel = UiFabrica.Panel(canvas.transform, "ModalMercado",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject;
            var prt = (RectTransform)_panel.transform;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            _panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.72f);

            var caja = UiFabrica.Panel(_panel.transform, "Caja",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(440, 470), Vector2.zero);
            caja.GetComponent<Image>().color = new Color(0.12f, 0.16f, 0.22f, 0.98f);

            var titulo = UiFabrica.TextoCaja(caja.transform, "Titulo", "MERCADO (trueque con tasa)",
                new Vector2(0.5f, 0.5f), new Vector2(400, 36), new Vector2(0, 198), 20);
            titulo.alignment = TextAnchor.MiddleCenter;
            titulo.color = new Color(1f, 0.85f, 0.4f, 1f);

            for (int i = 0; i < Recursos.Length; i++)
            {
                TipoRecurso t = Recursos[i];
                float y = 148 - i * 88;
                UiFabrica.Boton(caja.transform, $"Vender 100 {t} (+60 oro)", () => Vender(t),
                    new Vector2(0.5f, 0.5f), new Vector2(400, 36), new Vector2(0, y + 22), 14);
                UiFabrica.Boton(caja.transform, $"Comprar 100 {t} (-75 oro)", () => Comprar(t),
                    new Vector2(0.5f, 0.5f), new Vector2(400, 36), new Vector2(0, y - 22), 14, "verde");
            }
            UiFabrica.Boton(caja.transform, "Cerrar", Cerrar,
                new Vector2(0.5f, 0.5f), new Vector2(200, 36), new Vector2(0, -204));
        }
    }
}
