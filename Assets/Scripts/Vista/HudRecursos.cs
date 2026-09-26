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

        // Antivalores: solo se reescribe el texto que cambió (si no, cada
        // asignación reconstruye el Canvas).
        private int _oro = int.MinValue, _madera = int.MinValue, _comida = int.MinValue;
        private int _hierro = int.MinValue, _piedra = int.MinValue, _tiempo = int.MinValue;
        private string _estado, _mensaje;
        private Color _colorMensaje = Color.white;
        private Button _btnSonido;
        private bool _sonidoSi = true;

        public void Inicializar(GestorJuego gestor)
        {
            _gestor = gestor;
            ConstruirSiFalta();
        }

        public void Actualizar(InstantaneaJuego foto, GestorJuego gestor)
        {
            if (foto == null) return;
            ConstruirSiFalta();
            RefrescarBotonSonido(); // por si se silenció con la tecla S

            if (txtOro != null && foto.Oro != _oro) { _oro = foto.Oro; txtOro.text = $"Oro {foto.Oro}"; }
            if (txtMadera != null && foto.Madera != _madera) { _madera = foto.Madera; txtMadera.text = $"Madera {foto.Madera}"; }
            if (txtComida != null && foto.Comida != _comida) { _comida = foto.Comida; txtComida.text = $"Comida {foto.Comida}"; }
            if (txtHierro != null && foto.Hierro != _hierro) { _hierro = foto.Hierro; txtHierro.text = $"Hierro {foto.Hierro}"; }
            if (txtPiedra != null && foto.Piedra != _piedra) { _piedra = foto.Piedra; txtPiedra.text = $"Piedra {foto.Piedra}"; }

            if (txtTiempo != null && foto.TiempoJuegoSegundos != _tiempo)
            {
                _tiempo = foto.TiempoJuegoSegundos;
                txtTiempo.text = $"{_tiempo / 60:00}:{_tiempo % 60:00}";
            }

            if (txtEstado != null && gestor != null && gestor.Controlador != null)
            {
                bool ia = gestor.Controlador.IAActiva;
                string estado = ia ? "Modo: VS MAQUINA" : "Modo: LOCAL";
                if (estado != _estado) { _estado = estado; txtEstado.text = estado; }
            }

            if (txtMensaje != null)
            {
                // Mensaje efímero (2.5s) manda; si no hay, se ve el estado de
                // la selección (Recolectando, Moviendo...) hasta que cambie.
                // Los errores van en rojo para distinguirlos del estado normal.
                string m = gestor != null ? gestor.MensajeEstado : null;
                if (string.IsNullOrEmpty(m) && gestor != null) m = gestor.EstadoSeleccion;
                bool esError = gestor != null && gestor.MensajeEsError && !string.IsNullOrEmpty(gestor.MensajeEstado);
                Color cm = esError ? new Color(1f, 0.42f, 0.38f) : Color.white;
                if (m != _mensaje) { _mensaje = m; txtMensaje.text = m ?? ""; }
                if (txtMensaje.color != cm) txtMensaje.color = cm;
            }
        }

        private void ConstruirSiFalta()
        {
            if (_construido) return;
            _construido = true;

            // El objeto HUD se crea por código con un RectTransform de tamaño
            // cero (cae al centro de la pantalla): estirarlo a todo el Canvas
            // para que la barra ancle de verdad al borde SUPERIOR.
            var propia = (RectTransform)transform;
            propia.anchorMin = Vector2.zero;
            propia.anchorMax = Vector2.one;
            propia.offsetMin = Vector2.zero;
            propia.offsetMax = Vector2.zero;

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

            // Barra centrada arriba (ancho fijo, no a todo lo ancho).
            var panel = UiFabrica.Panel(transform, "BarraRecursos",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(1000, 48), new Vector2(0, -8));
            UiFabrica.Fondo(panel.gameObject, new Color(0f, 0f, 0f, 0.55f));

            txtOro = UiFabrica.Texto(panel, "Oro", "Oro 0", new Vector2(0, 0.5f), new Vector2(100, 24), new Vector2(38, 6));
            txtMadera = UiFabrica.Texto(panel, "Madera", "Madera 0", new Vector2(0, 0.5f), new Vector2(100, 24), new Vector2(170, 6));
            txtComida = UiFabrica.Texto(panel, "Comida", "Comida 0", new Vector2(0, 0.5f), new Vector2(100, 24), new Vector2(302, 6));
            txtHierro = UiFabrica.Texto(panel, "Hierro", "Hierro 0", new Vector2(0, 0.5f), new Vector2(90, 24), new Vector2(432, 6));
            txtPiedra = UiFabrica.Texto(panel, "Piedra", "Piedra 0", new Vector2(0, 0.5f), new Vector2(90, 24), new Vector2(552, 6));
            txtTiempo = UiFabrica.Texto(panel, "Tiempo", "00:00", new Vector2(0, 0.5f), new Vector2(80, 24), new Vector2(650, 6));
            txtEstado = UiFabrica.Texto(panel, "Estado", "Modo: ...", new Vector2(0, 0.5f), new Vector2(220, 24), new Vector2(740, 6));
            txtMensaje = UiFabrica.Texto(panel, "Mensaje", "", new Vector2(0, 0.5f), new Vector2(960, 24), new Vector2(10, -22));

            // Iconos de recurso (misma carpeta que los del mapa; si faltan, solo texto).
            UiFabrica.Icono(panel, "IconoOro", ArteRecursos.CargarSpriteRecurso(TipoRecurso.Oro), new Vector2(10, 6));
            UiFabrica.Icono(panel, "IconoMadera", ArteRecursos.CargarSpriteRecurso(TipoRecurso.Madera), new Vector2(142, 6));
            UiFabrica.Icono(panel, "IconoComida", ArteRecursos.CargarSpriteRecurso(TipoRecurso.Comida), new Vector2(274, 6));
            UiFabrica.Icono(panel, "IconoHierro", ArteRecursos.CargarSpriteRecurso(TipoRecurso.Hierro), new Vector2(404, 6));
            UiFabrica.Icono(panel, "IconoPiedra", ArteRecursos.CargarSpriteRecurso(TipoRecurso.Piedra), new Vector2(524, 6));

            // Botón de sonido arriba a la derecha (fuera de la barra).
            _btnSonido = UiFabrica.Boton(transform, "SONIDO: SÍ", AlternarSonido,
                new Vector2(1f, 1f), new Vector2(130, 34), new Vector2(-75, -25), 13);
        }

        private void AlternarSonido()
        {
            SonidoJuego.AlternarMudez();
            RefrescarBotonSonido();
            if (_gestor != null)
                _gestor.MostrarMensaje("Sonido: " + (SonidoJuego.Silenciado ? "NO" : "SÍ"), 2.5f, false);
            if (!SonidoJuego.Silenciado) SonidoJuego.Clic();
        }

        private void RefrescarBotonSonido()
        {
            bool si = !SonidoJuego.Silenciado;
            if (_btnSonido != null && si != _sonidoSi)
            {
                _sonidoSi = si;
                var t = _btnSonido.transform.Find("Txt")?.GetComponent<Text>();
                if (t != null) t.text = si ? "SONIDO: SÍ" : "SONIDO: NO";
            }
        }
    }
}
