using UnityEngine;
using UnityEngine.UI;
using Modelo;

namespace Vista
{
    // Menú INICIAL (antes de jugar): título + elegir modo + ayuda + salir.
    // La partida NO arranca sola: la IA solo se enciende con "VS MÁQUINA".
    // Todo pasa por la API del Controlador; la Vista no toca hilos ni sockets.
    public class MenuInicio : MonoBehaviour
    {
        private GestorJuego _gestor;
        private bool _construido;
        private GameObject _panel;
        private GameObject _ayudaGo;
        private Text _txtAyuda;

        // Escenario elegido: nº de bases enemigas (1..5), si arrancan
        // avanzadas (Cuartel + soldados) ellas o también tú, ritmo de
        // partida (Rápida/Normal/Larga) e inicio rico (+recursos y tropas).
        private int _bases = 1;
        private bool _advE, _advY, _rico;
        private RitmoPartida _ritmo = RitmoPartida.Normal;
        private readonly Button[] _btnBases = new Button[5];
        private readonly Button[] _btnRitmo = new Button[3];
        private Button _btnAdvE, _btnAdvY, _btnRico;
        private Text _txtRivales;

        public bool Abierto => _panel != null && _panel.activeSelf;

        public void Inicializar(GestorJuego gestor)
        {
            _gestor = gestor;
            ConstruirSiFalta();
            Cerrar();
        }

        public void Mostrar()
        {
            ConstruirSiFalta();
            if (_panel != null) _panel.SetActive(true);
            MostrarAyuda(false);
        }

        public void Cerrar()
        {
            if (_panel != null) _panel.SetActive(false);
        }

        public void ElegirPve()
        {
            // Arranque clásico: 1 base básica para todos, ritmo normal.
            _bases = 1;
            _advE = false;
            _advY = false;
            _rico = false;
            _ritmo = RitmoPartida.Normal;
            ActualizarOpciones();
            Jugar();
        }

        // Arranca la partida con el escenario configurado (recrea el mundo).
        public void Jugar()
        {
            if (_gestor == null) return;
            _gestor.ReiniciarConEscenario(_bases, _advE, _advY, _ritmo, _rico);
            Cerrar();
        }

        public void ElegirBases(int n)
        {
            _bases = Mathf.Clamp(n, 1, 5);
            ActualizarOpciones();
        }

        public void AlternarAdvE()
        {
            _advE = !_advE;
            ActualizarOpciones();
        }

        public void AlternarAdvY()
        {
            _advY = !_advY;
            ActualizarOpciones();
        }

        public void ElegirRitmo(int r)
        {
            _ritmo = (RitmoPartida)Mathf.Clamp(r, 0, 2);
            ActualizarOpciones();
        }

        public void AlternarRico()
        {
            _rico = !_rico;
            ActualizarOpciones();
        }

        private void ActualizarOpciones()
        {
            for (int i = 0; i < _btnBases.Length; i++)
            {
                if (_btnBases[i] == null) continue;
                var img = _btnBases[i].GetComponent<Image>();
                if (img != null) img.color = (i + 1 == _bases) ? Color.white : new Color(0.55f, 0.55f, 0.55f);
            }
            for (int i = 0; i < _btnRitmo.Length; i++)
            {
                if (_btnRitmo[i] == null) continue;
                var img = _btnRitmo[i].GetComponent<Image>();
                if (img != null) img.color = (i == (int)_ritmo) ? Color.white : new Color(0.55f, 0.55f, 0.55f);
            }
            PonerTexto(_btnAdvE, _advE ? "ENEMIGO AVANZADO: SÍ" : "ENEMIGO AVANZADO: NO");
            PonerTexto(_btnAdvY, _advY ? "YO AVANZADO: SÍ" : "YO AVANZADO: NO");
            PonerTexto(_btnRico, _rico ? "INICIO RICO: SÍ" : "INICIO RICO: NO");
            if (_txtRivales != null)
            {
                var nombres = new System.Collections.Generic.List<string>();
                for (int i = 0; i < _bases; i++)
                    nombres.Add(DatosDelJuego.FaccionEnemiga(i));
                _txtRivales.text = "RIVALES: " + string.Join(" + ", nombres).ToUpperInvariant();
            }
        }

        private static void PonerTexto(Button btn, string texto)
        {
            if (btn == null) return;
            var t = btn.transform.Find("Txt")?.GetComponent<Text>();
            if (t != null) t.text = texto;
        }

        public void ElegirSalir() => SalirDelJuego();

        public static void SalirDelJuego()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void AlternarAyuda() => MostrarAyuda(_ayudaGo == null || !_ayudaGo.activeSelf);

        private void MostrarAyuda(bool visible)
        {
            if (_ayudaGo != null) _ayudaGo.SetActive(visible);
        }

