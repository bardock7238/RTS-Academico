using UnityEngine;
using UnityEngine.UI;
using Controlador;
using Modelo;

namespace Vista
{
    // Menú de PARTIDA EN RED (2 PCs por TCP, puerto 5505): hospedar o
    // conectar con la IP del host. Al conectar el rival se cierra solo y
    // la partida arranca sin IA (dos personas, mismo mundo espejado).
    // Todo pasa por la API del Controlador; sin hilos ni sockets aquí.
    public class MenuRed : MonoBehaviour
    {
        private GestorJuego _gestor;
        private bool _construido;
        private GameObject _panel;
        private InputField _inputIp;
        private Text _txtEstado;
        private bool _esperando;

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
            _esperando = false;
            PonerEstado("Pon la IP del host o hospeda tu partida.");
        }

        public void Cerrar()
        {
            _esperando = false;
            if (_panel != null) _panel.SetActive(false);
        }

        // VOLVER: cierra y regresa al menú principal (la partida en red que
        // hubiera arrancado se limpia al elegir otro modo).
        public void Volver()
        {
            Cerrar();
            _gestor.MenuInicio?.Mostrar();
        }

        public void ElegirHospedar()
        {
            if (_gestor == null) return;
            _gestor.IniciarPartidaRed(true, null);
            _esperando = true;
            PonerEstado($"Hospedando en {ConectorRed.ObtenerIpLocal()}:5505... (pásale esa IP al rival)");
        }

        public void ElegirConectar()
        {
            if (_gestor == null) return;
            string ip = _inputIp != null ? _inputIp.text.Trim() : "";
            if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";
            PonerEstado($"Conectando a {ip}:5505...");
            _gestor.IniciarPartidaRed(false, ip);
            _esperando = true;
        }

        // Copia tu IP al portapapeles para pasarla al rival (sin dictarla).
        public void CopiarIp()
        {
            GUIUtility.systemCopyBuffer = ConectorRed.ObtenerIpLocal();
            PonerEstado($"IP copiada: {ConectorRed.ObtenerIpLocal()} (pásala al rival)");
        }

        private void Update()
        {
            if (!_esperando || _gestor == null || _gestor.Controlador == null) return;
            if (_panel == null || !_panel.activeSelf) { _esperando = false; return; }
            var ctrl = _gestor.Controlador;
            if (ctrl.RedPartida == null) return;
            if (!string.IsNullOrEmpty(ctrl.NombreRivalRed))
            {
                _esperando = false;
                Cerrar();
                _gestor.MostrarMensaje($"Rival conectado: {ctrl.NombreRivalRed} ¡A la guerra!", 5f);
                return;
            }
            if (!string.IsNullOrEmpty(ctrl.RedPartida.UltimoError))
                PonerEstado("Error de red: " + ctrl.RedPartida.UltimoError);
        }

        private void PonerEstado(string texto)
        {
            if (_txtEstado != null) _txtEstado.text = texto;
        }

