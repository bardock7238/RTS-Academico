using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Modelo;

namespace Vista
{
    // Traduce clics/teclas → API del Controlador. No valida reglas de negocio:
    // solo pide y muestra el resultado (true/false) en la barra de mensajes.
    public class ControlInputUsuario : MonoBehaviour
    {
        private GestorJuego _gestor;
        private Unidad _seleccionada;
        private Edificio _edificioSeleccionado;

        private TipoEdificio? _modoConstruccion;
        private bool _modoRecoleccion;

        public void Inicializar(GestorJuego gestor)
        {
            _gestor = gestor;
            ConstruirBotonesSiFaltan();
        }

        private void Update()
        {
            if (_gestor == null || _gestor.Controlador == null) return;
            if (SobreUi()) return;

            if (Input.GetMouseButtonDown(0))
                ManejarClicIzquierdo();

            if (Input.GetMouseButtonDown(1))
                ManejarClicDerecho();

            // Atajos de teclado (la guía de la Vista usa QWER + 1-4).
            if (Input.GetKeyDown(KeyCode.Q)) Entrenar(TipoUnidad.Aldeano, TipoEdificio.CentroUrbano);
            if (Input.GetKeyDown(KeyCode.W)) Entrenar(TipoUnidad.Soldado, TipoEdificio.Cuartel);
            if (Input.GetKeyDown(KeyCode.E)) Entrenar(TipoUnidad.Arquero, TipoEdificio.Cuartel);
            if (Input.GetKeyDown(KeyCode.R)) Entrenar(TipoUnidad.Caballero, TipoEdificio.Cuartel);

            if (Input.GetKeyDown(KeyCode.Alpha1)) IniciarConstruccion(TipoEdificio.Casa);
            if (Input.GetKeyDown(KeyCode.Alpha2)) IniciarConstruccion(TipoEdificio.Cuartel);
            if (Input.GetKeyDown(KeyCode.Alpha3)) IniciarConstruccion(TipoEdificio.Torre);
            if (Input.GetKeyDown(KeyCode.Alpha4)) IniciarConstruccion(TipoEdificio.CentroUrbano);

            if (Input.GetKeyDown(KeyCode.C)) AlternarRecoleccion();
            if (Input.GetKeyDown(KeyCode.I)) RecogerItemCercano();
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _modoConstruccion = null;
                _modoRecoleccion = false;
                _seleccionada = null;
                _edificioSeleccionado = null;
                _gestor.VistaTablero?.LimpiarSeleccion();
                _gestor.MostrarMensaje("Seleccion cancelada");
            }
        }

        private void ManejarClicIzquierdo()
        {
            if (!TryGetCelda(out int x, out int y)) return;

            var foto = _gestor.UltimaFoto;
            if (foto == null) return;

            // Modo construcción pendiente: el clic es el destino.
            if (_modoConstruccion.HasValue)
            {
                TipoEdificio tipo = _modoConstruccion.Value;
                _modoConstruccion = null;
                bool ok = _gestor.Controlador.ConstruirEdificio(tipo, x, y);
                if (ok) _gestor.MostrarMensaje($"Construyendo {tipo} en ({x},{y})");
                else _gestor.AccionRechazada($"construir {tipo} en ({x},{y})");
                return;
            }

            // ¿Hay unidad/edificio propio en la casilla?
            Unidad propia = null;
            foreach (Unidad u in foto.UnidadesLocal)
            {
                if (u.PosicionX == x && u.PosicionY == y) { propia = u; break; }
            }
            if (propia != null)
            {
                _seleccionada = propia;
                _edificioSeleccionado = null;
                _gestor.VistaTablero?.MarcarSeleccion(x, y);
                _gestor.MostrarMensaje($"Seleccionado: {propia.Tipo}");
                return;
            }

            Edificio edificio = null;
            foreach (Edificio e in foto.EdificiosLocal)
            {
                if (e.PosicionX == x && e.PosicionY == y) { edificio = e; break; }
            }
            if (edificio != null)
            {
                _edificioSeleccionado = edificio;
                _seleccionada = null;
                _gestor.VistaTablero?.MarcarSeleccion(x, y);
                _gestor.MostrarMensaje($"Edificio: {edificio.Tipo} ({edificio.Estado})");
                return;
            }

            // Clic en yacimiento con modo recolección + aldeano seleccionado.
            if (_modoRecoleccion && _seleccionada != null && _seleccionada.EsRecolector)
            {
                Recurso r = null;
                foreach (Recurso rec in foto.Recursos)
                    if (rec.PosicionX == x && rec.PosicionY == y) { r = rec; break; }
                if (r != null)
                {
                    bool ok = _gestor.Controlador.IniciarRecoleccion(_seleccionada, r);
                    if (ok) _gestor.MostrarMensaje($"Recolectando {r.Tipo}");
                    else _gestor.AccionRechazada("recolectar (¿adyacente?)");
                    _modoRecoleccion = false;
                    return;
                }
            }

            // Clic vacío: limpiar selección.
            _seleccionada = null;
            _edificioSeleccionado = null;
            _gestor.VistaTablero?.LimpiarSeleccion();
            _gestor.MostrarMensaje("Sin selección");
        }

