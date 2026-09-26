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
        // Grupos de control (teclas 5-9): Ctrl+N guarda la selección, N la
        // recupera. Solo Vista (las unidades muertas se podan al llamar).
        private readonly Dictionary<int, List<Unidad>> _grupos = new Dictionary<int, List<Unidad>>();

        private TipoEdificio? _modoConstruccion;
        private bool _modoRecoger; // C: próximo clic = yacimiento o item; la unidad va sola

        // Caché del fantasma para no revalidar cada frame.
        private bool _fantasmaVale;
        private TipoEdificio _fantasmaTipo;
        private int _fantasmaX, _fantasmaY;
        private float _fantasmaTiempo;

        // Último estado visto en la selección (para detectar "dejó de caminar").
        private Unidad _estadoPrevioDe;
        private EstadoUnidad _estadoPrevio;

        // Cámara RTS (solo vista): rueda, flechas y arrastre con botón central.
        // Se cachea (Camera.main busca por tag cada vez).
        private Camera _cam;
        private bool _arrastraCamara;
        private Vector3 _arrastreRaton;
        private Vector3 _arrastreCamaraPos;
        private Camera Cam() => _cam != null ? _cam : (_cam = Camera.main);

        // Selección por arrastre: caja con botón izquierdo en modo normal.
        private bool _cajaActiva;
        private bool _arrastrandoCaja;
        private (int x, int y) _cajaDesdeCelda;
        private (int x, int y) _cajaHastaCelda;
        private Vector2 _cajaDesdePantalla;
        private GameObject _cajaGo;
        private SpriteRenderer _cajaSr;

        public void Inicializar(GestorJuego gestor)
        {
            _gestor = gestor;
            ConstruirBotonesSiFaltan();
            CrearMarcoCaja();
        }

        private void SeleccionarSolo(Unidad u)
        {
            _seleccionadas.Clear();
            if (u != null) _seleccionadas.Add(u);
            RefrescarMarcas();
        }

        // Alt+QWER: reemplaza la selección por todas las vivas de ese tipo.
        private void SeleccionarPorTipo(TipoUnidad tipo)
        {
            var foto = _gestor.UltimaFoto;
            _seleccionadas.Clear();
            if (foto != null)
                foreach (Unidad u in foto.UnidadesLocal)
                    if (u.EstaViva && u.Tipo == tipo) _seleccionadas.Add(u);
            _edificioSeleccionado = null;
            _modoConstruccion = null;
            _modoRecoger = false;
            RefrescarMarcas();
            if (_seleccionadas.Count == 0)
                _gestor.MostrarMensaje($"Sin {tipo}s vivos");
            else
                _gestor.MostrarMensaje($"{_seleccionadas.Count} {tipo} seleccionados");
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

        // Ctrl+5-9: guarda la selección viva en el grupo N (sobrescribe).
        private void GuardarGrupo(int n)
        {
            var vivas = new List<Unidad>();
            foreach (Unidad u in _seleccionadas)
                if (u != null && u.EstaViva) vivas.Add(u);
            if (vivas.Count == 0)
            {
                _gestor.AccionRechazada($"grupo {n}: sin selección viva");
                return;
            }
            _grupos[n] = vivas;
            _gestor.MostrarMensaje($"Grupo {n} guardado ({vivas.Count})");
        }

        // 5-9: recupera el grupo N (solo vivas del mundo actual; las muertas
        // o de una partida anterior se podan y no seleccionan fantasmas).
        private void LlamarGrupo(int n)
        {
            if (!_grupos.TryGetValue(n, out List<Unidad> grupo))
            {
                _gestor.AccionRechazada($"grupo {n} vacío");
                return;
            }
            var foto = _gestor.UltimaFoto;
            var vivas = new List<Unidad>();
            foreach (Unidad u in grupo)
                if (u != null && u.EstaViva && foto != null && foto.UnidadesLocal.Contains(u))
                    vivas.Add(u);
            _grupos[n] = vivas;
            if (vivas.Count == 0)
            {
                _gestor.AccionRechazada($"grupo {n} vacío");
                return;
            }
            _seleccionadas.Clear();
            _seleccionadas.AddRange(vivas);
            _edificioSeleccionado = null;
            _modoConstruccion = null;
            _modoRecoger = false;
            RefrescarMarcas();
            _gestor.MostrarMensaje(vivas.Count > 1
                ? $"Grupo {n} ({vivas.Count} unidades)"
                : $"Grupo {n}: {vivas[0].Tipo} ({vivas[0].Estado})");
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

        // Facción de una unidad para el rótulo: la tuya (Griegos) o la del
        // centro enemigo más cercano (Romanos, Persas...). Solo Vista.
        private string FaccionDe(Unidad u)
        {
            if (u == null || _gestor == null || _gestor.Controlador == null) return "?";
            var foto = _gestor.UltimaFoto;
            if (foto != null && foto.UnidadesLocal.Contains(u))
                return _gestor.Controlador.JugadorLocal.Nombre;
            string f = foto != null ? ArteRecursos.FaccionDeTropa(u, foto.EdificiosEnemigo) : null;
            return f ?? _gestor.Controlador.JugadorEnemigo.Nombre;
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
                _gestor.EstadoSeleccion = $"Seleccionado: {u.Tipo} ({u.Estado}) · {FaccionDe(u)}";
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

            // Menú inicial abierto: la partida aún no empezó, sin input.
            if (_gestor.MenuInicio != null && _gestor.MenuInicio.Abierto)
            {
                _gestor.VistaTablero?.OcultarFantasma();
                return;
            }

            // Fantasma de construcción: sigue al ratón con la huella delineada
            // (verde = se puede, rojo = no). Solo se revalida al cambiar de
            // celda (o cada 0.5 s, por si cambian recursos/unidades). Sobre
            // la UI se oculta.
            if (_modoConstruccion.HasValue && !SobreUi() && TryGetHover(out int hx, out int hy))
            {
                if (!_fantasmaVale || _fantasmaTipo != _modoConstruccion.Value
                    || _fantasmaX != hx || _fantasmaY != hy
                    || Time.unscaledTime - _fantasmaTiempo > 0.5f)
                {
                    _fantasmaVale = true;
                    _fantasmaTipo = _modoConstruccion.Value;
                    _fantasmaX = hx;
                    _fantasmaY = hy;
                    _fantasmaTiempo = Time.unscaledTime;
                    _gestor.VistaTablero?.MostrarFantasma(_modoConstruccion.Value, hx, hy,
                        AreaConstruible(_modoConstruccion.Value, hx, hy));
                }
            }
            else
            {
                _fantasmaVale = false;
                _gestor.VistaTablero?.OcultarFantasma();
            }

            // Cámara antes del filtro UI: la rueda funciona también sobre paneles.
            CamaraRts();

            if (SobreUi()) return;

            // Clic clásico o caja de selección (solo en modo normal: en modo
            // construcción/recoger el clic va directo a su acción).
            if (!_modoConstruccion.HasValue && !_modoRecoger
                && Input.GetMouseButtonDown(0) && TryGetHover(out int bx, out int by))
            {
                _cajaDesdeCelda = (bx, by);
                _cajaHastaCelda = (bx, by);
                _cajaDesdePantalla = Input.mousePosition;
                _cajaActiva = true;
                _arrastrandoCaja = false;
            }
            if (_cajaActiva)
            {
                if (Input.GetMouseButtonUp(0))
                {
                    _cajaActiva = false;
                    OcultarCaja();
                    if (_arrastrandoCaja) { _arrastrandoCaja = false; SeleccionarEnCaja(); }
                    else ManejarClicIzquierdo();
                }
                else if (Input.GetMouseButton(0))
                {
                    if (!_arrastrandoCaja
                        && ((Vector2)Input.mousePosition - _cajaDesdePantalla).magnitude > 10f)
                        _arrastrandoCaja = true;
                    if (_arrastrandoCaja && TryGetHover(out int hx2, out int hy2))
                    {
                        _cajaHastaCelda = (hx2, hy2);
                        PintarCaja();
                    }
                }
                else { _cajaActiva = false; OcultarCaja(); }
            }
            else if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                ManejarClicIzquierdo();

            // Atajos de teclado (la guía de la Vista usa QWER + 1-4).
            // Alt+QWER = seleccionar TODAS las unidades de ese tipo.
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (Input.GetKeyDown(KeyCode.Q))
            {
                if (alt) SeleccionarPorTipo(TipoUnidad.Aldeano);
                else Entrenar(TipoUnidad.Aldeano, TipoEdificio.CentroUrbano);
            }
            if (Input.GetKeyDown(KeyCode.W))
            {
                if (alt) SeleccionarPorTipo(TipoUnidad.Soldado);
                else Entrenar(TipoUnidad.Soldado, TipoEdificio.Cuartel);
            }
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (alt) SeleccionarPorTipo(TipoUnidad.Arquero);
                else Entrenar(TipoUnidad.Arquero, TipoEdificio.Cuartel);
            }
            if (Input.GetKeyDown(KeyCode.R))
            {
                if (alt) SeleccionarPorTipo(TipoUnidad.Caballero);
                else Entrenar(TipoUnidad.Caballero, TipoEdificio.Cuartel);
            }

            if (Input.GetKeyDown(KeyCode.Alpha1)) IniciarConstruccion(TipoEdificio.Casa);
            if (Input.GetKeyDown(KeyCode.Alpha2)) IniciarConstruccion(TipoEdificio.Cuartel);
            if (Input.GetKeyDown(KeyCode.Alpha3)) IniciarConstruccion(TipoEdificio.Torre);
            if (Input.GetKeyDown(KeyCode.Alpha4)) IniciarConstruccion(TipoEdificio.CentroUrbano);

            if (Input.GetKeyDown(KeyCode.C)) IniciarModoRecoger();
            if (Input.GetKeyDown(KeyCode.I)) RecogerItemCercano();

            // T: mercado, Y: herrería, M: vista completa, S: sonido sí/no.
            if (Input.GetKeyDown(KeyCode.T)) _gestor.MenuMercado?.Alternar();
            if (Input.GetKeyDown(KeyCode.Y)) _gestor.MenuMejoras?.Alternar();
            if (Input.GetKeyDown(KeyCode.M)) AlternarVistaCompleta();
            if (Input.GetKeyDown(KeyCode.S))
            {
                SonidoJuego.AlternarMudez();
                _gestor.MostrarMensaje("Sonido: " + (SonidoJuego.Silenciado ? "NO" : "SÍ"), 2.5f, false);
            }

            // 5-9: llamar grupo de control; Ctrl+5-9: guardar la selección.
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            for (int n = 5; n <= 9; n++)
            {
                if (!Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha5 + (n - 5)))) continue;
                if (ctrl) GuardarGrupo(n);
                else LlamarGrupo(n);
                break;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // Con el menú inicial abierto hay que elegir modo: Esc no hace nada.
                if (_gestor.MenuInicio != null && _gestor.MenuInicio.Abierto) return;
                // Con mercado/herrería abiertos, Esc los cierra y listo.
                if (_gestor.MenuMercado != null && _gestor.MenuMercado.Abierto)
                {
                    _gestor.MenuMercado.Cerrar();
                    return;
                }
                if (_gestor.MenuMejoras != null && _gestor.MenuMejoras.Abierto)
                {
                    _gestor.MenuMejoras.Cerrar();
                    return;
                }
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
                        _gestor.MostrarMensaje($"Seleccionado: {_seleccionadas[0].Tipo} ({_seleccionadas[0].Estado}) · {FaccionDe(_seleccionadas[0])}");
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
                    if (e.Ocupa(x, y)) { edificio = e; break; }
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
                            string fe = foto != null
                                ? ArteRecursos.FaccionDeTropa(e, foto.EdificiosEnemigo) : null;
                            _gestor.MostrarMensaje($"Yendo a atacar {e.Tipo}" +
                                (fe != null ? $" ({fe})" : "") + $" ({nOk} unidades)...");
                            _gestor.VistaTablero?.MarcarSeleccion(e.PosicionX, e.PosicionY);
                        }
                        else if (_seleccionadas.Count > 0)
                        {
                            Unidad pri = _seleccionadas[0];
                            _gestor.AccionRechazada($"atacar {e.Tipo}: {MotivoAtacar(pri, e)}");
                        }
                        else _gestor.MostrarMensaje("Selecciona tropas para atacar");
                        return;
                    }
                }
                // Ciervo → cazar con militares (+100 comida al matarlo).
                foreach (Unidad c in foto.Animales)
                {
                    if (c.PosicionX == x && c.PosicionY == y && c.EstaViva)
                    {
                        int nOk = 0;
                        foreach (Unidad u in _seleccionadas)
                            if (_gestor.Controlador.MoverAAtacar(u, c)) nOk++;
                        if (nOk > 0)
                        {
                            _gestor.MostrarMensaje($"Cazando ciervo ({nOk} unidades)...");
                            _gestor.VistaTablero?.MarcarSeleccion(c.PosicionX, c.PosicionY);
                        }
                        else if (_seleccionadas.Count > 0)
                        {
                            Unidad pri = _seleccionadas[0];
                            _gestor.AccionRechazada($"cazar ciervo: {MotivoAtacar(pri, c)}");
                        }
                        else _gestor.MostrarMensaje("Selecciona tropas para cazar");
                        return;
                    }
                }
                foreach (Edificio e in foto.EdificiosEnemigo)
                {
                    // Ocupa(): vale clicar CUALQUIER casilla de la huella (el
                    // Centro es 3x3, no solo su ancla).
                    if (e.Ocupa(x, y) && e.EstaViva)
                    {
                        int nOk = 0;
                        foreach (Unidad u in _seleccionadas)
                            if (_gestor.Controlador.MoverAAtacarEdificio(u, e)) nOk++;
                        if (nOk > 0)
                        {
                            _gestor.MostrarMensaje($"Demoliendo {e.Tipo} ({nOk} unidades)...");
                            _gestor.VistaTablero?.MarcarSeleccion(e.PosicionX, e.PosicionY);
                        }
                        else if (_seleccionadas.Count > 0)
                        {
                            Unidad pri = _seleccionadas[0];
                            string m = !pri.PuedeAtacar ? "los aldeanos no atacan"
                                : !e.EstaViva ? "edificio ya destruido"
                                : "sin hueco junto al edificio";
                            _gestor.AccionRechazada($"atacar {e.Tipo}: {m}");
                        }
                        else _gestor.MostrarMensaje("Selecciona tropas para atacar");
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
            Camera camItem = Cam();
            if (_seleccionada != null) { ox = _seleccionada.PosicionX; oy = _seleccionada.PosicionY; }
            else if (camItem != null)
            {
                ox = Mathf.RoundToInt(camItem.transform.position.x);
                oy = Mathf.RoundToInt(camItem.transform.position.y);
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
            Camera camCerca = Cam();
            int cx = camCerca != null
                ? Mathf.RoundToInt(camCerca.transform.position.x) : Mapa.Ancho / 2;
            int cy = camCerca != null
                ? Mathf.RoundToInt(camCerca.transform.position.y) : Mapa.Alto / 2;
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
                    Camera camModo = Cam();
                    int cx = Mapa.Ancho / 2, cy = Mapa.Alto / 2;
                    if (camModo != null)
                    {
                        cx = Mathf.RoundToInt(camModo.transform.position.x);
                        cy = Mathf.RoundToInt(camModo.transform.position.y);
                    }
                    // Preferir aldeano (para recolectar); si no, cualquier unidad.
                    SeleccionarSolo(AldeanoMasCercanoA(foto, cx, cy) ?? UnidadMasCercanaA(foto, cx, cy));
                    if (_seleccionada != null)
                    {
                        _edificioSeleccionado = null;
                        _gestor.VistaTablero?.MarcarSeleccion(_seleccionada.PosicionX, _seleccionada.PosicionY);
                        _gestor.MostrarMensaje($"Seleccionado: {_seleccionada.Tipo} ({_seleccionada.Estado}) · {FaccionDe(_seleccionada)}");
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
            _gestor.MostrarMensaje("Clic en un yacimiento o item: irá solo (C otra vez = cercano, Esc cancela)");
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

        // Cámara RTS: rueda = zoom (6..55), flechas = paneo, botón central =
        // arrastrar. No toca el Modelo: solo mueve la cámara de la Vista.
        private void CamaraRts()
        {
            Camera cam = Cam();
            if (cam == null || !cam.orthographic) return;

            float rueda = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(rueda) > 0.0001f)
                cam.orthographicSize = Mathf.Clamp(cam.orthographicSize - rueda * 4f, 6f, 55f);

            // Paneo solo fuera de la UI y sin estar escribiendo la IP del menú.
            if (!SobreUi() && !EstaEscribiendoEnUi())
            {
                float vel = cam.orthographicSize * 1.5f;
                Vector3 d = Vector3.zero;
                if (Input.GetKey(KeyCode.LeftArrow)) d.x -= 1f;
                if (Input.GetKey(KeyCode.RightArrow)) d.x += 1f;
                if (Input.GetKey(KeyCode.UpArrow)) d.y += 1f;
                if (Input.GetKey(KeyCode.DownArrow)) d.y -= 1f;
                if (d != Vector3.zero)
                    MoverCamaraA(cam.transform.position + d.normalized * vel * Time.unscaledDeltaTime);

                if (Input.GetMouseButtonDown(2))
                {
                    _arrastraCamara = true;
                    _arrastreRaton = Input.mousePosition;
                    _arrastreCamaraPos = cam.transform.position;
                }
            }
            if (Input.GetMouseButtonUp(2)) _arrastraCamara = false;
            if (_arrastraCamara && Input.GetMouseButton(2))
            {
                float mundoPorPixel = (cam.orthographicSize * 2f) / Screen.height;
                Vector3 delta = Input.mousePosition - _arrastreRaton;
                Vector3 p = _arrastreCamaraPos - new Vector3(delta.x * mundoPorPixel, delta.y * mundoPorPixel, 0f);
                p.z = cam.transform.position.z;
                MoverCamaraA(p);
            }
        }

        private void MoverCamaraA(Vector3 p)
        {
            Camera cam = Cam();
            if (cam == null) return;
            p.x = Mathf.Clamp(p.x, -2f, Mapa.Ancho + 1f);
            p.y = Mathf.Clamp(p.y, -2f, Mapa.Alto + 1f);
            cam.transform.position = p;
        }

        private static bool EstaEscribiendoEnUi()
        {
            var actual = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return actual != null && actual.GetComponent<UnityEngine.UI.InputField>() != null;
        }

        // Marco visual de la caja de selección (un sprite blanco traslúcido).
        private void CrearMarcoCaja()
        {
            if (_cajaGo != null) return;
            _cajaGo = new GameObject("MarcoCaja", typeof(SpriteRenderer));
            _cajaGo.transform.SetParent(transform, false);
            _cajaSr = _cajaGo.GetComponent<SpriteRenderer>();
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _cajaSr.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            _cajaSr.color = new Color(0.3f, 1f, 0.3f, 0.25f);
            _cajaSr.sortingOrder = 6;
            _cajaGo.SetActive(false);
        }

        private void PintarCaja()
        {
            if (_cajaSr == null) return;
            float tam = _gestor.VistaTablero != null ? _gestor.VistaTablero.TamanoCasilla : 1f;
            int x0 = Mathf.Min(_cajaDesdeCelda.x, _cajaHastaCelda.x);
            int x1 = Mathf.Max(_cajaDesdeCelda.x, _cajaHastaCelda.x);
            int y0 = Mathf.Min(_cajaDesdeCelda.y, _cajaHastaCelda.y);
            int y1 = Mathf.Max(_cajaDesdeCelda.y, _cajaHastaCelda.y);
            _cajaSr.transform.position = new Vector3((x0 + x1 + 1) / 2f * tam, (y0 + y1 + 1) / 2f * tam, -0.06f);
            _cajaSr.transform.localScale = new Vector3((x1 - x0 + 1) * tam, (y1 - y0 + 1) * tam, 1f);
            _cajaGo.SetActive(true);
        }

        private void OcultarCaja()
        {
            if (_cajaGo != null) _cajaGo.SetActive(false);
        }

        private void SeleccionarEnCaja()
        {
            var foto = _gestor.UltimaFoto;
            if (foto == null) return;
            int x0 = Mathf.Min(_cajaDesdeCelda.x, _cajaHastaCelda.x);
            int x1 = Mathf.Max(_cajaDesdeCelda.x, _cajaHastaCelda.x);
            int y0 = Mathf.Min(_cajaDesdeCelda.y, _cajaHastaCelda.y);
            int y1 = Mathf.Max(_cajaDesdeCelda.y, _cajaHastaCelda.y);
            _seleccionadas.Clear();
            foreach (Unidad u in foto.UnidadesLocal)
                if (u.EstaViva && u.PosicionX >= x0 && u.PosicionX <= x1 && u.PosicionY >= y0 && u.PosicionY <= y1)
                    _seleccionadas.Add(u);
            _edificioSeleccionado = null;
            RefrescarMarcas();
            _gestor.MostrarMensaje(_seleccionadas.Count == 0 ? "Sin selección"
                : _seleccionadas.Count == 1
                    ? $"Seleccionado: {_seleccionadas[0].Tipo} ({_seleccionadas[0].Estado}) · {FaccionDe(_seleccionadas[0])}"
                    : $"{_seleccionadas.Count} unidades seleccionadas");
        }

        private static bool SobreUi()
        {
            if (EventSystem.current == null) return false;
            return EventSystem.current.IsPointerOverGameObject();
        }

        private bool TryGetHover(out int x, out int y)
        {
            x = y = 0;
            Camera cam = Cam();
            if (cam == null) return false;
            Vector2 punto = cam.ScreenToWorldPoint(Input.mousePosition);
            x = Mathf.RoundToInt(punto.x);
            y = Mathf.RoundToInt(punto.y);
            return x >= 0 && x < Mapa.Ancho && y >= 0 && y < Mapa.Alto;
        }

        // ¿Se podría construir aquí? Replica la validación del Modelo leyendo
        // SOLO la foto (la Vista no toca el Modelo): área libre + costos.
        private bool AreaConstruible(TipoEdificio tipo, int x, int y)
        {
            var foto = _gestor.UltimaFoto;
            if (foto == null || !foto.EnEjecucion) return false;
            int lado = DatosDelJuego.LadoSegunTipo(tipo);
            for (int dx = 0; dx < lado; dx++)
                for (int dy = 0; dy < lado; dy++)
                {
                    int cx = x + dx, cy = y + dy;
                    if (cx < 0 || cy < 0 || cx >= Mapa.Ancho || cy >= Mapa.Alto) return false;
                    foreach (Recurso r in foto.Recursos)
                        if (r.PosicionX == cx && r.PosicionY == cy) return false;
                    foreach (Unidad u in foto.UnidadesLocal)
                        if (u.EstaViva && u.PosicionX == cx && u.PosicionY == cy) return false;
                    foreach (Unidad u in foto.UnidadesEnemigo)
                        if (u.EstaViva && u.PosicionX == cx && u.PosicionY == cy) return false;
                    foreach (Edificio e in foto.EdificiosLocal)
                        if (e.Ocupa(cx, cy)) return false;
                    foreach (Edificio e in foto.EdificiosEnemigo)
                        if (e.Ocupa(cx, cy)) return false;
                }
            EdificioConfig cfg = DatosDelJuego.EdificiosBase[tipo];
            return foto.Madera >= cfg.CostoMadera && foto.Oro >= cfg.CostoOro
                && foto.Comida >= cfg.CostoComida && foto.Hierro >= cfg.CostoHierro
                && foto.Piedra >= cfg.CostoPiedra;
        }

        private bool TryGetClic(out int x, out int y, out bool clicDerecho)
        {
            x = y = 0;
            clicDerecho = Input.GetMouseButtonDown(1);
            Camera cam = Cam();
            if (cam == null) return false;
            Vector2 punto = cam.ScreenToWorldPoint(Input.mousePosition);
            x = Mathf.RoundToInt(punto.x);
            y = Mathf.RoundToInt(punto.y);
            return x >= 0 && x < Mapa.Ancho && y >= 0 && y < Mapa.Alto;
        }

        private void IniciarConstruccion(TipoEdificio tipo)
        {
            _modoConstruccion = tipo;
            _gestor.MostrarMensaje($"Clic en el mapa para construir {tipo} (Esc cancela)");
        }

        // Diagnóstico del rechazo en el MISMO orden que ConstruirEdificioPara:
        // yacimiento → casilla ocupada → costos. Revisa toda la HUELLA
        // (Lado x Lado). La Vista solo lee la foto.
        private static string MotivoConstruccionFallida(InstantaneaJuego foto, TipoEdificio tipo, int x, int y)
        {
            int lado = DatosDelJuego.LadoSegunTipo(tipo);
            for (int dx = 0; dx < lado; dx++)
                for (int dy = 0; dy < lado; dy++)
                {
                    int cx = x + dx, cy = y + dy;
                    foreach (Recurso r in foto.Recursos)
                        if (r.PosicionX == cx && r.PosicionY == cy)
                            return $"hay un yacimiento de {r.Tipo}";
                }
            for (int dx = 0; dx < lado; dx++)
                for (int dy = 0; dy < lado; dy++)
                {
                    int cx = x + dx, cy = y + dy;
                    foreach (Unidad u in foto.UnidadesLocal)
                        if (u.EstaViva && u.PosicionX == cx && u.PosicionY == cy)
                            return "casilla ocupada por una unidad";
                    foreach (Unidad u in foto.UnidadesEnemigo)
                        if (u.EstaViva && u.PosicionX == cx && u.PosicionY == cy)
                            return "casilla ocupada por una unidad";
                    foreach (Edificio e in foto.EdificiosLocal)
                        if (e.Ocupa(cx, cy))
                            return $"casilla ocupada por {e.Tipo}";
                    foreach (Edificio e in foto.EdificiosEnemigo)
                        if (e.Ocupa(cx, cy))
                            return $"casilla ocupada por {e.Tipo}";
                }

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
                if (e.Ocupa(x, y))
                    return $"casilla ocupada por {e.Tipo}";
            foreach (Edificio e in foto.EdificiosEnemigo)
                if (e.Ocupa(x, y))
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
            if (!atacante.PuedeAtacar && enemigo.Tipo != TipoUnidad.Ciervo) return "los aldeanos no atacan";
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

        // Botones de acción en el PANEL LATERAL derecho (vertical, estilo RTS
        // clásico). Los atajos de teclado no cambian.
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
            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
            rt.sizeDelta = new Vector2(156, 766);
            rt.anchoredPosition = new Vector2(-8, 0);
            var img = barra.AddComponent<Image>();
            img.color = new Color(0.10f, 0.14f, 0.12f, 0.95f);
            img.raycastTarget = false;

            var titulo = UiFabrica.TextoCaja(barra.transform, "TituloAcciones", "ACCIONES",
                new Vector2(0.5f, 1f), new Vector2(140, 28), new Vector2(0, -20), 15);
            titulo.alignment = TextAnchor.MiddleCenter;
            titulo.color = new Color(1f, 0.85f, 0.4f, 1f);

            Sprite[] iconosEdificios = ArteRecursos.CargarEdificios();
            Sprite[] iconosUnidades = SpriteFactory.Unidades();
            CrearBotonEstructura(barra.transform, "Casa [1]", TipoEdificio.Casa, () => IniciarConstruccion(TipoEdificio.Casa), 0, iconosEdificios);
            CrearBotonEstructura(barra.transform, "Cuartel [2]", TipoEdificio.Cuartel, () => IniciarConstruccion(TipoEdificio.Cuartel), 1, iconosEdificios);
            CrearBotonEstructura(barra.transform, "Torre [3]", TipoEdificio.Torre, () => IniciarConstruccion(TipoEdificio.Torre), 2, iconosEdificios);
            CrearBotonEstructura(barra.transform, "Centro [4]", TipoEdificio.CentroUrbano, () => IniciarConstruccion(TipoEdificio.CentroUrbano), 3, iconosEdificios);
            CrearBotonEstructura(barra.transform, "Aldeano [Q]", TipoUnidad.Aldeano, () => Entrenar(TipoUnidad.Aldeano, TipoEdificio.CentroUrbano), 4, iconosUnidades);
            CrearBotonEstructura(barra.transform, "Soldado [W]", TipoUnidad.Soldado, () => Entrenar(TipoUnidad.Soldado, TipoEdificio.Cuartel), 5, iconosUnidades);
            CrearBotonEstructura(barra.transform, "Arquero [E]", TipoUnidad.Arquero, () => Entrenar(TipoUnidad.Arquero, TipoEdificio.Cuartel), 6, iconosUnidades);
            CrearBotonEstructura(barra.transform, "Caballero [R]", TipoUnidad.Caballero, () => Entrenar(TipoUnidad.Caballero, TipoEdificio.Cuartel), 7, iconosUnidades);
            CrearBotonAccion(barra.transform, "Recoger [C]", "clic en mapa", IniciarModoRecoger, 8);
            CrearBotonAccion(barra.transform, "Item [I]", "el más cercano", RecogerItemCercano, 9);
            CrearBotonAccion(barra.transform, "Mercado [T]", "trueque", () => _gestor.MenuMercado?.Alternar(), 10);
            CrearBotonAccion(barra.transform, "Mejoras [Y]", "herrería", () => _gestor.MenuMejoras?.Alternar(), 11);
            CrearBotonAccion(barra.transform, "Menú", "volver al inicio", () => _gestor.MenuInicio?.Mostrar(), 12);

            ConstruirBarraSeleccionSiFalta(canvas.transform);
        }

        // Botón de estructura/unidad con icono y costo (interfaz de construcción RTS).
        private static void CrearBotonEstructura(Transform padre, string etiqueta, TipoEdificio tipo,
            UnityEngine.Events.UnityAction accion, int indice, Sprite[] iconos)
        {
            Sprite icono = (iconos != null && (int)tipo >= 0 && (int)tipo < iconos.Length) ? iconos[(int)tipo] : null;
            EdificioConfig cfg = DatosDelJuego.EdificiosBase[tipo];
            UiFabrica.BotonEstructura(padre, etiqueta,
                CostoCorto(cfg.CostoMadera, cfg.CostoOro, cfg.CostoComida, cfg.CostoHierro, cfg.CostoPiedra),
                accion, new Vector2(0.5f, 1f), new Vector2(140, 52), new Vector2(0, -(50 + indice * 54)), icono);
        }

        private static void CrearBotonEstructura(Transform padre, string etiqueta, TipoUnidad tipo,
            UnityEngine.Events.UnityAction accion, int indice, Sprite[] iconos)
        {
            Sprite icono = (iconos != null && (int)tipo >= 0 && (int)tipo < iconos.Length) ? iconos[(int)tipo] : null;
            UnidadConfig cfg = DatosDelJuego.UnidadesBase[tipo];
            UiFabrica.BotonEstructura(padre, etiqueta,
                CostoCorto(cfg.CostoMadera, cfg.CostoOro, cfg.CostoComida, cfg.CostoHierro, cfg.CostoPiedra),
                accion, new Vector2(0.5f, 1f), new Vector2(140, 52), new Vector2(0, -(50 + indice * 54)), icono);
        }

        private static void CrearBotonAccion(Transform padre, string etiqueta, string pista,
            UnityEngine.Events.UnityAction accion, int indice)
        {
            UiFabrica.BotonEstructura(padre, etiqueta, pista, accion,
                new Vector2(0.5f, 1f), new Vector2(140, 52), new Vector2(0, -(50 + indice * 54)), null);
        }

        private static string CostoCorto(int m, int o, int c, int h, int p)
        {
            string s = "";
            if (m > 0) s += m + "M ";
            if (o > 0) s += o + "O ";
            if (c > 0) s += c + "C ";
            if (h > 0) s += h + "H ";
            if (p > 0) s += p + "P";
            s = s.Trim();
            return s == "" ? "Gratis" : s;
        }

        // Panel lateral IZQUIERDO: atajos de selección por tipo (lo mismo que
        // Alt+QWER, pero clicable). Solo lee la foto; no toca el Modelo.
        private void ConstruirBarraSeleccionSiFalta(Transform canvas)
        {
            if (transform.Find("BarraSeleccion") != null) return;

            var sel = new GameObject("BarraSeleccion", typeof(RectTransform));
            sel.transform.SetParent(canvas, false);
            var rt = (RectTransform)sel.transform;
            rt.anchorMin = new Vector2(0, 0.5f);
            rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(150, 288);
            rt.anchoredPosition = new Vector2(8, 30);
            var img = sel.AddComponent<Image>();
            img.color = new Color(0.10f, 0.14f, 0.12f, 0.95f);
            img.raycastTarget = false;

            var tituloT = UiFabrica.TextoCaja(sel.transform, "TituloTropas", "TROPAS",
                new Vector2(0.5f, 1f), new Vector2(140, 28), new Vector2(0, -20), 15);
            tituloT.alignment = TextAnchor.MiddleCenter;
            tituloT.color = new Color(1f, 0.85f, 0.4f, 1f);

            CrearBoton(sel.transform, "Aldeanos [AQ]", () => SeleccionarPorTipo(TipoUnidad.Aldeano), 0);
            CrearBoton(sel.transform, "Soldados [AW]", () => SeleccionarPorTipo(TipoUnidad.Soldado), 1);
            CrearBoton(sel.transform, "Arqueros [AE]", () => SeleccionarPorTipo(TipoUnidad.Arquero), 2);
            CrearBoton(sel.transform, "Caballeros [AR]", () => SeleccionarPorTipo(TipoUnidad.Caballero), 3);
            CrearBoton(sel.transform, "Todos", SeleccionarTodas, 4);
        }

        // Selecciona todas tus unidades vivas (para darles la misma orden).
        private void SeleccionarTodas()
        {
            var foto = _gestor.UltimaFoto;
            _seleccionadas.Clear();
            _edificioSeleccionado = null;
            if (foto != null)
                foreach (Unidad u in foto.UnidadesLocal)
                    if (u.EstaViva) _seleccionadas.Add(u);
            RefrescarMarcas();
            if (_seleccionadas.Count == 0)
                _gestor.MostrarMensaje("Sin unidades vivas");
            else
                _gestor.MostrarMensaje($"{_seleccionadas.Count} unidades seleccionadas");
        }

        // M: vista completa del mapa (zoom para verlo todo) y vuelta.
        // Guarda el zoom/posición previos para restaurarlos.
        private bool _vistaCompleta;
        private float _zoomPrevio = 15.5f;
        private Vector3 _posPrevia;

        private void AlternarVistaCompleta()
        {
            Camera cam = Cam();
            if (cam == null || !cam.orthographic) return;
            if (!_vistaCompleta)
            {
                _vistaCompleta = true;
                _zoomPrevio = cam.orthographicSize;
                _posPrevia = cam.transform.position;
                float mitad = Mathf.Max(Mapa.Ancho, Mapa.Alto) / 2f + 2f;
                cam.orthographicSize = Mathf.Min(mitad, 55f);
                cam.transform.position = new Vector3(
                    (Mapa.Ancho - 1) / 2f, (Mapa.Alto - 1) / 2f, cam.transform.position.z);
                _gestor.MostrarMensaje("Vista completa del mapa [M]");
            }
            else
            {
                _vistaCompleta = false;
                cam.orthographicSize = _zoomPrevio;
                MoverCamaraA(_posPrevia);
                _gestor.MostrarMensaje("Vista normal [M]");
            }
        }

        private static void CrearBoton(Transform padre, string etiqueta, UnityEngine.Events.UnityAction accion, int indice)
        {
            UiFabrica.Boton(padre, etiqueta, accion,
                new Vector2(0.5f, 1f), new Vector2(136, 38), new Vector2(0, -(50 + indice * 46)), 14);
        }
    }
}
