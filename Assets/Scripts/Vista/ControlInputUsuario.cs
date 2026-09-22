using System.Collections.Generic;
using System.Linq;
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
        // Selección múltiple: clic derecho AÑADE aldeanos/tropas al grupo.
        private readonly List<Unidad> _seleccionadas = new List<Unidad>();
        private Unidad _seleccionada => _seleccionadas.Count > 0 ? _seleccionadas[0] : null;
        private Edificio _edificioSeleccionado;

        private TipoEdificio? _modoConstruccion;
        private bool _modoRecoger; // C: próximo clic = yacimiento o item; la unidad va sola

        // Último estado visto en la selección (para detectar "dejó de caminar").
        private Unidad _estadoPrevioDe;
        private EstadoUnidad _estadoPrevio;

        public void Inicializar(GestorJuego gestor)
        {
            _gestor = gestor;
            ConstruirBotonesSiFaltan();
        }

        private void SeleccionarSolo(Unidad u)
        {
            _seleccionadas.Clear();
            if (u != null) _seleccionadas.Add(u);
            RefrescarMarcas();
        }

        private void AgregarSeleccion(Unidad u)
        {
            if (u == null || !u.EstaViva) return;
            if (_seleccionadas.Contains(u)) _seleccionadas.Remove(u);
            else _seleccionadas.Add(u);
            RefrescarMarcas();
        }

        private void LimpiarUnidadesSeleccionadas()
        {
            _seleccionadas.Clear();
            RefrescarMarcas();
        }

        private void RefrescarMarcas()
        {
            if (_seleccionadas.Count == 0)
            {
                if (_edificioSeleccionado == null) _gestor.VistaTablero?.LimpiarSeleccion();
                return;
            }
            var celdas = new List<(int X, int Y)>();
            foreach (Unidad u in _seleccionadas)
                if (u.EstaViva) celdas.Add((u.PosicionX, u.PosicionY));
            _gestor.VistaTablero?.MarcarSelecciones(celdas);
        }

        // Estado de la selección SIEMPRE visible en la barra (se refresca cada
        // frame: Recolectando, Moviendo, Atacando...). Caduca solo el mensaje efímero.
        private void ActualizarEstadoSeleccion()
        {
            // Poda de muertas del grupo.
            for (int i = _seleccionadas.Count - 1; i >= 0; i--)
                if (_seleccionadas[i] == null || !_seleccionadas[i].EstaViva)
                    _seleccionadas.RemoveAt(i);

            if (_seleccionadas.Count > 1)
            {
                _estadoPrevioDe = null;
                _gestor.EstadoSeleccion = $"{_seleccionadas.Count} unidades seleccionadas";
            }
            else if (_seleccionadas.Count == 1)
            {
                Unidad u = _seleccionadas[0];
                if (_estadoPrevioDe == u &&
                    _estadoPrevio == EstadoUnidad.Moviendo &&
                    u.Estado != EstadoUnidad.Moviendo)
                {
                    _gestor.LimpiarMensaje();
                }
                _estadoPrevioDe = u;
                _estadoPrevio = u.Estado;
                _gestor.EstadoSeleccion = $"Seleccionado: {u.Tipo} ({u.Estado})";
            }
            else if (_edificioSeleccionado != null)
            {
                _estadoPrevioDe = null;
                _gestor.EstadoSeleccion = $"Edificio: {_edificioSeleccionado.Tipo} ({_edificioSeleccionado.Estado})";
            }
            else
            {
                _estadoPrevioDe = null;
                _gestor.EstadoSeleccion = null;
            }
        }

        private void Update()
        {
            if (_gestor == null || _gestor.Controlador == null) return;
            ActualizarEstadoSeleccion();
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
                // Escape SIEMPRE deselecciona; si había viajes, también los corta.
                bool canceloViaje = false;
                foreach (Unidad u in _seleccionadas)
                    if (u.TieneDestino && _gestor.Controlador.CancelarDestino(u))
                        canceloViaje = true;
                _modoConstruccion = null;
                _modoRecoger = false;
                LimpiarUnidadesSeleccionadas();
                _edificioSeleccionado = null;
                _gestor.VistaTablero?.LimpiarSeleccion();
                _gestor.MostrarMensaje(canceloViaje
                    ? "Viajes cancelados y selección limpia"
                    : "Selección cancelada (Escape)");
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

            // Unidades/edificios propios en la casilla.
            // Izq = selección única (o todas las apiladas en la casilla).
            // Der = AÑADIR al grupo (selección múltiple).
            var propias = new List<Unidad>();
            foreach (Unidad u in foto.UnidadesLocal)
                if (u.EstaViva && u.PosicionX == x && u.PosicionY == y) propias.Add(u);

            if (propias.Count > 0)
            {
                if (clicDerecho)
                {
                    foreach (Unidad u in propias) AgregarSeleccion(u);
                    _edificioSeleccionado = null;
                    if (_seleccionadas.Count == 0)
                        _gestor.MostrarMensaje("Sin selección");
                    else if (_seleccionadas.Count > 1)
                        _gestor.MostrarMensaje($"{_seleccionadas.Count} unidades seleccionadas");
                    else
                        _gestor.MostrarMensaje($"Seleccionado: {_seleccionadas[0].Tipo} ({_seleccionadas[0].Estado})");
                    return;
                }

                // Izq: si hay grupo multi y clic en otra propia → reemplaza por
                // las de esa casilla (comportamiento clásico de un clic).
                SeleccionarSolo(propias[0]);
                for (int i = 1; i < propias.Count; i++) _seleccionadas.Add(propias[i]);
                RefrescarMarcas();
                _edificioSeleccionado = null;
                _gestor.MostrarMensaje(_seleccionadas.Count > 1
                    ? $"{_seleccionadas.Count} unidades seleccionadas"
                    : $"Seleccionado: {propias[0].Tipo} ({propias[0].Estado})");
                return;
            }

            if (!clicDerecho)
            {
                Edificio edificio = null;
                foreach (Edificio e in foto.EdificiosLocal)
                {
                    if (e.PosicionX == x && e.PosicionY == y) { edificio = e; break; }
                }
                if (edificio != null)
                {
                    _edificioSeleccionado = edificio;
                    LimpiarUnidadesSeleccionadas();
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

            // Con unidad(es) seleccionada(s): acción contextual en TODAS.
            if (_seleccionadas.Count > 0)
            {
                // Clic en item: todas caminan y lo recogen al llegar.
                Item itemClic = BuscarItemEn(x, y);
                if (itemClic != null)
                {
                    int nOk = 0;
                    foreach (Unidad u in _seleccionadas)
                        if (_gestor.Controlador.MoverARecogerItem(u, itemClic)) nOk++;
                    if (nOk > 0)
                    {
                        _gestor.MostrarMensaje($"Caminando a {DatosDelJuego.NombreDe(itemClic.Tipo)} ({itemClic.PosicionX},{itemClic.PosicionY})...");
                        _gestor.VistaTablero?.MarcarSeleccion(itemClic.PosicionX, itemClic.PosicionY);
                    }
                    else _gestor.AccionRechazada($"recoger item: {MotivoItem(foto, itemClic)}");
                    return;
                }

                // Clic en yacimiento → todos los aldeanos del grupo recolectan.
                Recurso r = null;
                foreach (Recurso rec in foto.Recursos)
                    if (rec.PosicionX == x && rec.PosicionY == y) { r = rec; break; }
                if (r != null)
                {
                    int nOk = 0, nAldeanos = 0;
                    foreach (Unidad u in _seleccionadas)
                    {
                        if (!u.EsRecolector || !u.EstaViva) continue;
                        nAldeanos++;
                        if (_gestor.Controlador.MoverARecolectar(u, r)) nOk++;
                    }
                    if (nOk > 0)
                    {
                        bool ady = _seleccionadas.Any(u =>
                            Mathf.Abs(u.PosicionX - r.PosicionX) <= 1 &&
                            Mathf.Abs(u.PosicionY - r.PosicionY) <= 1);
                        if (ady) _gestor.MostrarMensaje($"Recolectando {r.Tipo}");
                        else
                        {
                            _gestor.MostrarMensaje($"Caminando a {r.Tipo} ({r.PosicionX},{r.PosicionY})...");
                            _gestor.VistaTablero?.MarcarSeleccion(r.PosicionX, r.PosicionY);
                        }
                    }
                    else if (nAldeanos > 0) _gestor.AccionRechazada($"recolectar {r.Tipo}: {MotivoRecolectar(foto, r)}");
                    else _gestor.MostrarMensaje("Selecciona aldeanos para recolectar");
                    return;
                }

                // Enemigo → todas caminan/atacan.
                foreach (Unidad e in foto.UnidadesEnemigo)
                {
                    if (e.PosicionX == x && e.PosicionY == y && e.EstaViva)
                    {
                        int nOk = 0;
                        foreach (Unidad u in _seleccionadas)
                            if (_gestor.Controlador.MoverAAtacar(u, e)) nOk++;
                        if (nOk > 0)
                        {
                            _gestor.MostrarMensaje($"Yendo a atacar {e.Tipo} ({nOk} unidades)...");
                            _gestor.VistaTablero?.MarcarSeleccion(e.PosicionX, e.PosicionY);
                        }
                        else
                        {
                            Unidad pri = _seleccionadas[0];
                            _gestor.AccionRechazada($"atacar {e.Tipo}: {MotivoAtacar(pri, e)}");
                        }
                        return;
                    }
                }
                foreach (Edificio e in foto.EdificiosEnemigo)
                {
                    if (e.PosicionX == x && e.PosicionY == y && e.EstaViva)
                    {
                        int nOk = 0;
                        foreach (Unidad u in _seleccionadas)
                            if (_gestor.Controlador.AtacarEdificio(u, e)) nOk++;
                        if (nOk > 0) _gestor.MostrarMensaje($"Atacando {e.Tipo} ({nOk} unidades)");
                        else
                        {
                            Unidad pri = _seleccionadas[0];
                            int d = Mathf.Abs(pri.PosicionX - e.PosicionX)
                                  + Mathf.Abs(pri.PosicionY - e.PosicionY);
                            string m = !pri.PuedeAtacar ? "los aldeanos no atacan"
                                : !e.EstaViva ? "edificio ya destruido"
                                : d > pri.RangoAtaque ? $"fuera de rango (distancia {d}, rango {pri.RangoAtaque})"
                                : "no se pudo atacar";
                            _gestor.AccionRechazada($"atacar {e.Tipo}: {m}");
                        }
                        return;
                    }
                }

                // Mover TODO el grupo a la casilla (apilamiento permitido).
                _modoRecoger = false;
                int movieron = 0, rechazadas = 0;
                foreach (Unidad u in _seleccionadas)
                {
                    u.Objetivo = null;
                    if (_gestor.Controlador.MoverUnidad(u, x, y)) movieron++;
                    else rechazadas++;
                }
                if (movieron > 0)
                {
                    string prefijo = _seleccionadas.Count > 1 ? $"{movieron} unidades: " : "";
                    _gestor.MostrarMensaje($"{prefijo}Caminando a ({x},{y})...");
                    RefrescarMarcas();
                }
                else if (rechazadas > 0)
                {
                    _gestor.AccionRechazada($"mover a ({x},{y}): {MotivoMover(foto, x, y)}");
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
                var f = _gestor.UltimaFoto;
                _gestor.AccionRechazada($"recoger item: {(f != null ? MotivoItem(f, item) : "no se pudo")}");
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
                SeleccionarSolo(AldeanoMasCercanoA(foto, x, y));
                _edificioSeleccionado = null;
                if (_seleccionada == null)
                {
                    _gestor.AccionRechazada("recolectar: no hay aldeanos vivos");
                    return true; // el clic SÍ era un yacimiento
                }
                _gestor.VistaTablero?.MarcarSeleccion(_seleccionada.PosicionX, _seleccionada.PosicionY);
            }

            // Varias seleccionadas: todos los aldeanos del grupo van al yacimiento.
            var aldeanosGrupo = new List<Unidad>();
            foreach (Unidad u in _seleccionadas)
                if (u.EsRecolector && u.EstaViva) aldeanosGrupo.Add(u);
            if (aldeanosGrupo.Count == 0) aldeanosGrupo.Add(_seleccionada);

            int trabajaron = 0;
            bool algunAdy = false;
            foreach (Unidad al in aldeanosGrupo)
            {
                if (Mathf.Abs(al.PosicionX - r.PosicionX) <= 1 &&
                    Mathf.Abs(al.PosicionY - r.PosicionY) <= 1)
                    algunAdy = true;
                if (_gestor.Controlador.MoverARecolectar(al, r)) trabajaron++;
            }
            if (trabajaron > 0 && algunAdy) _gestor.MostrarMensaje($"Recolectando {r.Tipo}");
            else if (trabajaron > 0)
            {
                _gestor.MostrarMensaje($"Caminando a {r.Tipo} ({r.PosicionX},{r.PosicionY})...");
                _gestor.VistaTablero?.MarcarSeleccion(r.PosicionX, r.PosicionY);
            }
            else _gestor.AccionRechazada($"recolectar {r.Tipo}: {MotivoRecolectar(foto, r)}");
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
            SeleccionarSolo(UnidadMasCercanaA(foto, cx, cy));
            _edificioSeleccionado = null;
            if (_seleccionada != null)
                _gestor.VistaTablero?.MarcarSeleccion(_seleccionada.PosicionX, _seleccionada.PosicionY);
            else
                _gestor.AccionRechazada($"recoger item: {(foto != null ? MotivoItem(foto, null) : "no hay unidades vivas")}");
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
                    SeleccionarSolo(AldeanoMasCercanoA(foto, cx, cy) ?? UnidadMasCercanaA(foto, cx, cy));
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

        // Motivos de rechazo (la Vista solo lee la foto / objetos de la foto).
        private static string MotivoMover(InstantaneaJuego foto, int x, int y)
        {
            if (x < 0 || y < 0 || x >= Mapa.Ancho || y >= Mapa.Alto)
                return "fuera del mapa";
            foreach (Edificio e in foto.EdificiosLocal)
                if (e.PosicionX == x && e.PosicionY == y)
                    return $"casilla ocupada por {e.Tipo}";
            foreach (Edificio e in foto.EdificiosEnemigo)
                if (e.PosicionX == x && e.PosicionY == y)
                    return $"casilla ocupada por {e.Tipo}";
            if (!foto.EnEjecucion)
                return "partida pausada o terminada";
            return "no se pudo mover";
        }

        private static string MotivoRecolectar(InstantaneaJuego foto, Recurso r)
        {
            if (r == null) return "sin yacimiento";
            if (r.EstaAgotado) return "yacimiento agotado";
            bool hayAldeano = false;
            foreach (Unidad u in foto.UnidadesLocal)
                if (u.EstaViva && u.EsRecolector) { hayAldeano = true; break; }
            if (!hayAldeano) return "no hay aldeanos vivos";
            if (!foto.EnEjecucion) return "partida pausada o terminada";
            return "sin hueco libre junto al yacimiento";
        }

        private static string MotivoItem(InstantaneaJuego foto, Item item)
        {
            if (item != null && item.Recogido) return "item ya recogido";
            bool hayUnidad = false;
            foreach (Unidad u in foto.UnidadesLocal)
                if (u.EstaViva) { hayUnidad = true; break; }
            if (!hayUnidad) return "no hay unidades vivas";
            if (!foto.EnEjecucion) return "partida pausada o terminada";
            return item != null ? "sin hueco junto al item" : "no hay unidades vivas";
        }

        private static string MotivoAtacar(Unidad atacante, Unidad enemigo)
        {
            if (enemigo == null || !enemigo.EstaViva) return "objetivo ya muerto";
            if (!atacante.PuedeAtacar) return "los aldeanos no atacan";
            int d = Mathf.Abs(atacante.PosicionX - enemigo.PosicionX)
                  + Mathf.Abs(atacante.PosicionY - enemigo.PosicionY);
            if (d > atacante.RangoAtaque)
                return $"fuera de rango (distancia {d}, rango {atacante.RangoAtaque})";
            if (atacante.TiempoEsperaAtaque > 0) return "enfriamiento de ataque";
            return "no se pudo atacar";
        }

        private static string MotivoEntrenar(InstantaneaJuego foto, TipoUnidad tipo, TipoEdificio en)
        {
            bool hayTipo = false, hayOperativo = false;
            foreach (Edificio e in foto.EdificiosLocal)
            {
                if (e.Tipo != en || !e.EstaViva) continue;
                hayTipo = true;
                if (e.Estado == EstadoEdificio.Operativo) hayOperativo = true;
            }
            if (!hayTipo) return $"no tienes {en}";
            if (!hayOperativo) return $"{en} aún en construcción";
            if (!DatosDelJuego.EdificiosBase[en].UnidadesEntrenables.Contains(tipo))
                return $"{en} no entrena {tipo}";

            UnidadConfig cfg = DatosDelJuego.UnidadesBase[tipo];
            var faltan = new List<string>();
            if (foto.Madera < cfg.CostoMadera) faltan.Add($"Madera {foto.Madera}/{cfg.CostoMadera}");
            if (foto.Oro < cfg.CostoOro) faltan.Add($"Oro {foto.Oro}/{cfg.CostoOro}");
            if (foto.Comida < cfg.CostoComida) faltan.Add($"Comida {foto.Comida}/{cfg.CostoComida}");
            if (foto.Hierro < cfg.CostoHierro) faltan.Add($"Hierro {foto.Hierro}/{cfg.CostoHierro}");
            if (foto.Piedra < cfg.CostoPiedra) faltan.Add($"Piedra {foto.Piedra}/{cfg.CostoPiedra}");
            if (faltan.Count > 0) return $"falta {string.Join(", ", faltan)}";

            if (!foto.EnEjecucion) return "partida pausada o terminada";
            return "ya hay un entrenamiento de ese tipo en curso";
        }

        private void Entrenar(TipoUnidad tipo, TipoEdificio en)
        {
            bool ok = _gestor.Controlador.EntrenarUnidad(tipo, en);
            if (ok) _gestor.MostrarMensaje($"Entrenando {tipo} en {en}");
            else
            {
                var f = _gestor.UltimaFoto;
                _gestor.AccionRechazada($"entrenar {tipo} en {en}: {(f != null ? MotivoEntrenar(f, tipo, en) : "no se pudo")}");
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