        private void ConstruirSiFalta()
        {
            if (_construido) return;
            _construido = true;
            if (_panel != null) return;

            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGo = new GameObject("CanvasMenuInicio", typeof(Canvas),
                    typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                canvasGo.transform.SetParent(transform, false);
                canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            _panel = UiFabrica.Panel(canvas.transform, "ModalMenuInicio",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject;
            var prt = (RectTransform)_panel.transform;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            _panel.GetComponent<Image>().color = new Color(0.02f, 0.05f, 0.03f, 0.94f);

            var caja = UiFabrica.Panel(_panel.transform, "Caja",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(480, 730), Vector2.zero);
            caja.GetComponent<Image>().color = new Color(0.10f, 0.14f, 0.12f, 0.98f);

            var titulo = UiFabrica.TextoCaja(caja.transform, "Titulo", "IMPERIOS EN GUERRA",
                new Vector2(0.5f, 0.5f), new Vector2(440, 40), new Vector2(0, 330), 30);
            titulo.alignment = TextAnchor.MiddleCenter;
            titulo.color = new Color(1f, 0.85f, 0.4f, 1f);

            var subt = UiFabrica.TextoCaja(caja.transform, "Subtitulo", "Estrategia en tiempo real contra la máquina",
                new Vector2(0.5f, 0.5f), new Vector2(440, 24), new Vector2(0, 300), 14);
            subt.alignment = TextAnchor.MiddleCenter;
            subt.color = new Color(0.8f, 0.85f, 0.8f, 1f);

            var lblBases = UiFabrica.TextoCaja(caja.transform, "LblBases", "BASES ENEMIGAS",
                new Vector2(0.5f, 0.5f), new Vector2(440, 22), new Vector2(0, 272), 14);
            lblBases.alignment = TextAnchor.MiddleCenter;
            lblBases.color = new Color(0.8f, 0.85f, 0.8f, 1f);

            for (int n = 1; n <= 5; n++)
            {
                int k = n;
                _btnBases[n - 1] = UiFabrica.Boton(caja.transform, k.ToString(), () => ElegirBases(k),
                    new Vector2(0.5f, 0.5f), new Vector2(64, 34), new Vector2(-128 + (k - 1) * 64, 238));
            }
            _txtRivales = UiFabrica.TextoCaja(caja.transform, "Rivales", "",
                new Vector2(0.5f, 0.5f), new Vector2(440, 22), new Vector2(0, 208), 12);
            _txtRivales.alignment = TextAnchor.MiddleCenter;
            _txtRivales.color = new Color(1f, 0.85f, 0.4f, 1f);

            var lblRitmo = UiFabrica.TextoCaja(caja.transform, "LblRitmo", "RITMO DE PARTIDA",
                new Vector2(0.5f, 0.5f), new Vector2(440, 22), new Vector2(0, 178), 14);
            lblRitmo.alignment = TextAnchor.MiddleCenter;
            lblRitmo.color = new Color(0.8f, 0.85f, 0.8f, 1f);
            string[] ritmos = { "RÁPIDA", "NORMAL", "LARGA" };
            for (int r = 0; r < 3; r++)
            {
                int k = r;
                _btnRitmo[r] = UiFabrica.Boton(caja.transform, ritmos[r], () => ElegirRitmo(k),
                    new Vector2(0.5f, 0.5f), new Vector2(110, 34), new Vector2(-120 + k * 120, 148), 14);
            }

            _btnAdvE = UiFabrica.Boton(caja.transform, "ENEMIGO AVANZADO: NO", AlternarAdvE,
                new Vector2(0.5f, 0.5f), new Vector2(210, 34), new Vector2(-110, 110), 14);
            _btnAdvY = UiFabrica.Boton(caja.transform, "YO AVANZADO: NO", AlternarAdvY,
                new Vector2(0.5f, 0.5f), new Vector2(210, 34), new Vector2(110, 110), 14);
            _btnRico = UiFabrica.Boton(caja.transform, "INICIO RICO: NO", AlternarRico,
                new Vector2(0.5f, 0.5f), new Vector2(260, 34), new Vector2(0, 72), 14);
            ActualizarOpciones();

            UiFabrica.Boton(caja.transform, "¡JUGAR!", Jugar,
                new Vector2(0.5f, 0.5f), new Vector2(360, 44), new Vector2(0, 22), 18, "verde");
            UiFabrica.Boton(caja.transform, "COMO JUGAR", AlternarAyuda,
                new Vector2(0.5f, 0.5f), new Vector2(360, 44), new Vector2(0, -28));
            UiFabrica.Boton(caja.transform, "SALIR", ElegirSalir,
                new Vector2(0.5f, 0.5f), new Vector2(360, 44), new Vector2(0, -78), 16, "rojo");

            _ayudaGo = new GameObject("Ayuda", typeof(RectTransform));
            _ayudaGo.transform.SetParent(caja.transform, false);
            var art = (RectTransform)_ayudaGo.transform;
            art.anchorMin = new Vector2(0.5f, 0.5f);
            art.anchorMax = new Vector2(0.5f, 0.5f);
            art.pivot = new Vector2(0.5f, 0.5f);
            art.sizeDelta = new Vector2(440, 110);
            art.anchoredPosition = new Vector2(0, -155);
            _txtAyuda = UiFabrica.TextoCaja(_ayudaGo.transform, "TxtAyuda",
                "Izq: seleccionar/ordenar · Der: añadir a grupo · Arrastrar: varias · Minimapa: clic mueve cámara\n" +
                "QWER entrenar · 1-4 construir (Esc cancela) · C recoger (x2=cercano) · I item · T mercado · Y mejoras\n" +
                "Clic en ciervo: cazarlo con aldeanos o tropas (+100 comida) · Alt+QWER: seleccionar tipo\n" +
                "Flechas/rueda/central: cámara · M: vista completa · Esc: deseleccionar · Menú: volver al inicio\n" +
                "S: sonido sí/no · Ctrl+5-9: guardar grupo · 5-9: llamar grupo",
                new Vector2(0.5f, 0.5f), new Vector2(440, 110), Vector2.zero, 11);
            _txtAyuda.alignment = TextAnchor.MiddleCenter;
            _txtAyuda.color = new Color(0.85f, 0.9f, 0.85f, 1f);
            _ayudaGo.SetActive(false);
        }
    }
}