        // Recoger item adyacente con la unidad seleccionada (tecla I).
        private void RecogerItemCercano()
        {
            if (_seleccionada == null)
            {
                _gestor.MostrarMensaje("Selecciona una unidad (clic izq) para recoger");
                return;
            }

            var foto = _gestor.UltimaFoto;
            if (foto == null) return;

            Item mejor = null;
            int mejorDist = int.MaxValue;
            foreach (Item it in foto.Items)
            {
                int d = Mathf.Abs(_seleccionada.PosicionX - it.PosicionX)
                      + Mathf.Abs(_seleccionada.PosicionY - it.PosicionY);
                if (d <= 2 && d < mejorDist)
                {
                    mejorDist = d;
                    mejor = it;
                }
            }

            if (mejor == null)
            {
                _gestor.MostrarMensaje("No hay item cerca");
                return;
            }

            bool ok = _gestor.Controlador.RecogerItem(_seleccionada, mejor);
            if (ok) _gestor.MostrarMensaje($"Recogido: {mejor.Tipo}");
            else _gestor.AccionRechazada("recoger item (¿adyacente?)");
        }

        private void ManejarClicDerecho()
        {
            if (_seleccionada == null || !TryGetCelda(out int x, out int y)) return;

            var foto = _gestor.UltimaFoto;
            if (foto == null) return;

            // ¿Objetivo enemigo en esa casilla? → atacar.
            foreach (Unidad e in foto.UnidadesEnemigo)
            {
                if (e.PosicionX == x && e.PosicionY == y && e.EstaViva)
                {
                    bool ok = _gestor.Controlador.Atacar(_seleccionada, e);
                    if (ok) _gestor.MostrarMensaje($"Atacando {e.Tipo}");
                    else _gestor.AccionRechazada($"atacar {e.Tipo} (¿rango?)");
                    return;
                }
            }
            foreach (Edificio e in foto.EdificiosEnemigo)
            {
                if (e.PosicionX == x && e.PosicionY == y && e.EstaViva)
                {
                    bool ok = _gestor.Controlador.AtacarEdificio(_seleccionada, e);
                    if (ok) _gestor.MostrarMensaje($"Atacando {e.Tipo}");
                    else _gestor.AccionRechazada($"atacar {e.Tipo} (¿rango?)");
                    return;
                }
            }

            // Sino: mover.
            bool movio = _gestor.Controlador.MoverUnidad(_seleccionada, x, y);
            if (movio)
            {
                _gestor.MostrarMensaje($"Movido a ({x},{y})");
                _gestor.VistaTablero?.MarcarSeleccion(x, y);
            }
            else
            {
                _gestor.AccionRechazada($"mover a ({x},{y})");
            }
        }

