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

            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                ManejarClicIzquierdo();

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
                // Con unidad caminando, la primera Escape corta el viaje (la
                // mantiene seleccionada); la siguiente limpia la selección.
                if (_seleccionada != null && _seleccionada.TieneDestino &&
                    _gestor.Controlador.CancelarDestino(_seleccionada))
                {
                    _gestor.MostrarMensaje("Viaje cancelado");
                    return;
                }
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
            if (!TryGetClic(out int x, out int y, out bool clicDerecho)) return;

            var foto = _gestor.UltimaFoto;
            if (foto == null) return;

            // Modo construcción pendiente: el clic es el destino (solo izq).
            if (_modoConstruccion.HasValue && !clicDerecho)
            {
                TipoEdificio tipo = _modoConstruccion.Value;
                _modoConstruccion = null;
                bool ok = _gestor.Controlador.ConstruirEdificio(tipo, x, y);
                if (ok) _gestor.MostrarMensaje($"Construyendo {tipo} en ({x},{y})");
                else _gestor.AccionRechazada($"construir {tipo} en ({x},{y})");
                return;
            }

            // ¿Hay unidad/edificio propio en la casilla? → seleccionar (solo izq:
            // el clic der con unidad seleccionada ya se usa para mover/atacar).
            if (!clicDerecho)
            {
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
            }

            // Con unidad seleccionada: clic izq = acción contextual
            // (item / recolectar / atacar / mover). El clic der hace solo mover/atacar.
            if (_seleccionada != null)
            {
                // Clic en item (amarillo): si está al lado lo recoge; si no,
                // se mueve a su lado y lo recoge (mismo flujo que recolectar).
                Item itemClic = null;
                foreach (Item it in foto.Items)
                    if (it.PosicionX == x && it.PosicionY == y && !it.Recogido) { itemClic = it; break; }
                if (itemClic != null && !clicDerecho)
                {
                    RecogerItemConClick(itemClic);
                    return;
                }

                // Modo recolección + aldeano + clic en yacimiento (camina si lejos).
                if (_modoRecoleccion && _seleccionada.EsRecolector)
                {
                    Recurso r = null;
                    foreach (Recurso rec in foto.Recursos)
                        if (rec.PosicionX == x && rec.PosicionY == y) { r = rec; break; }
                    if (r != null)
                    {
                        bool ady = Mathf.Abs(_seleccionada.PosicionX - r.PosicionX) <= 1
                                && Mathf.Abs(_seleccionada.PosicionY - r.PosicionY) <= 1;
                        bool ok = _gestor.Controlador.MoverARecolectar(_seleccionada, r);
                        if (ok && ady) _gestor.MostrarMensaje($"Recolectando {r.Tipo}");
                        else if (ok) _gestor.MostrarMensaje($"Caminando a {r.Tipo} ({r.PosicionX},{r.PosicionY})...");
                        else _gestor.AccionRechazada($"recolectar {r.Tipo}");
                        _modoRecoleccion = false;
                        return;
                    }
                }

                // Enemigo en la casilla → atacar.
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

                // Casilla vacía/en propia → caminar hasta ahí (sin teletransporte).
                bool movio = _gestor.Controlador.MoverUnidad(_seleccionada, x, y);
                if (movio)
                {
                    _gestor.MostrarMensaje($"Caminando a ({x},{y})...");
                    _gestor.VistaTablero?.MarcarSeleccion(x, y);
                }
                else
                {
                    _gestor.AccionRechazada($"mover a ({x},{y})");
                }
                return;
            }

            // Sin selección: clic en vacío limpia.
            if (!clicDerecho)
            {
                _edificioSeleccionado = null;
                _gestor.VistaTablero?.LimpiarSeleccion();
                _gestor.MostrarMensaje("Sin selección");
            }
        }

        // Clic en un item: recoge si ya está adyacente; si no, camina hasta su
        // casilla y lo recoge solo al llegar (1 clic, sin teletransporte).
        private void RecogerItemConClick(Item item)
        {
            if (_seleccionada == null) return;

            int dx = Mathf.Abs(_seleccionada.PosicionX - item.PosicionX);
            int dy = Mathf.Abs(_seleccionada.PosicionY - item.PosicionY);
            bool adyacente = dx <= 1 && dy <= 1;

            bool ok = _gestor.Controlador.MoverARecogerItem(_seleccionada, item);
            if (!ok)
            {
                _gestor.AccionRechazada("recoger item");
                return;
            }

            if (adyacente) _gestor.MostrarMensaje($"Recogido: {item.Tipo}");
            else
            {
                _gestor.MostrarMensaje($"Caminando a {DatosDelJuego.NombreDe(item.Tipo)} ({item.PosicionX},{item.PosicionY})...");
                _gestor.VistaTablero?.MarcarSeleccion(item.PosicionX, item.PosicionY);
            }
        }

        // Recoger el item más cercano (tecla I, atajo si no hay clic disponible).
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
                if (d < mejorDist)
                {
                    mejorDist = d;
                    mejor = it;
                }
            }

            if (mejor == null)
            {
                _gestor.MostrarMensaje("No hay items en el mapa");
                return;
            }

            RecogerItemConClick(mejor);
        }

        private static bool SobreUi()
        {
            if (EventSystem.current == null) return false;
            return EventSystem.current.IsPointerOverGameObject();
        }

        private bool TryGetClic(out int x, out int y, out bool clicDerecho)
        {
            x = y = 0;
            clicDerecho = Input.GetMouseButtonDown(1);
            if (Camera.main == null) return false;
            Vector2 punto = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            x = Mathf.RoundToInt(punto.x);
            y = Mathf.RoundToInt(punto.y);
            return x >= 0 && x < Mapa.Ancho && y >= 0 && y < Mapa.Alto;
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
                _gestor.MostrarMensaje("Selecciona un aldeano (clic izq)");
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
                _gestor.MostrarMensaje("Clic en un yacimiento o item para recoger");
            }
        }

        // Botones de acción en la barra inferior (placeholder; issue #6/#9 los embellece).
        private void ConstruirBotonesSiFaltan()
        {
            if (transform.Find("BarraAcciones") != null) return;

            var canvas = FindAnyObjectByType<Canvas>();
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
            rt.sizeDelta = new Vector2(1040, 52);
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
            CrearBoton(barra.transform, "Item [I]", RecogerItemCercano, 6);
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