        private void ConstruirSiFalta()
        {
            if (_construido) return;
            _construido = true;
            if (_panel != null) return;

            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGo = new GameObject("CanvasMenuRed", typeof(Canvas),
                    typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                canvasGo.transform.SetParent(transform, false);
                canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            _panel = UiFabrica.Panel(canvas.transform, "ModalMenuRed",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject;
            var prt = (RectTransform)_panel.transform;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            _panel.GetComponent<Image>().color = new Color(0.02f, 0.05f, 0.03f, 0.94f);

            var caja = UiFabrica.Panel(_panel.transform, "Caja",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(480, 380), Vector2.zero);
            caja.GetComponent<Image>().color = new Color(0.10f, 0.14f, 0.12f, 0.98f);

            var titulo = UiFabrica.TextoCaja(caja.transform, "Titulo", "PARTIDA EN RED",
                new Vector2(0.5f, 0.5f), new Vector2(440, 40), new Vector2(0, 140), 28);
            titulo.alignment = TextAnchor.MiddleCenter;
            titulo.color = new Color(1f, 0.85f, 0.4f, 1f);

            var subt = UiFabrica.TextoCaja(caja.transform, "Subtitulo", "2 PCs en la misma red · puerto 5505\nSi no conecta: abre el 5505 en el firewall del host",
                new Vector2(0.5f, 0.5f), new Vector2(440, 40), new Vector2(0, 104), 13);
            subt.alignment = TextAnchor.MiddleCenter;
            subt.color = new Color(0.8f, 0.85f, 0.8f, 1f);

            var lblIp = UiFabrica.TextoCaja(caja.transform, "LblIp", "IP DEL HOST",
                new Vector2(0.5f, 0.5f), new Vector2(440, 22), new Vector2(0, 66), 14);
            lblIp.alignment = TextAnchor.MiddleCenter;
            lblIp.color = new Color(0.8f, 0.85f, 0.8f, 1f);

            _inputIp = CrearCampoIp(caja.transform, new Vector2(0, 24));

            UiFabrica.Boton(caja.transform, "HOSPEDAR", ElegirHospedar,
                new Vector2(0.5f, 0.5f), new Vector2(210, 40), new Vector2(-110, -26), 16, "verde");
            UiFabrica.Boton(caja.transform, "CONECTAR", ElegirConectar,
                new Vector2(0.5f, 0.5f), new Vector2(210, 40), new Vector2(110, -26), 16, "azul");
            UiFabrica.Boton(caja.transform, "VOLVER", Volver,
                new Vector2(0.5f, 0.5f), new Vector2(210, 40), new Vector2(-110, -72), 16, "rojo");
            UiFabrica.Boton(caja.transform, "COPIAR MI IP", CopiarIp,
                new Vector2(0.5f, 0.5f), new Vector2(210, 40), new Vector2(110, -72), 16, "azul");

            _txtEstado = UiFabrica.TextoCaja(caja.transform, "Estado", "",
                new Vector2(0.5f, 0.5f), new Vector2(440, 60), new Vector2(0, -132), 13);
            _txtEstado.alignment = TextAnchor.MiddleCenter;
            _txtEstado.color = new Color(1f, 0.85f, 0.4f, 1f);
        }

        // Campo de texto para la IP (InputField clásico por código).
        private InputField CrearCampoIp(Transform padre, Vector2 pos)
        {
            var go = new GameObject("CampoIp", typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(padre, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(440, 40);
            rt.anchoredPosition = pos;
            go.GetComponent<Image>().color = new Color(0.92f, 0.94f, 0.90f, 1f);

            var input = go.GetComponent<InputField>();

            var txtGo = new GameObject("Texto", typeof(RectTransform), typeof(Text));
            txtGo.transform.SetParent(go.transform, false);
            var rtt = (RectTransform)txtGo.transform;
            rtt.anchorMin = Vector2.zero;
            rtt.anchorMax = Vector2.one;
            rtt.offsetMin = new Vector2(10, 0);
            rtt.offsetMax = new Vector2(-10, 0);
            var t = txtGo.GetComponent<Text>();
            t.font = UiFabrica.Fuente();
            t.fontSize = 16;
            t.alignment = TextAnchor.MiddleLeft;
            t.color = Color.black;
            t.text = "127.0.0.1";
            input.textComponent = t;

            var phGo = new GameObject("Marcador", typeof(RectTransform), typeof(Text));
            phGo.transform.SetParent(go.transform, false);
            var rtp = (RectTransform)phGo.transform;
            rtp.anchorMin = Vector2.zero;
            rtp.anchorMax = Vector2.one;
            rtp.offsetMin = new Vector2(10, 0);
            rtp.offsetMax = new Vector2(-10, 0);
            var ph = phGo.GetComponent<Text>();
            ph.font = UiFabrica.Fuente();
            ph.fontSize = 16;
            ph.alignment = TextAnchor.MiddleLeft;
            ph.color = new Color(0.4f, 0.4f, 0.4f, 1f);
            ph.text = "127.0.0.1";
            input.placeholder = ph;

            input.text = "127.0.0.1";
            return input;
        }
    }
}
