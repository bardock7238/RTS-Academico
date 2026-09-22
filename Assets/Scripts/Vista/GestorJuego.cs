using UnityEngine;
using Controlador;
using Modelo;

namespace Vista
{
    // Raíz de la Vista PVE: crea el Controlador (API única), arranca la IA de la
    // máquina y en cada frame drena la red + lee UNA instantánea para el resto
    // de componentes. Cero Task/Thread/lock aquí (regla MVC de la profesora).
    public class GestorJuego : MonoBehaviour
    {
        [Header("Partida")]
        [SerializeField] private string nombreJugador = "Jugador";
        [SerializeField] private bool localArriba = true;
        [SerializeField] private bool modoPVE = true;

        [Header("Componentes de la Vista")]
        [SerializeField] private VistaTablero vistaTablero;
        [SerializeField] private HudRecursos hudRecursos;
        [SerializeField] private ControlInputUsuario controlInput;
        [SerializeField] private PanelFinPartida panelFin;

        public JuegoControlador Controlador { get; private set; }
        public InstantaneaJuego UltimaFoto { get; private set; }
        public VistaTablero VistaTablero => vistaTablero;

        // Mensaje efímero para la barra de estado (acción aceptada/rechazada).
        public string MensajeEstado { get; private set; }
        private float _mensajeHasta;

        // Estado fijo de la selección actual (vivo cada frame; no caduca).
        // La barra lo muestra cuando no hay mensaje efímero encima.
        public string EstadoSeleccion { get; set; }

        private static GestorJuego _instancia;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCrear()
        {
            if (Object.FindAnyObjectByType<GestorJuego>() != null) return;
            var go = new GameObject("GestorJuego");
            go.AddComponent<GestorJuego>();
        }

        private void Awake()
        {
            if (_instancia != null && _instancia != this)
            {
                Destroy(gameObject);
                return;
            }
            _instancia = this;

            GestorArchivos.CarpetaDestino = Application.persistentDataPath;

            Controlador = new JuegoControlador(nombreJugador, localArriba);
            if (modoPVE)
                Controlador.IniciarIA();

            if (vistaTablero == null) vistaTablero = GetComponentInChildren<VistaTablero>(true);
            if (hudRecursos == null) hudRecursos = GetComponentInChildren<HudRecursos>(true);
            if (controlInput == null) controlInput = GetComponent<ControlInputUsuario>();
            if (panelFin == null) panelFin = GetComponentInChildren<PanelFinPartida>(true);

            vistaTablero?.Inicializar(this);
            hudRecursos?.Inicializar(this);
            controlInput?.Inicializar(this);
            panelFin?.Inicializar(this);

            ConstruirUiSiFalta();
        }

        private void Update()
        {
            if (Controlador == null) return;

            // Patrón obligatorio: procesar red ANTES de la instantánea.
            Controlador.ProcesarMensajesRedPendientes();
            UltimaFoto = Controlador.Instantanea();

            vistaTablero?.Actualizar(UltimaFoto);
            hudRecursos?.Actualizar(UltimaFoto, this);
            panelFin?.Actualizar(UltimaFoto);

            if (Time.unscaledTime > _mensajeHasta)
                MensajeEstado = null;
        }

        private void OnDestroy()
        {
            if (_instancia == this)
            {
                Controlador?.Detener();
                _instancia = null;
            }
        }

        private void OnApplicationQuit()
        {
            Controlador?.Detener();
        }

        public void MostrarMensaje(string texto, float segundos = 2.5f)
        {
            MensajeEstado = texto;
            _mensajeHasta = Time.unscaledTime + segundos;
        }

        // Borra el mensaje efímero YA (sin esperar a que caduque): p. ej. al
        // llegar de caminar se quita "Caminando a..." y se ve el estado real.
        public void LimpiarMensaje()
        {
            MensajeEstado = null;
            _mensajeHasta = 0f;
        }

        public void AccionRechazada(string accion)
        {
            // Rechazos más visibles: más tiempo para leer el motivo.
            MostrarMensaje($"No se pudo: {accion}", 6f);
        }

        // Si la escena no trae HUD (bootstrap mínimo), la Vista crea su propia UI
        // por código. El compañero puede reemplazarla jerarquizando la escena (issue #4).
        private void ConstruirUiSiFalta()
        {
            // El Gestor se auto-crea con RuntimeInitializeOnLoadMethod; la UI y el
            // tablero se montan por código si la escena no los trae enlazados.
            // El compañero puede reemplazar esto jerarquizando la escena (issue #4).
            if (Camera.main == null)
            {
                var camGo = new GameObject("Camara Principal", typeof(Camera));
                camGo.tag = "MainCamera";
            }

            var cam = Camera.main;
            if (cam != null)
            {
                cam.orthographic = true;
                cam.orthographicSize = 15.5f;
                cam.transform.position = new Vector3((Mapa.Ancho - 1) / 2f, (Mapa.Alto - 1) / 2f, -10f);
                cam.backgroundColor = new Color(0.12f, 0.16f, 0.12f);
                cam.clearFlags = CameraClearFlags.SolidColor;
            }

            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }

            if (vistaTablero == null)
            {
                var tabGo = new GameObject("TableroRoot");
                tabGo.transform.SetParent(transform, false);
                vistaTablero = tabGo.AddComponent<VistaTablero>();
                vistaTablero.Inicializar(this);
            }

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            if (hudRecursos == null)
            {
                var hudGo = new GameObject("HudRecursos", typeof(RectTransform));
                hudGo.transform.SetParent(canvasGo.transform, false);
                hudRecursos = hudGo.AddComponent<HudRecursos>();
                hudRecursos.Inicializar(this);
            }

            if (controlInput == null)
            {
                controlInput = gameObject.AddComponent<ControlInputUsuario>();
                controlInput.Inicializar(this);
            }

            if (panelFin == null)
            {
                var finGo = new GameObject("PanelFinPartida", typeof(RectTransform));
                finGo.transform.SetParent(canvasGo.transform, false);
                panelFin = finGo.AddComponent<PanelFinPartida>();
                panelFin.Inicializar(this);
            }
        }
    }
}