        private void IniciarConstruccion(TipoEdificio tipo)
        {
            _modoConstruccion = tipo;
            _gestor.MostrarMensaje($"Clic en el mapa para construir {tipo}");
        }

        private void Entrenar(TipoUnidad tipo, TipoEdificio en)
        {
            bool ok = _gestor.Controlador.EntrenarUnidad(tipo, en);
            if (ok) _gestor.MostrarMensaje($"Entrenando {tipo} en {en}");
            else _gestor.AccionRechazada($"entrenar {tipo} en {en}");
        }

        private void AlternarRecoleccion()
        {
            if (_seleccionada == null || !_seleccionada.EsRecolector)
            {
                _gestor.MostrarMensaje("Selecciona un aldeano para recolectar (C)");
                return;
            }

            if (_gestor.Controlador.EstaRecolectando(_seleccionada))
            {
                bool stop = _gestor.Controlador.DetenerRecoleccion(_seleccionada);
                _gestor.MostrarMensaje(stop ? "Recolección detenida" : "No estaba recolectando");
                _modoRecoleccion = false;
            }
            else
            {
                _modoRecoleccion = true;
                _gestor.MostrarMensaje("Clic en un yacimiento para recolectar");
            }
        }

        private static bool SobreUi()
        {
            if (EventSystem.current == null) return false;
            return EventSystem.current.IsPointerOverGameObject();
        }

        private bool TryGetCelda(out int x, out int y)
        {
            x = y = 0;
            if (Camera.main == null) return false;
            Vector2 punto = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            x = Mathf.RoundToInt(punto.x);
            y = Mathf.RoundToInt(punto.y);
            return x >= 0 && x < Mapa.Ancho && y >= 0 && y < Mapa.Alto;
        }

        // Botones de acción en la barra inferior (placeholder; issue #6/#9 los embellece).
        private void ConstruirBotonesSiFaltan()
        {
            if (transform.Find("BarraAcciones") != null) return;

            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGo = new GameObject("CanvasAcciones", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
                canvasGo.transform.SetParent(transform, false);
                canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            var barra = new GameObject("BarraAcciones", typeof(RectTransform));
            barra.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)barra.transform;
            rt.anchorMin = new Vector2(0.5f, 0);
            rt.anchorMax = new Vector2(0.5f, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.sizeDelta = new Vector2(900, 52);
            rt.anchoredPosition = new Vector2(0, 8);
            var img = barra.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.55f);
            img.raycastTarget = false;

            CrearBoton(barra.transform, "Casa [1]", () => IniciarConstruccion(TipoEdificio.Casa), 0);
            CrearBoton(barra.transform, "Cuartel [2]", () => IniciarConstruccion(TipoEdificio.Cuartel), 1);
            CrearBoton(barra.transform, "Torre [3]", () => IniciarConstruccion(TipoEdificio.Torre), 2);
            CrearBoton(barra.transform, "Aldeano [Q]", () => Entrenar(TipoUnidad.Aldeano, TipoEdificio.CentroUrbano), 3);
            CrearBoton(barra.transform, "Soldado [W]", () => Entrenar(TipoUnidad.Soldado, TipoEdificio.Cuartel), 4);
            CrearBoton(barra.transform, "Recolectar [C]", AlternarRecoleccion, 5);
        }

        private static void CrearBoton(Transform padre, string etiqueta, UnityEngine.Events.UnityAction accion, int indice)
        {
            var go = new GameObject("Btn_" + etiqueta, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(padre, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0, 0.5f);
            rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(130, 40);
            rt.anchoredPosition = new Vector2(8 + indice * 142, 0);

            go.GetComponent<Image>().color = new Color(0.2f, 0.35f, 0.55f, 0.95f);
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(accion);

            var txtGo = new GameObject("Txt", typeof(RectTransform), typeof(Text));
            txtGo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)txtGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            var t = txtGo.GetComponent<Text>();
            t.font = HudRecursos.RecursoFuente();
            t.fontSize = 14;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.text = etiqueta;
            t.raycastTarget = false;
        }
    }
}
