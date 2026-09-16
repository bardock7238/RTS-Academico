using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Modelo
{
    // ============================================================================
    //  SIMULACIÓN (Modelo)
    //  ----------------------------------------------------------------------------
    //  "El mundo completo está AQUÍ, y AQUÍ único lugar donde vive la concurrencia."
    //
    //  Este motor concentra TODOS los mecanismos concurrentes del juego:
    //    · Candado compartido: cada cambio a recursos/listas pasa por lock(Candado).
    //    · Reloj en tiempo real (Task de fondo que suma 1 segundo cada segundo real).
    //    · Entrenamiento (Task por unidad + CancellationToken para cancelar).
    //    · Construcción (Task que completa el edificio solo).
    //    · Recolección (Task por aldeano + CancellationToken).
    //    · Spawner de items (solo el host siembra objetos solos en el mapa).
    //    · Expiración del Casco (Task temporal que quita la defensa extra).
    //
    //  El Controlador NO crea hilos ni candados: solo pide acciones y avisa al
    //  rival por red. De esa manera la concurrencia queda 100% en el Modelo.
    //
    //  Uso en red:
    //    · Las acciones DEL JUGADOR local se aplican aquí y el Controlador las
    //      anuncia al rival; el rival las refleja con los métodos "Espejo" (XxxRival).
    //    · Solo lo que el motor decide SOLO necesita avisar por red (una unidad que
    //      terminó de entrenar, un item que apareció): se usa el evento ParaTransmitir.
    // ============================================================================
    public class Simulacion
    {
        // [Concurrencia] Candado del MUNDO (lo posee el Modelo): toda mutación de
        // unidades, edificios, recursos o items pasa por aquí. Los Tasks de fondo y
        // las acciones del jugador se serializan con este candado.
        public readonly object Candado = new object();

        public Jugador JugadorLocal { get; private set; }
        public Jugador JugadorEnemigo { get; private set; }
        public Mapa Tablero { get; private set; }
        public Partida EstadoPartida { get; private set; }

        // ---- Items en el mapa (el spawner concurrente del host los siembra) ----
        private readonly bool _esHost;                 // Solo el host siembra items.
        private readonly Random _rng = new Random();
        private readonly CancellationTokenSource _ctsSpawner = new CancellationTokenSource();
        private readonly List<Item> _itemsGlobales = new List<Item>();

        // La Vista lee esto (vía hilo principal) para pintar los items en el mapa.
        public List<Item> ItemsVisibles => _itemsGlobales;

        // Trabajos en segundo plano activos: para poder cancelarlos.
        private readonly Dictionary<TipoUnidad, CancellationTokenSource> _entrenamientosActivos =
            new Dictionary<TipoUnidad, CancellationTokenSource>();
        private readonly Dictionary<Unidad, CancellationTokenSource> _recolectoresActivos =
            new Dictionary<Unidad, CancellationTokenSource>();

        // [Concurrencia] Reloj del juego (RTS en tiempo real): un Task de fondo que
        // suma 1 segundo por cada segundo real. Se cancela con este token.
        private readonly CancellationTokenSource _ctsReloj = new CancellationTokenSource();

        // == CONFIGURACIÓN (el Controlador/tests la ajusta; el Modelo la usa) ==
        // Cada cuántos ms late un ciclo de recolección (1000 = 1 segundo real).
        public int CicloRecoleccionMs { get; set; } = 1000;
        // Cada cuántos ms late el reloj (1000 = 1 segundo real).
        public int RelojTickMs { get; set; } = 1000;
        // Cada cuántos ms siembra el spawner un item (host).
        public int IntervaloSpawnerMs { get; set; } = 3000;
        // Cuántos SEGUNDOS dura el Casco antes de que su Task lo apague.
        public int DuracionCascoSegundos { get; set; } = 10;
        // Delegados de espera: permiten acelerar el tiempo en pruebas (DemoRapida).
        public Func<int, CancellationToken, Task> EsperarEntrenamiento { get; set; } =
            (segundos, token) => Task.Delay(segundos * 1000, token);
        public Func<int, Task> EsperarConstruccion { get; set; } =
            segundos => Task.Delay(segundos * 1000);

        // [Concurrencia] Aviso del Modelo hacia el Controlador: sucedió algo que el
        // Controlador debe anunciar por red (terminó un entrenamiento, apareció un
        // item, un aldeano dejó de recolectar solo...). El Controlador se suscribe.
        public event Action<string> ParaTransmitir;

        public Simulacion(string nombreJugador, bool localArriba = true)
        {
            _esHost = localArriba;
            JugadorLocal = new Jugador(nombreJugador);
            JugadorEnemigo = new Jugador("Enemigo");
            Tablero = new Mapa();
            EstadoPartida = new Partida();

            // [Concurrencia] RTS en tiempo real: desde el arranque, un Task de fondo
            // marca los segundos de partida mientras esta siga en ejecución.
            _ = IniciarRelojAsync();

            // [Concurrencia] El host SIEMBRA items solos en el mapa; su Task anuncia
            // cada colocación por red para que el espejo del cliente vea lo mismo.
            if (_esHost)
                _ = IniciarSpawnerItemsAsync();

            // Cada instancia coloca a SU jugador en su lado. El host (localArriba = true)
            // vive arriba; el cliente (localArriba = false) vive abajo. Así las copias
            // de ambos mundos concuerdan y la red puede espejar movimientos por casilla.
            int centroLocalY = localArriba ? 1 : 13;
            int centroEnemigoY = localArriba ? 13 : 1;
            int aldeanoLocalY = localArriba ? 1 : 13;
            int aldeanoEnemigoY = localArriba ? 13 : 1;

            Edificio centroLocal = DatosDelJuego.CrearCentroUrbano(7, centroLocalY);
            Edificio centroEnemigo = DatosDelJuego.CrearCentroUrbano(7, centroEnemigoY);
            JugadorLocal.AgregarEdificio(centroLocal);
            JugadorEnemigo.AgregarEdificio(centroEnemigo);

            JugadorLocal.AgregarUnidad(DatosDelJuego.CrearUnidad(TipoUnidad.Aldeano, 6, aldeanoLocalY));
            JugadorEnemigo.AgregarUnidad(DatosDelJuego.CrearUnidad(TipoUnidad.Aldeano, 6, aldeanoEnemigoY));

            GestorArchivos.GuardarConfiguracionInicial(
                $"Jugador: {nombreJugador} | Mapa: {Mapa.Ancho}x{Mapa.Alto} | " +
                $"Centro local ({centroLocal.PosicionX},{centroLocal.PosicionY}) | " +
                $"Centro enemigo ({centroEnemigo.PosicionX},{centroEnemigo.PosicionY})");
            GestorArchivos.RegistrarAccion(nombreJugador, "Inicio", "Partida inicializada.");
        }

        // ============ ACCIONES DEL JUGADOR (todas con lock(Candado) interno) ============

        // 1. Mover Unidad
        public bool MoverUnidad(Unidad unidad, int nuevoX, int nuevoY)
        {
            lock (Candado)
            {
                if (unidad == null) return false;
                if (!Tablero.EsCoordenadaValida(nuevoX, nuevoY)) return false;
                if (!Tablero.EsCasillaLibre(nuevoX, nuevoY, JugadorLocal, JugadorEnemigo)) return false;

                int origenX = unidad.PosicionX;
                int origenY = unidad.PosicionY;
                unidad.MoverA(nuevoX, nuevoY);

                GestorArchivos.RegistrarAccion(
                    JugadorLocal.Nombre,
                    "Mover",
                    $"{unidad.Tipo} de ({origenX},{origenY}) a ({nuevoX},{nuevoY})");
                return true;
            }
        }

        // 2. Construir Edificio (valida coordenadas, choque, yacimiento y costos del catálogo)
        public bool ConstruirEdificio(TipoEdificio tipo, int x, int y)
        {
            lock (Candado)
            {
                if (!Tablero.EsCoordenadaValida(x, y)) return false;
                if (Tablero.CasillaTieneRecurso(x, y)) return false;
                if (!Tablero.EsCasillaLibre(x, y, JugadorLocal, JugadorEnemigo)) return false;

                EdificioConfig config = DatosDelJuego.EdificiosBase[tipo];
                if (!JugadorLocal.Gastar(config.CostoMadera, config.CostoOro, config.CostoComida))
                    return false;

                Edificio nuevoEdificio = DatosDelJuego.CrearEdificio(tipo, x, y);
                JugadorLocal.AgregarEdificio(nuevoEdificio);

                // La obra avanza sola en segundo plano y completa el edificio.
                _ = ConstruccionTaskAsync(nuevoEdificio, config.TiempoConstruccionSegundos, JugadorLocal.Nombre);

                GestorArchivos.RegistrarAccion(
                    JugadorLocal.Nombre,
                    "Construir",
                    $"{tipo} en ({x},{y}). Madera: {JugadorLocal.Madera}, Oro: {JugadorLocal.Oro}, Comida: {JugadorLocal.Comida}");
                return true;
            }
        }

        // 3. Entrenar Unidad: valida, cobra y lanza el "trabajo" en segundo plano.
        // [Concurrencia] La Task no bloquea al usuario: la unidad aparece al terminar.
        public bool EntrenarUnidad(TipoUnidad tipo, TipoEdificio edificioOrigen)
        {
            lock (Candado)
            {
                Edificio edificio = JugadorLocal.Edificios.FirstOrDefault(e => e.Tipo == edificioOrigen);
                if (edificio == null || !edificio.PuedeEntrenar(tipo)) return false;
                if (_entrenamientosActivos.ContainsKey(tipo)) return false; // ya hay uno en curso

                UnidadConfig config = DatosDelJuego.UnidadesBase[tipo];
                if (!JugadorLocal.Gastar(config.CostoMadera, config.CostoOro, config.CostoComida))
                    return false;

                CancellationTokenSource cts = new CancellationTokenSource();
                _entrenamientosActivos[tipo] = cts;

                // Lanzamos la tarea y seguimos: no bloqueamos al usuario.
                _ = EntrenamientoTaskAsync(tipo, config, cts.Token);

                GestorArchivos.RegistrarAccion(
                    JugadorLocal.Nombre,
                    "Entrenar",
                    $"{tipo} iniciado en {edificioOrigen}.");
                return true;
            }
        }

        // [Concurrencia] La obra avanza sola en segundo plano y completa el edificio;
        // la misma Task se usa para el espejo del edificio rival en la otra máquina.
        private async Task ConstruccionTaskAsync(Edificio edificio, int segundos, string nombreDueno)
        {
            await EsperarConstruccion(segundos);

            lock (Candado)
            {
                edificio.CompletarConstruccion();
                GestorArchivos.RegistrarAccion(
                    nombreDueno,
                    "Construir",
                    $"{edificio.Tipo} en ({edificio.PosicionX},{edificio.PosicionY}) terminado.");
            }
        }

        private async Task EntrenamientoTaskAsync(TipoUnidad tipo, UnidadConfig config, CancellationToken token)
        {
            bool completado = true;
            try
            {
                await EsperarEntrenamiento(config.TiempoEntrenamientoSegundos, token);
            }
            catch (TaskCanceledException)
            {
                completado = false;
            }

            lock (Candado)
            {
                _entrenamientosActivos.Remove(tipo);
                if (!completado || token.IsCancellationRequested) return;

                Unidad nueva = DatosDelJuego.CrearUnidad(tipo, 0, 0);
                Edificio origen = JugadorLocal.Edificios.FirstOrDefault(
                    e => e.UnidadesEntrenables.Contains(tipo) && e.EstaOperativo);
                if (origen != null)
                {
                    (int x, int y) = ObtenerPosicionDeSalida(origen);
                    nueva.MoverA(x, y);
                }

                JugadorLocal.AgregarUnidad(nueva);
                ParaTransmitir?.Invoke($"ENTRENAR;{tipo};{nueva.PosicionX};{nueva.PosicionY}");
                GestorArchivos.RegistrarAccion(
                    JugadorLocal.Nombre,
                    "Entrenar",
                    $"{tipo} listo en ({nueva.PosicionX},{nueva.PosicionY}).");
            }
        }

        // 4. Recolectar recursos en segundo plano (un hilo por aldeano).
        // [Concurrencia] Task de fondo + lock(Candado) + CancellationToken:
        // el aldeano trabaja, suma recursos y el jugador puede seguir jugando.
        public bool IniciarRecoleccion(Unidad aldeano, Recurso recurso)
        {
            lock (Candado)
            {
                if (aldeano == null || recurso == null) return false;
                if (!aldeano.EsRecolector || !aldeano.EstaViva) return false;
                if (recurso.EstaAgotado) return false;
                if (!Tablero.RecursosEnMapa.Contains(recurso)) return false;
                if (_recolectoresActivos.ContainsKey(aldeano)) return false; // ya trabaja
                if (!EstanAdyacentes(aldeano, recurso)) return false;

                aldeano.Estado = EstadoUnidad.Recolectando;
                CancellationTokenSource cts = new CancellationTokenSource();
                _recolectoresActivos[aldeano] = cts;

                _ = RecoleccionTaskAsync(aldeano, recurso, cts.Token);

                GestorArchivos.RegistrarAccion(
                    JugadorLocal.Nombre,
                    "Recolectar",
                    $"{aldeano.Tipo} recolecta {recurso.Tipo} en ({recurso.PosicionX},{recurso.PosicionY}).");
                return true;
            }
        }

        public bool DetenerRecoleccion(Unidad aldeano)
        {
            lock (Candado)
            {
                if (_recolectoresActivos.TryGetValue(aldeano, out CancellationTokenSource cts))
                {
                    cts.Cancel();
                    return true;
                }
                return false;
            }
        }

        public bool EstaRecolectando(Unidad aldeano)
        {
            lock (Candado)
            {
                return _recolectoresActivos.ContainsKey(aldeano);
            }
        }

        private async Task RecoleccionTaskAsync(Unidad aldeano, Recurso recurso, CancellationToken token)
        {
            int cantidadPorCiclo = DatosDelJuego.UnidadesBase[aldeano.Tipo].CapacidadRecoleccion;
            bool terminoSolo = false; // True si dejó de recolectar por sí mismo (no por detenerlo).

            while (true)
            {
                try { await Task.Delay(CicloRecoleccionMs, token); }
                catch (TaskCanceledException) { break; } // Lo detuvieron: el 0 ya salió de DetenerRecoleccion.

                lock (Candado)
                {
                    if (!aldeano.EstaViva || recurso.EstaAgotado) { terminoSolo = true; break; }
                    int cantidad = recurso.Extraer(cantidadPorCiclo);
                    if (cantidad <= 0) { terminoSolo = true; break; }

                    // [Items] Pasivo Herramientas: +5% de lo recolectado por ciclo.
                    cantidad += (int)Math.Round(cantidad * JugadorLocal.BonusRecoleccion);

                    EntregarRecurso(recurso.Tipo, cantidad);
                    GestorArchivos.RegistrarAccion(
                        JugadorLocal.Nombre,
                        "Recolectar",
                        $"+{cantidad} de {recurso.Tipo}. Total {JugadorLocal.Oro}/{JugadorLocal.Madera}/{JugadorLocal.Comida}");
                }
            }

            lock (Candado)
            {
                _recolectoresActivos.Remove(aldeano);
                aldeano.Estado = EstadoUnidad.Idle;
            }

            // Si terminó solo (yacimiento vacío o aldeano muerto), avisa al rival para
            // que su copia también vuelva a Idle. Si lo detuvieron, ya avisó el Controlador.
            if (terminoSolo)
                ParaTransmitir?.Invoke($"RECOLECTAR;{aldeano.PosicionX};{aldeano.PosicionY};0");
        }

        private void EntregarRecurso(TipoRecurso tipo, int cantidad)
        {
            switch (tipo)
            {
                case TipoRecurso.Oro: JugadorLocal.Recibir(0, cantidad, 0); break;
                case TipoRecurso.Madera: JugadorLocal.Recibir(cantidad, 0, 0); break;
                case TipoRecurso.Comida: JugadorLocal.Recibir(0, 0, cantidad); break;
            }
        }

        // 5. Atacar (valida rango y usa la defensa del Modelo).
        public bool Atacar(Unidad atacante, Unidad enemigo)
        {
            lock (Candado)
            {
                if (atacante == null || enemigo == null) return false;
                if (!atacante.PuedeAtacar || !enemigo.EstaViva) return false;

                int distancia = Math.Abs(atacante.PosicionX - enemigo.PosicionX)
                              + Math.Abs(atacante.PosicionY - enemigo.PosicionY);
                if (distancia > atacante.RangoAtaque) return false; // fuera de alcance

                atacante.Estado = EstadoUnidad.Atacando;

                // [Items] Ataque con posible Espada (AtaqueTotal) y defensa con el
                // posible Casco del defensor. El daño se calcula UNA vez y se manda:
                // cada copia restará exactamente lo mismo (RecibirGolpe = daño plano).
                int danoReal = Math.Max(0, atacante.AtaqueTotal -
                    (enemigo.Defensa + (JugadorEnemigo.Unidades.Contains(enemigo)
                        ? JugadorEnemigo.DefensaBonus : JugadorLocal.DefensaBonus)));
                enemigo.RecibirGolpe(danoReal);

                // El Modelo avisa del ataque (con su daño ya calculado) para la red.
                ParaTransmitir?.Invoke(
                    $"ATACAR;{atacante.PosicionX};{atacante.PosicionY};{enemigo.PosicionX};{enemigo.PosicionY};{danoReal}");
                GestorArchivos.RegistrarAccion(
                    JugadorLocal.Nombre,
                    "Ataque",
                    $"{atacante.Tipo} infligió {danoReal} de daño a {enemigo.Tipo}.");

                if (!enemigo.EstaViva)
                {
                    JugadorEnemigo.EliminarUnidad(enemigo);
                    GestorArchivos.RegistrarAccion(
                        JugadorLocal.Nombre,
                        "Ataque",
                        $"Impacto - {enemigo.Tipo} enemigo destruido");
                    VerificarGanador();
                }
                return true;
            }
        }

        // 5b. Atacar Edificio (permite destruir estructuras y ganar por Centro Urbano).
        public bool AtacarEdificio(Unidad atacante, Edificio edificioEnemigo)
        {
            lock (Candado)
            {
                if (atacante == null || edificioEnemigo == null) return false;
                if (!atacante.PuedeAtacar || !edificioEnemigo.EstaViva) return false;
                if (!JugadorEnemigo.Edificios.Contains(edificioEnemigo)) return false;

                int distancia = Math.Abs(atacante.PosicionX - edificioEnemigo.PosicionX)
                              + Math.Abs(atacante.PosicionY - edificioEnemigo.PosicionY);
                if (distancia > atacante.RangoAtaque) return false;

                atacante.Estado = EstadoUnidad.Atacando;
                // [Items] El ataque suma la Espada (AtaqueTotal) si va equipada.
                edificioEnemigo.RecibirDano(atacante.AtaqueTotal);

                // Se envía el ATAQUE (no el daño final): el rival aplica la MISMA fórmula
                // con su copia del edificio y los dos lados coinciden.
                ParaTransmitir?.Invoke($"ATACAR_EDIFICIO;{atacante.PosicionX};{atacante.PosicionY};" +
                             $"{edificioEnemigo.PosicionX};{edificioEnemigo.PosicionY};{atacante.AtaqueTotal}");

                GestorArchivos.RegistrarAccion(JugadorLocal.Nombre, "Ataque",
                    $"{atacante.Tipo} atacó {edificioEnemigo.Tipo} (vida restante {edificioEnemigo.Vida}).");

                if (!edificioEnemigo.EstaViva)
                {
                    JugadorEnemigo.EliminarEdificio(edificioEnemigo);
                    GestorArchivos.RegistrarAccion(JugadorLocal.Nombre, "Ataque",
                        $"{edificioEnemigo.Tipo} enemigo destruido.");
                    VerificarGanador();
                }
                return true;
            }
        }

        // ============ VERIFICACIÓN DE GANADOR (revisa a AMBOS jugadores) ============

        public void VerificarGanador()
        {
            lock (Candado)
            {
                if (JugadorDerrotado(JugadorEnemigo))
                {
                    FinalizarPartida(JugadorLocal);
                }
                else if (JugadorDerrotado(JugadorLocal))
                {
                    FinalizarPartida(JugadorEnemigo);
                }
            }
        }

        private bool JugadorDerrotado(Jugador jugador)
        {
            bool sinCentroUrbano = !jugador.Edificios.Exists(
                e => e.Tipo == TipoEdificio.CentroUrbano && e.Vida > 0);
            bool sinUnidades = jugador.Unidades.Count == 0;
            return sinCentroUrbano || sinUnidades;
        }

        private void FinalizarPartida(Jugador ganador)
        {
            EstadoPartida.Finalizar(ganador.Nombre);
            GestorArchivos.RegistrarAccion(ganador.Nombre, "Victoria", "Partida terminada.");
            GestorArchivos.GuardarResultadoFinal($"¡Ganador: {ganador.Nombre}!");
        }

        // ============ ITEMS (objetos del mapa, generados por concurrencia) ============

        // [Concurrencia] SPAWNER DE ITEMS: solo el host corre este Task. Cada
        // IntervaloSpawnerMs siembra un item en una casilla libre y su evento anuncia
        // por red (ITEM;...) para que el espejo del cliente vea lo mismo. El mapa
        // cambia SOLO, sin que nadie lo ordene.
        private async Task IniciarSpawnerItemsAsync()
        {
            while (!_ctsSpawner.IsCancellationRequested)
            {
                try { await Task.Delay(IntervaloSpawnerMs, _ctsSpawner.Token); }
                catch (TaskCanceledException) { break; } // Cancelado (fin de partida/aplicación).

                lock (Candado)
                {
                    if (!EstadoPartida.EnEjecucion) continue;
                    (int X, int Y)? casilla = ElegirCasillaLibreParaItem();
                    if (!casilla.HasValue) continue; // sin hueco, se espera al próximo latido
                    TipoItem tipo = (TipoItem)_rng.Next(0, 4); // uno de los 4 al azar
                    if (ColocarItem(tipo, casilla.Value.X, casilla.Value.Y))
                        ParaTransmitir?.Invoke($"ITEM;{tipo};{casilla.Value.X};{casilla.Value.Y}");
                }
            }
        }

        // Busca 80 casillas al azar hasta encontrar una libre (sin unidad, edificio,
        // yacimiento ni otro item). Devuelve null si el mapa está lleno.
        private (int X, int Y)? ElegirCasillaLibreParaItem()
        {
            for (int intentos = 0; intentos < 80; intentos++)
            {
                int x = _rng.Next(0, Mapa.Ancho);
                int y = _rng.Next(0, Mapa.Alto);
                if (Tablero.EsCasillaLibre(x, y, JugadorLocal, JugadorEnemigo) &&
                    !Tablero.CasillaTieneRecurso(x, y) &&
                    !_itemsGlobales.Any(i => i.PosicionX == x && i.PosicionY == y))
                    return (x, y);
            }
            return null;
        }

        // Pone un item en este mundo (el host lo siembra desde su spawner; la red lo
        // replica con ColocarItemRival). Devuelve true si la casilla quedó ocupada.
        public bool ColocarItem(TipoItem tipo, int x, int y)
        {
            lock (Candado)
            {
                if (!EstadoPartida.EnEjecucion) return false;
                if (!Tablero.EsCoordenadaValida(x, y)) return false;
                if (!Tablero.EsCasillaLibre(x, y, JugadorLocal, JugadorEnemigo)) return false;
                if (Tablero.CasillaTieneRecurso(x, y)) return false;
                if (_itemsGlobales.Any(i => i.PosicionX == x && i.PosicionY == y)) return false;

                _itemsGlobales.Add(new Item(tipo, x, y));
                return true;
            }
        }

        // Una unidad adyacente al item lo recoge. Solo se aplica el efecto en la
        // máquina del dueño; el rival solo refleja (elimina su copia + Casco espejo).
        public bool RecogerItem(Unidad unidad, Item item)
        {
            lock (Candado)
            {
                if (unidad == null || item == null || !EstadoPartida.EnEjecucion) return false;
                if (item.Recogido || !_itemsGlobales.Contains(item)) return false; // (idempotencia)

                int dx = Math.Abs(unidad.PosicionX - item.PosicionX);
                int dy = Math.Abs(unidad.PosicionY - item.PosicionY);
                if (dx > 1 || dy > 1) return false; // hay que estar adyacente, como el yacimiento

                item.Recogido = true;
                _itemsGlobales.Remove(item);

                Jugador dueno = JugadorLocal.Unidades.Contains(unidad) ? JugadorLocal : JugadorEnemigo;
                AplicarEfectoItem(item, unidad, dueno);

                GestorArchivos.RegistrarAccion(dueno.Nombre, "Item",
                    $"{unidad.Tipo} recogió {DatosDelJuego.NombreDe(item.Tipo)} en ({item.PosicionX},{item.PosicionY}).");
                return true;
            }
        }

        // Efecto REAL del item para el dueño (corre dentro de lock(Candado)).
        private void AplicarEfectoItem(Item item, Unidad unidad, Jugador dueno)
        {
            switch (item.Tipo)
            {
                case TipoItem.Yogur:          // consumible: cura a todas las tropas griegas (por ahora todas)
                    foreach (Unidad u in dueno.Unidades)
                        if (u.EstaViva) u.Curarse(DatosDelJuego.CuraYogur);
                    break;

                case TipoItem.Casco:          // temporal: +defensa; otro Task lo quita a los X s
                    dueno.DefensaBonus += DatosDelJuego.BonoDefensaCasco;
                    _ = ExpiracionCascoAsync(dueno.Nombre);
                    break;

                case TipoItem.Espada:         // equipable: la lleva esa unidad (AtaqueTotal lo suma)
                    unidad.Equipado = item;
                    break;

                case TipoItem.Herramientas:   // pasivo: +5% de recolección para siempre
                    dueno.BonusRecoleccion += DatosDelJuego.BonusRecoleccionHerramientas;
                    break;
            }
            dueno.ItemsRecogidos++;
        }

        // Espejo del rival: solo replica lo que afecta CÁLCULOS COMPARTIDOS (la
        // defensa del Casco, que el atacante usa al calcular daño). El yogur, la
        // espada y las herramientas son efectos locales del dueño.
        private void AplicarEfectoItemEspejo(Item item)
        {
            if (item.Tipo == TipoItem.Casco)
            {
                JugadorEnemigo.DefensaBonus += DatosDelJuego.BonoDefensaCasco;
                _ = ExpiracionCascoAsync(JugadorEnemigo.Nombre);
            }
        }

        // [Concurrencia] El Casco es TEMPORAL: este Task lo apaga a los X segundos.
        private async Task ExpiracionCascoAsync(string nombreJugador)
        {
            await Task.Delay(DuracionCascoSegundos * 1000);
            lock (Candado)
            {
                Jugador jug = JugadorLocal.Nombre == nombreJugador ? JugadorLocal : JugadorEnemigo;
                if (jug != null) jug.DefensaBonus = Math.Max(0, jug.DefensaBonus - DatosDelJuego.BonoDefensaCasco);
            }
            GestorArchivos.RegistrarAccion(nombreJugador, "Item", "El Casco dejó de hacer efecto (defensa normal).");
        }

        // ============ RELOJ DEL JUEGO (RTS en TIEMPO REAL, sin turnos) ============

        // [Concurrencia] Bucle de fondo: espera 1 segundo real y suma 1 al marcador
        // de la partida. Cada instancia (host y cliente) corre SU propio reloj, igual
        // que corre su propia simulación; la red solo intercambia las acciones.
        private async Task IniciarRelojAsync()
        {
            while (!_ctsReloj.IsCancellationRequested)
            {
                try { await Task.Delay(RelojTickMs, _ctsReloj.Token); }
                catch (TaskCanceledException) { break; } // Cancelado (fin de partida/aplicación).

                lock (Candado)
                {
                    if (EstadoPartida.EnEjecucion)
                        EstadoPartida.TiempoJuegoSegundos++;
                }
            }
        }

        // ============ ESPEJO DE LA RED (el motor refleja la copia del rival) ============
        // Estos métodos NO avisan por red: los invoca el Controlador al RECIBIR un
        // mensaje, para aplicar en esta máquina lo que hizo el rival en la suya.

        public void EstablecerNombreRival(string nombre)
        {
            lock (Candado)
            {
                JugadorEnemigo.Nombre = nombre;
            }
        }

        public Unidad MoverUnidadRival(int origenX, int origenY, int nuevoX, int nuevoY)
        {
            lock (Candado)
            {
                Unidad unidad = JugadorEnemigo.Unidades.FirstOrDefault(
                    u => u.PosicionX == origenX && u.PosicionY == origenY);
                if (unidad == null || !unidad.EstaViva) return null;
                if (!Tablero.EsCoordenadaValida(nuevoX, nuevoY)) return null;
                if (!Tablero.EsCasillaLibre(nuevoX, nuevoY, JugadorLocal, JugadorEnemigo)) return null;

                unidad.MoverA(nuevoX, nuevoY);
                return unidad;
            }
        }

        public Unidad AplicarAtaqueEnUnidadLocal(int ax, int ay, int bx, int by, int dano)
        {
            lock (Candado)
            {
                Unidad objetivo = JugadorLocal.Unidades.FirstOrDefault(
                    u => u.PosicionX == bx && u.PosicionY == by);
                if (objetivo == null || !objetivo.EstaViva) return null;

                objetivo.RecibirGolpe(dano); // daño plano (ya calculado en el otro lado)

                if (!objetivo.EstaViva)
                {
                    JugadorLocal.EliminarUnidad(objetivo);
                    VerificarGanador();
                }
                return objetivo;
            }
        }

        public Edificio AplicarAtaqueEnEdificioLocal(int ax, int ay, int bx, int by, int ataque)
        {
            lock (Candado)
            {
                Edificio objetivo = JugadorLocal.Edificios.FirstOrDefault(
                    e => e.PosicionX == bx && e.PosicionY == by);
                if (objetivo == null || !objetivo.EstaViva) return null;

                objetivo.RecibirDano(ataque); // misma fórmula de daño que en el lado del rival

                if (!objetivo.EstaViva)
                {
                    JugadorLocal.EliminarEdificio(objetivo);
                    VerificarGanador();
                }
                return objetivo;
            }
        }

        public bool CrearEdificioRival(TipoEdificio tipo, int x, int y)
        {
            lock (Candado)
            {
                if (!Tablero.EsCoordenadaValida(x, y)) return false;
                if (Tablero.CasillaTieneRecurso(x, y)) return false;
                if (!Tablero.EsCasillaLibre(x, y, JugadorLocal, JugadorEnemigo)) return false;

                Edificio espejo = DatosDelJuego.CrearEdificio(tipo, x, y);
                JugadorEnemigo.AgregarEdificio(espejo);
                _ = ConstruccionTaskAsync(espejo,
                    DatosDelJuego.EdificiosBase[tipo].TiempoConstruccionSegundos,
                    JugadorEnemigo.Nombre);
                return true;
            }
        }

        public bool CrearUnidadRival(TipoUnidad tipo, int x, int y)
        {
            lock (Candado)
            {
                if (!Tablero.EsCoordenadaValida(x, y)) return false;
                if (!Tablero.EsCasillaLibre(x, y, JugadorLocal, JugadorEnemigo)) return false;

                JugadorEnemigo.AgregarUnidad(DatosDelJuego.CrearUnidad(tipo, x, y));
                return true;
            }
        }

        public bool CambiarEstadoRecoleccionRival(int x, int y, bool activo)
        {
            lock (Candado)
            {
                Unidad enemigo = JugadorEnemigo.Unidades.FirstOrDefault(
                    u => u.PosicionX == x && u.PosicionY == y);
                if (enemigo == null || !enemigo.EstaViva || !enemigo.EsRecolector) return false;
                // Solo reflejamos el estado visual: NO se lanza otro bucle (eso ya pasa en el lado del rival).
                enemigo.Estado = activo ? EstadoUnidad.Recolectando : EstadoUnidad.Idle;
                return true;
            }
        }

        public bool ColocarItemRival(TipoItem tipo, int x, int y)
        {
            return ColocarItem(tipo, x, y);
        }

        public bool AplicarRecogidaRival(TipoItem tipo, int x, int y)
        {
            lock (Candado)
            {
                Item item = _itemsGlobales.FirstOrDefault(i => i.PosicionX == x && i.PosicionY == y);
                if (item == null) return false;          // (idempotencia) ya lo había quitado
                item.Recogido = true;
                _itemsGlobales.Remove(item);
                AplicarEfectoItemEspejo(item);
                return true;
            }
        }

        // ============ AYUDANTES ============

        private bool EstanAdyacentes(Unidad unidad, Recurso recurso)
        {
            int dx = Math.Abs(unidad.PosicionX - recurso.PosicionX);
            int dy = Math.Abs(unidad.PosicionY - recurso.PosicionY);
            return dx <= 1 && dy <= 1;
        }

        // Busca la casilla libre más cercana a un edificio (de ahí "sale" la unidad entrenada).
        private (int X, int Y) ObtenerPosicionDeSalida(Edificio edificio)
        {
            for (int radio = 1; radio < Mapa.Ancho; radio++)
            {
                for (int dx = -radio; dx <= radio; dx++)
                {
                    for (int dy = -radio; dy <= radio; dy++)
                    {
                        int x = edificio.PosicionX + dx;
                        int y = edificio.PosicionY + dy;
                        if (Tablero.EsCasillaLibre(x, y, JugadorLocal, JugadorEnemigo))
                            return (x, y);
                    }
                }
            }
            return (edificio.PosicionX, edificio.PosicionY);
        }
    }
}