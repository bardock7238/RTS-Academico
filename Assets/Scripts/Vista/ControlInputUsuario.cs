using System.Collections.Generic;
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
        private bool _modoRecoger; // C: próximo clic = yacimiento o item; la unidad va sola

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

            if (Input.GetKeyDown(KeyCode.C)) IniciarModoRecoger();
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
                _modoRecoger = false;
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
                else _gestor.AccionRechazada($"construir {tipo} en ({x},{y}): {MotivoConstruccionFallida(foto, tipo, x, y)}");
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
                    _gestor.MostrarMensaje($"Seleccionado: {propia.Tipo} ({propia.Estado})");
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

            // El modo C funciona también sin unidad "clásica" seleccionada
            // (autoselecta aldeano si el objetivo es un yacimiento).
            if (_modoRecoger && !clicDerecho)
            {
                EjecutarModoRecoger(x, y);
                return;
            }

            // Con unidad seleccionada: clic izq = acción contextual
            // (item / recolectar / atacar / mover). El clic der hace solo mover/atacar.
            if (_seleccionada != null)
            {
                // Clic en item (sin modo): si está al lado lo recoge; si no,
                // se mueve a su lado y lo recoge (mismo flujo que recolectar).
                Item itemClic = BuscarItemEn(x, y);
                if (itemClic != null && !clicDerecho)
                {
                    RecogerItemConClick(itemClic);
                    return;
                }

                // Clic en yacimiento con aldeano (solo izq) → camina y recolecta.
                if (!clicDerecho && _seleccionada.EsRecolector)
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
                        return;
                    }
                }

                // Enemigo (cualquier casilla) → MoverAAtacar: si ya está en rango
                // pega; si no, la unidad CAMINA hacia él hasta poder golpear.
                foreach (Unidad e in foto.UnidadesEnemigo)
                {
                    if (e.PosicionX == x && e.PosicionY == y && e.EstaViva)
                    {
                        bool ok = _gestor.Controlador.MoverAAtacar(_seleccionada, e);
                        if (ok)
                        {
                            int d = Mathf.Abs(_seleccionada.PosicionX - e.PosicionX)
                                  + Mathf.Abs(_seleccionada.PosicionY - e.PosicionY);
                            if (d <= _seleccionada.RangoAtaque)
                                _gestor.MostrarMensaje($"Atacando {e.Tipo}");
                            else
                            {
                                _gestor.MostrarMensaje($"Yendo a atacar {e.Tipo}...");
                                _gestor.VistaTablero?.MarcarSeleccion(e.PosicionX, e.PosicionY);
                            }
                        }
                        else _gestor.AccionRechazada($"atacar {e.Tipo}");
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

                // Mover con unidad seleccionada y clic en casilla vacía/en propia:
                // corta objetivo de combate y camina (sin teletransporte).
                _modoRecoger = false;
                if (_seleccionada != null) _seleccionada.Objetivo = null;
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

        // Tecla I: ir directo al item más cercano (sin modo ni clic).
        private void RecogerItemCercano()
        {
            AsegurarUnidadParaItem();
            if (_seleccionada == null) return;

            var foto = _gestor.UltimaFoto;
            if (foto == null) return;

            Item mejor = BuscarItemMasCercanoA(_seleccionada.PosicionX, _seleccionada.PosicionY);
            if (mejor == null)
            {
                _gestor.MostrarMensaje("No hay items en el mapa");
                return;
            }
            RecogerItemConClick(mejor);
        }

        // Clic en un yacimiento: si la seleccionada es aldeano, camina y recolecta;
        // si no (o no hay selección en modo C), autoselecta el aldeano más cercano.
        private bool IntentarRecolectar(InstantaneaJuego foto, int x, int y)
        {
            Recurso r = null;
            foreach (Recurso rec in foto.Recursos)
                if (rec.PosicionX == x && rec.PosicionY == y) { r = rec; break; }
            if (r == null) return false;

            if (_seleccionada == null || !_seleccionada.EsRecolector || !_seleccionada.EstaViva)
            {
                _seleccionada = AldeanoMasCercanoA(foto, x, y);
                _edificioSeleccionado = null;
                if (_seleccionada == null)
                {
                    _gestor.AccionRechazada("recolectar: no hay aldeanos vivos");
                    return true; // el clic SÍ era un yacimiento
                }
                _gestor.VistaTablero?.MarcarSeleccion(_seleccionada.PosicionX, _seleccionada.PosicionY);
            }

            bool ady = Mathf.Abs(_seleccionada.PosicionX - r.PosicionX) <= 1
                    && Mathf.Abs(_seleccionada.PosicionY - r.PosicionY) <= 1;
            bool ok = _gestor.Controlador.MoverARecolectar(_seleccionada, r);
            if (ok && ady) _gestor.MostrarMensaje($"Recolectando {r.Tipo}");
            else if (ok)
            {
                _gestor.MostrarMensaje($"Caminando a {r.Tipo} ({r.PosicionX},{r.PosicionY})...");
                _gestor.VistaTablero?.MarcarSeleccion(r.PosicionX, r.PosicionY);
            }
            else _gestor.AccionRechazada($"recolectar {r.Tipo}");
            return true;
        }

        // Modo C: clic en yacimiento → aldeano camina a recolectar; clic en item
        // (o cerca de uno) → unidad camina a recogerlo. Si no hay nada exacto,
        // elige el item/yacimiento más cercano al punto del clic.
        private void EjecutarModoRecoger(int x, int y)
        {
            _modoRecoger = false;
            var foto = _gestor.UltimaFoto;
            if (foto == null) return;

            // Exacto en la casilla: primero yacimiento, luego item.
            if (IntentarRecolectar(foto, x, y)) return;

            Item itemExacto = BuscarItemEn(x, y);
            if (itemExacto != null)
            {
                AsegurarUnidadParaItem();
                if (_seleccionada != null) RecogerItemConClick(itemExacto);
                return;
            }

            // Nada exacto: lo más cercano al clic entre items y yacimientos.
            Item itemC = BuscarItemMasCercanoA(x, y);
            Recurso recC = RecursoMasCercanoA(x, y);
            int dItem = itemC == null ? int.MaxValue
                : Mathf.Abs(itemC.PosicionX - x) + Mathf.Abs(itemC.PosicionY - y);
            int dRec = recC == null ? int.MaxValue
                : Mathf.Abs(recC.PosicionX - x) + Mathf.Abs(recC.PosicionY - y);

            if (dRec <= dItem && recC != null)
            {
                IntentarRecolectar(foto, recC.PosicionX, recC.PosicionY);
                return;
            }
            if (itemC != null)
            {
                AsegurarUnidadParaItem();
                if (_seleccionada != null) RecogerItemConClick(itemC);
                return;
            }

            _gestor.MostrarMensaje("No hay items ni yacimientos cerca (modo cancelado)");
        }

        // Segunda pulsada de C: va al recurso o item más cercano a la unidad
        // (o a la cámara si no hay selección).
        private void IrAlMasCercano()
        {
            var foto = _gestor.UltimaFoto;
            if (foto == null) return;

            int ox, oy;
            if (_seleccionada != null) { ox = _seleccionada.PosicionX; oy = _seleccionada.PosicionY; }
            else if (Camera.main != null)
            {
                ox = Mathf.RoundToInt(Camera.main.transform.position.x);
                oy = Mathf.RoundToInt(Camera.main.transform.position.y);
            }
            else { _gestor.MostrarMensaje("Sin cámara ni selección"); return; }

            Item item = BuscarItemMasCercanoA(ox, oy);
            Recurso rec = RecursoMasCercanoA(ox, oy);
            int dItem = item == null ? int.MaxValue
                : Mathf.Abs(item.PosicionX - ox) + Mathf.Abs(item.PosicionY - oy);
            int dRec = rec == null ? int.MaxValue
                : Mathf.Abs(rec.PosicionX - ox) + Mathf.Abs(rec.PosicionY - oy);

            if (rec != null && dRec <= dItem)
            {
                IntentarRecolectar(foto, rec.PosicionX, rec.PosicionY);
                return;
            }
            if (item != null)
            {
                AsegurarUnidadParaItem();
                if (_seleccionada != null) RecogerItemConClick(item);
                return;
            }

            _gestor.MostrarMensaje("No hay items ni yacimientos en el mapa");
        }

        private void AsegurarUnidadParaItem()
        {
            if (_seleccionada != null && _seleccionada.EstaViva) return;
            var foto = _gestor.UltimaFoto;
            if (foto == null) return;
            int cx = Camera.main != null
                ? Mathf.RoundToInt(Camera.main.transform.position.x) : Mapa.Ancho / 2;
            int cy = Camera.main != null
                ? Mathf.RoundToInt(Camera.main.transform.position.y) : Mapa.Alto / 2;
            _seleccionada = UnidadMasCercanaA(foto, cx, cy);
            _edificioSeleccionado = null;
            if (_seleccionada != null)
                _gestor.VistaTablero?.MarcarSeleccion(_seleccionada.PosicionX, _seleccionada.PosicionY);
            else
                _gestor.AccionRechazada("recoger: no hay unidades vivas");
        }

        // C: modo recoger (yacimiento/item). Sin selección autoselecciona un
        // aldeano (o la unidad viva más cercana). Segunda C = lo más cercano.
        private void IniciarModoRecoger()
        {
            if (_seleccionada == null || !_seleccionada.EstaViva)
            {
                var foto = _gestor.UltimaFoto;
                if (foto != null)
                {
                    int cx = Mapa.Ancho / 2, cy = Mapa.Alto / 2;
                    if (Camera.main != null)
                    {
                        cx = Mathf.RoundToInt(Camera.main.transform.position.x);
                        cy = Mathf.RoundToInt(Camera.main.transform.position.y);
                    }
                    // Preferir aldeano (para recolectar); si no, cualquier unidad.
                    _seleccionada = AldeanoMasCercanoA(foto, cx, cy) ?? UnidadMasCercanaA(foto, cx, cy);
                    if (_seleccionada != null)
                    {
                        _edificioSeleccionado = null;
                        _gestor.VistaTablero?.MarcarSeleccion(_seleccionada.PosicionX, _seleccionada.PosicionY);
                        _gestor.MostrarMensaje($"Seleccionado: {_seleccionada.Tipo} ({_seleccionada.Estado})");
                    }
                }
                if (_seleccionada == null)
                {
                    _gestor.MostrarMensaje("No hay unidades vivas para recolectar");
                    return;
                }
            }

            if (_modoRecoger)
            {
                _modoRecoger = false;
                IrAlMasCercano();
                return;
            }

            _modoConstruccion = null;
            _modoRecoger = true;
            _gestor.MostrarMensaje("Clic en un yacimiento o item: irá solo a recogerlo (C otra vez = más cercano)");
        }

        private Item BuscarItemEn(int x, int y)
        {
            var foto = _gestor.UltimaFoto;
            if (foto == null) return null;
            foreach (Item it in foto.Items)
                if (!it.Recogido && it.PosicionX == x && it.PosicionY == y) return it;
            return null;
        }

        private Item BuscarItemMasCercanoA(int x, int y)
        {
            var foto = _gestor.UltimaFoto;
            if (foto == null) return null;
            Item mejor = null;
            int mejorD = int.MaxValue;
            foreach (Item it in foto.Items)
            {
                if (it.Recogido) continue;
                int d = Mathf.Abs(it.PosicionX - x) + Mathf.Abs(it.PosicionY - y);
                if (d < mejorD) { mejorD = d; mejor = it; }
            }
            return mejor;
        }

        private Recurso RecursoMasCercanoA(int x, int y)
        {
            var foto = _gestor.UltimaFoto;
            if (foto == null) return null;
            Recurso mejor = null;
            int mejorD = int.MaxValue;
            foreach (Recurso rec in foto.Recursos)
            {
                int d = Mathf.Abs(rec.PosicionX - x) + Mathf.Abs(rec.PosicionY - y);
                if (d < mejorD) { mejorD = d; mejor = rec; }
            }
            return mejor;
        }

        private static Unidad AldeanoMasCercanoA(InstantaneaJuego foto, int x, int y)
        {
            Unidad mejor = null;
            int mejorD = int.MaxValue;
            foreach (Unidad u in foto.UnidadesLocal)
            {
                if (!u.EstaViva || !u.EsRecolector) continue;
                int d = Mathf.Abs(u.PosicionX - x) + Mathf.Abs(u.PosicionY - y);
                if (d < mejorD) { mejorD = d; mejor = u; }
            }
            return mejor;
        }

        private static Unidad UnidadMasCercanaA(InstantaneaJuego foto, int x, int y)
        {
            Unidad mejor = null;
            int mejorD = int.MaxValue;
            foreach (Unidad u in foto.UnidadesLocal)
            {
                if (!u.EstaViva) continue;
                int d = Mathf.Abs(u.PosicionX - x) + Mathf.Abs(u.PosicionY - y);
                if (d < mejorD) { mejorD = d; mejor = u; }
            }
            return mejor;
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

        // Diagnóstico del rechazo en el MISMO orden que ConstruirEdificioPara:
        // yacimiento → casilla ocupada → costos. La Vista solo lee la foto.
        private static string MotivoConstruccionFallida(InstantaneaJuego foto, TipoEdificio tipo, int x, int y)
        {
            foreach (Recurso r in foto.Recursos)
                if (r.PosicionX == x && r.PosicionY == y)
                    return $"hay un yacimiento de {r.Tipo}";

            foreach (Unidad u in foto.UnidadesLocal)
                if (u.PosicionX == x && u.PosicionY == y)
                    return "casilla ocupada por una unidad";
            foreach (Unidad u in foto.UnidadesEnemigo)
                if (u.PosicionX == x && u.PosicionY == y)
                    return "casilla ocupada por una unidad";
            foreach (Edificio e in foto.EdificiosLocal)
                if (e.PosicionX == x && e.PosicionY == y)
                    return $"casilla ocupada por {e.Tipo}";
            foreach (Edificio e in foto.EdificiosEnemigo)
                if (e.PosicionX == x && e.PosicionY == y)
                    return $"casilla ocupada por {e.Tipo}";

            EdificioConfig cfg = DatosDelJuego.EdificiosBase[tipo];
            var faltan = new List<string>();
            if (foto.Madera < cfg.CostoMadera) faltan.Add($"Madera {foto.Madera}/{cfg.CostoMadera}");
            if (foto.Oro < cfg.CostoOro) faltan.Add($"Oro {foto.Oro}/{cfg.CostoOro}");
            if (foto.Comida < cfg.CostoComida) faltan.Add($"Comida {foto.Comida}/{cfg.CostoComida}");
            if (foto.Hierro < cfg.CostoHierro) faltan.Add($"Hierro {foto.Hierro}/{cfg.CostoHierro}");
            if (foto.Piedra < cfg.CostoPiedra) faltan.Add($"Piedra {foto.Piedra}/{cfg.CostoPiedra}");
            if (faltan.Count > 0) return $"falta {string.Join(", ", faltan)}";

            return "no se pudo (¿partida pausada o terminada?)";
        }

        private void Entrenar(TipoUnidad tipo, TipoEdificio en)
        {
            bool ok = _gestor.Controlador.EntrenarUnidad(tipo, en);
            if (ok) _gestor.MostrarMensaje($"Entrenando {tipo} en {en}");
            else _gestor.AccionRechazada($"entrenar {tipo} en {en}");
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
            CrearBoton(barra.transform, "Recoger [C]", IniciarModoRecoger, 5);
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
