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
        [SerializeField] private string nombreJugador = "Griegos";
        [SerializeField] private bool localArriba = true;
        [SerializeField] private bool modoPVE = true;
        [SerializeField] private bool mostrarMenuInicial = true;

        [Header("Componentes de la Vista")]
        [SerializeField] private VistaTablero vistaTablero;
        [SerializeField] private HudRecursos hudRecursos;
        [SerializeField] private ControlInputUsuario controlInput;
        [SerializeField] private PanelFinPartida panelFin;
        [SerializeField] private MenuInicio menuInicio;
        [SerializeField] private MenuRed menuRed;
        [SerializeField] private MenuMercado menuMercado;
        [SerializeField] private MenuMejoras menuMejoras;

        public JuegoControlador Controlador { get; private set; }
        public InstantaneaJuego UltimaFoto { get; private set; }
        public VistaTablero VistaTablero => vistaTablero;
        public MenuInicio MenuInicio => menuInicio;
        public MenuRed MenuRed => menuRed;
        public MenuMercado MenuMercado => menuMercado;
        public MenuMejoras MenuMejoras => menuMejoras;

        // Mensaje efímero para la barra de estado (acción aceptada/rechazada).
        public string MensajeEstado { get; private set; }
        public bool MensajeEsError { get; private set; }
        private float _mensajeHasta;

        // Estado fijo de la selección actual (vivo cada frame; no caduca).
        // La barra lo muestra cuando no hay mensaje efímero encima.
        public string EstadoSeleccion { get; set; }

        private static GestorJuego _instancia;

        // Reintentar salta el menú inicial (lo pone el botón de fin de partida).
        public static bool SaltarMenuInicial { get; set; }

        // Último escenario jugado (para Reintentar sin recargar la escena).
        private int _ultBases = 1;
        private bool _ultAdvE, _ultAdvY, _ultRico, _ultExp;
        private RitmoPartida _ultRitmo = RitmoPartida.Normal;
        private bool _ultFueRed;

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

            if (vistaTablero == null) vistaTablero = GetComponentInChildren<VistaTablero>(true);
            if (hudRecursos == null) hudRecursos = GetComponentInChildren<HudRecursos>(true);
            if (controlInput == null) controlInput = GetComponent<ControlInputUsuario>();
            if (panelFin == null) panelFin = GetComponentInChildren<PanelFinPartida>(true);
            if (menuInicio == null) menuInicio = GetComponentInChildren<MenuInicio>(true);
            if (menuRed == null) menuRed = GetComponentInChildren<MenuRed>(true);
            vistaTablero?.Inicializar(this);
            hudRecursos?.Inicializar(this);
            controlInput?.Inicializar(this);
            panelFin?.Inicializar(this);

            ConstruirUiSiFalta();

            // La partida NO arranca sola: con menú inicial, la IA espera a
            // que elijas VS MÁQUINA. Sin menú (o tras Reintentar), PVE directo.
            if (SaltarMenuInicial)
            {
                SaltarMenuInicial = false;
                if (modoPVE) Controlador.IniciarIA();
            }
            else if (menuInicio != null && mostrarMenuInicial)
                menuInicio.Mostrar();
            else if (modoPVE)
                Controlador.IniciarIA();
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
            VigilarEventosSonido();
            VigilarDesconexion();

            if (Time.unscaledTime > _mensajeHasta)
                MensajeEstado = null;
        }

        // La Vista detecta eventos comparando fotos: más bajas = golpe, más
        // tropas = entrenar, más edificios operativos = construir. La primera
        // foto de cada mundo solo sincroniza (sin sonar).
        private int _sonBajas = -1, _sonUnidades = -1, _sonEdificios = -1;

        private void VigilarEventosSonido()
        {
            if (Controlador == null || UltimaFoto == null) return;
            int bajas = Controlador.BajasLocal + Controlador.BajasEnemigo;
            int unds = UltimaFoto.UnidadesLocal.Count;
            int edifOp = 0;
            foreach (Edificio e in UltimaFoto.EdificiosLocal)
                if (e.EstaOperativo) edifOp++;
            if (_sonBajas < 0)
            {
                _sonBajas = bajas;
                _sonUnidades = unds;
                _sonEdificios = edifOp;
                return;
            }
            if (bajas > _sonBajas) SonidoJuego.Golpe();
            if (unds > _sonUnidades) SonidoJuego.Entrenar();
            if (edifOp > _sonEdificios) SonidoJuego.Construir();
            _sonBajas = bajas;
            _sonUnidades = unds;
            _sonEdificios = edifOp;
        }

        // En partida en red, si el rival se cae DE VERDAD (5 s sin tubo),
        // se cierra la partida y se vuelve al menú en vez de dejar el mundo
        // colgado. Los microcortes se perdonan (el cliente reintenta solo).
        private bool _rivalVisto;
        private float _caidaDesde = -1f;
        private const float SegundosParaMenuPorCaida = 5f;

        private void VigilarDesconexion()
        {
            var ctrl = Controlador;
            if (ctrl == null || ctrl.RedPartida == null)
            {
                _rivalVisto = false;
                _caidaDesde = -1f;
                return;
            }
            if (!string.IsNullOrEmpty(ctrl.NombreRivalRed)) _rivalVisto = true;
            if (!_rivalVisto) return; // aún no había rival: esperando conexión
            if (ctrl.RedPartida.EstaConectado) { _caidaDesde = -1f; return; }
            if (_caidaDesde < 0f) _caidaDesde = Time.unscaledTime;
            if (Time.unscaledTime - _caidaDesde < SegundosParaMenuPorCaida) return;
            // Caída sostenida: cerrar red y volver al menú principal.
            _rivalVisto = false;
            _caidaDesde = -1f;
            Controlador.Detener();
            Controlador = new JuegoControlador(nombreJugador, localArriba);
            _sonBajas = -1;
            menuInicio?.Mostrar();
            MostrarMensaje("Rival desconectado.", 5f, false);
        }

        private void OnDestroy()
        {
            if (_instancia == this)
            {
                try { Controlador?.Detener(); } catch { }
                _instancia = null;
            }
        }

        private void OnApplicationQuit()
        {
            Controlador?.Detener();
        }

        public void MostrarMensaje(string texto, float segundos = 2.5f, bool clic = true)
        {
            MensajeEstado = texto;
            MensajeEsError = false;
            _mensajeHasta = Time.unscaledTime + segundos;
            if (clic) SonidoJuego.Clic();
        }

        // Borra el mensaje efímero YA (sin esperar a que caduque): p. ej. al
        // llegar de caminar se quita "Caminando a..." y se ve el estado real.
        public void LimpiarMensaje()
        {
            MensajeEstado = null;
            MensajeEsError = false;
            _mensajeHasta = 0f;
        }

        public void AccionRechazada(string accion)
        {
            // Rechazos más visibles: más tiempo para leer el motivo.
            MostrarMensaje($"No se pudo: {accion}", 6f, false);
            MensajeEsError = true;
            SonidoJuego.Error();
        }

        // Arranca (o rearranca) la partida PVE con el escenario elegido en el
        // menú: tira el mundo anterior y crea uno nuevo con N bases enemigas
        // y avanzados o no. La Vista sigue leyendo el mismo Controlador.
        public void ReiniciarConEscenario(int basesEnemigas, bool enemigoAvanzado, bool jugadorAvanzado,
            RitmoPartida ritmo = RitmoPartida.Normal, bool inicioRico = false,
            bool exploracion = false)
        {
            _ultBases = basesEnemigas;
            _ultAdvE = enemigoAvanzado;
            _ultAdvY = jugadorAvanzado;
            _ultRitmo = ritmo;
            _ultRico = inicioRico;
            _ultExp = exploracion;
            _ultFueRed = false;
            Controlador?.Detener();
            Controlador = new JuegoControlador(nombreJugador, localArriba,
                basesEnemigas, enemigoAvanzado, jugadorAvanzado, ritmo, inicioRico, exploracion);
            Controlador.IniciarIA();
            _sonBajas = -1; // el mundo nuevo sincroniza el sonido sin sonar
            var rivales = Controlador.FaccionesRivales;
            string nombres = "";
            for (int i = 0; i < rivales.Count; i++)
                nombres += (i > 0 ? " + " : "") + rivales[i];
            MostrarMensaje(string.IsNullOrEmpty(nombres) ? "¡A la guerra!" : $"Te enfrentas a: {nombres}", 5f);
        }

        // Arranca una partida EN RED (2 PCs, sin IA): el host juega arriba y
        // el cliente abajo, con el mismo mundo espejado por TCP (puerto 5505).
        public void IniciarPartidaRed(bool esHost, string ip)
        {
            _ultFueRed = true;
            Controlador?.Detener();
            Controlador = new JuegoControlador(nombreJugador, esHost, 1, false, false);
            _sonBajas = -1; // el mundo nuevo sincroniza el sonido sin sonar
            if (esHost)
            {
                if (Controlador.HospedarRed())
                    MostrarMensaje($"Hospedando en {ConectorRed.ObtenerIpLocal()}:5505... (pásale esa IP al rival)", 8f);
                else
                    AccionRechazada("red: no se pudo hospedar (¿puerto 5505 ocupado?)");
            }
            else
            {
                if (Controlador.ConectarRed(ip))
                    MostrarMensaje($"Conectado a {ip}:5505, esperando saludo...", 8f);
                else
                    AccionRechazada($"red: no se pudo conectar a {ip}:5505");
            }
        }

        // Reintentar SIN recargar la escena (instantáneo): recrea el mundo con
        // el último escenario. Si lo último fue red, vuelve al menú de red.
        public void ReintentarUltimaPartida()
        {
            panelFin?.Ocultar();
            controlInput?.LimpiarTodo();
            hudRecursos?.Reiniciar();
            LimpiarMensaje();
            if (_ultFueRed)
            {
                menuRed?.Mostrar();
                return;
            }
            ReiniciarConEscenario(_ultBases, _ultAdvE, _ultAdvY, _ultRitmo, _ultRico, _ultExp);
        }

        // Volver al menú SIN recargar: cierra el fin, detiene el mundo y abre
        // el inicio (el próximo Jugar crea mundo nuevo).
        public void VolverAlMenu()
        {
            panelFin?.Ocultar();
            controlInput?.LimpiarTodo();
            hudRecursos?.Reiniciar();
            LimpiarMensaje();
            Controlador?.Detener();
            Controlador = new JuegoControlador(nombreJugador, localArriba);
            _sonBajas = -1;
            menuInicio?.Mostrar();
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

            if (menuInicio == null)
            {
                var inicioGo = new GameObject("MenuInicio", typeof(RectTransform));
                inicioGo.transform.SetParent(canvasGo.transform, false);
                menuInicio = inicioGo.AddComponent<MenuInicio>();
            }
            menuInicio.Inicializar(this);

            if (menuMercado == null)
            {
                var mercGo = new GameObject("MenuMercado", typeof(RectTransform));
                mercGo.transform.SetParent(canvasGo.transform, false);
                menuMercado = mercGo.AddComponent<MenuMercado>();
            }
            menuMercado.Inicializar(this);

            if (menuMejoras == null)
            {
                var mejGo = new GameObject("MenuMejoras", typeof(RectTransform));
                mejGo.transform.SetParent(canvasGo.transform, false);
                menuMejoras = mejGo.AddComponent<MenuMejoras>();
            }
            menuMejoras.Inicializar(this);

            if (menuRed == null)
            {
                var redGo = new GameObject("MenuRed", typeof(RectTransform));
                redGo.transform.SetParent(canvasGo.transform, false);
                menuRed = redGo.AddComponent<MenuRed>();
            }
            menuRed.Inicializar(this);

            var miniGo = new GameObject("Minimap", typeof(RectTransform));
            miniGo.transform.SetParent(canvasGo.transform, false);
            var minimapa = miniGo.AddComponent<Minimap>();
            minimapa.Inicializar(this, canvasGo.transform);
        }
    }
}
