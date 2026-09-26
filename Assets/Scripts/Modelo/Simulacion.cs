using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Modelo
{
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
    //      terminó de entrenar, un item que apareció): se ENCOLA en la cola de
    //      salida y el Controlador la drena desde el hilo principal (ver
    //      ProcesarMensajesRedPendientes). Encolar es no bloqueante: NUNCA se
    //      escribe a un socket dentro de lock(Candado).
    public class Simulacion
    {
        // [Concurrencia] Candado del MUNDO (lo posee el Modelo): toda mutación de
        // unidades, edificios, recursos o items pasa por aquí. Los Tasks de fondo y
        // las acciones del jugador se serializan con este candado.
        public readonly object Candado = new object();

        // [Concurrencia] COLA DE SALIDA: lo que el Modelo quiere anunciar por red.
        // Los Tasks de fondo ENCOLAN aquí (no bloqueante); el Controlador drena en
        // el hilo principal y hace el socket. Así una escritura TCP bloqueante nunca
        // se ejecuta estando tomado el candado del mundo.
        private readonly ConcurrentQueue<string> _salientes = new ConcurrentQueue<string>();

        public bool HaySalientes => !_salientes.IsEmpty;

        public string SiguienteSaliente()
        {
            if (_salientes.TryDequeue(out string mensaje)) return mensaje;
            return null;
        }

        private void Transmitir(string mensaje) => _salientes.Enqueue(mensaje);

        public Jugador JugadorLocal { get; private set; }
        public Jugador JugadorEnemigo { get; private set; }
        // Capital enemiga (su PRIMER Centro Urbano): el regicidio la tumba y
        // la partida termina aunque le queden tropas o bases menores.
        public Edificio CapitalEnemiga { get; private set; }
        public Mapa Tablero { get; private set; }
        public Partida EstadoPartida { get; private set; }
        // Facciones enemigas en juego, en orden de base (para rótulos y menú).
        public readonly List<string> FaccionesRivales = new List<string>();
        // Fauna neutral (ciervos): no es de ningún jugador; se caza por comida.
        public List<Unidad> Fauna { get; private set; } = new List<Unidad>();

        // ---- Items en el mapa (el spawner concurrente del host los siembra) ----
        private readonly bool _esHost;                 // Solo el host siembra items.
        private readonly Random _rng = new Random();
        private readonly CancellationTokenSource _ctsSpawner = new CancellationTokenSource();
        private readonly List<Item> _itemsGlobales = new List<Item>();

        // [Concurrencia] Economía pasiva: Casas y Centros operativos generan
        // solos (comida y oro). Se cancela con este token.
        private readonly CancellationTokenSource _ctsEconomia = new CancellationTokenSource();

        // La Vista lee esto (vía hilo principal) para pintar los items en el mapa.
        public IReadOnlyList<Item> ItemsVisibles
        {
            get
            {
                lock (Candado)
                {
                    return new List<Item>(_itemsGlobales);
                }
            }
        }

        // Trabajos en segundo plano activos: para poder cancelarlos.
        // [PVE] Entrenamientos clave por (jugador, tipo): la IA y el humano local
        // pueden entrenar el MISMO tipo a la vez sin pisarse.
        private readonly Dictionary<(Jugador, TipoUnidad), CancellationTokenSource> _entrenamientosActivos =
            new Dictionary<(Jugador, TipoUnidad), CancellationTokenSource>();
        private readonly Dictionary<Edificio, CancellationTokenSource> _construccionesActivas =
            new Dictionary<Edificio, CancellationTokenSource>();
        private readonly Dictionary<Unidad, CancellationTokenSource> _recolectoresActivos =
            new Dictionary<Unidad, CancellationTokenSource>();

        // [PVE] IA económica + militar del oponente (corre en SU Task, con lock(Candado)).
        private IAEnemiga _ia;

        // [Concurrencia] Reloj del juego (RTS en tiempo real): un Task de fondo que
        // suma 1 segundo por cada segundo real. Se cancela con este token.
        private readonly CancellationTokenSource _ctsReloj = new CancellationTokenSource();

        // [Concurrencia] Bucle de simulación (la "batalla"): el Task de fondo que
        // avanza a TODAS las unidades controladas por IA en cada latido.
        private readonly CancellationTokenSource _ctsSimulacion = new CancellationTokenSource();
        private readonly CancellationTokenSource _ctsEfectosTemporales = new CancellationTokenSource();
        // [Concurrencia] Fauna (ciervos que vagan): su propio CTS y su azar
        // dedicado (Random no es seguro entre hilos: nadie más lo toca).
        private readonly CancellationTokenSource _ctsFauna = new CancellationTokenSource();
        private readonly Random _rngFauna = new Random();
        private volatile bool _detenido;

        // == CONFIGURACIÓN (el Controlador/tests la ajusta; el Modelo la usa) ==
        // Cada cuántos ms late un ciclo de recolección (1000 = 1 segundo real).
        public int CicloRecoleccionMs { get; set; } = 1000;
        // Cada cuántos ms late el reloj (1000 = 1 segundo real).
        public int RelojTickMs { get; set; } = 1000;
        // Cada cuántos ms siembra el spawner un item (host).
        public int IntervaloSpawnerMs { get; set; } = 15000;
        // Máximo de items simultáneos en el mapa (evita la lluvia de cuadros amarillos).
        public int MaxItemsEnMapa { get; set; } = 8;
        // [PVE] Cada cuántos ms decide la IA enemiga (las pruebas lo bajan).
        public int IntervaloIaMs { get; set; } = 2000;
        // [PVE] Handicap de la IA: sus tropas hacen este % de daño (0.6 = 60%).
        // Solo afecta a golpes de unidades ControladaPorIA; tus tropas pegan
        // igual y el espejo de red transmite el daño ya calculado (sin tocar).
        public double FactorDanoIA { get; set; } = 0.6;
        // Reposición automática: al haber bajas, el Centro entrena un aldeano
        // solo (si hay hueco, fondos y quedan menos de 4). Vale para ambos.
        public bool ReposicionAldeanos { get; set; } = true;
        // [PVE] Gracia militar: con IA directora, sus tropas no INICIAN ataques
        // hasta este segundo (se preparan pero no pegan). Tus ataques valen
        // siempre. Sin IA (batalla de mentira) no aplica.
        public int GraciaMilitarSegundos { get; set; } = 180;
        // Cuántos SEGUNDOS dura el Casco antes de que su Task lo apague.
        public int DuracionCascoSegundos { get; set; } = 10;

        // == CONFIGURACIÓN DE LA BATALLA (concurrencia masiva — NIVEL 1) ==
        // Cada cuántos ms late el bucle de simulación (100 = 10 latidos por segundo).
        public int TickSimulacionMs { get; set; } = 100;
        // Cuántos latidos debe esperar una unidad antes de volver a atacar.
        public int TicksEntreAtaques { get; set; } = 5;

        // == ESTADO DE LA BATALLA (lo lee la Vista/el informe) ==
        // Lo enciende IniciarBatalla(); mientras esté en true el bucle trabaja.
        public volatile bool BucleActivo;
        public int TicksSimulados { get; private set; }
        public int BajasLocal { get; private set; }
        public int BajasEnemigo { get; private set; }

        // Cuántas unidades siguen vivas en total (para el HUD de la demo).
        public int UnidadesEnBatalla
        {
            get { lock (Candado) return JugadorLocal.Unidades.Count + JugadorEnemigo.Unidades.Count; }
        }
        // Delegados de espera: permiten acelerar el tiempo en pruebas (DemoRapida).
        public Func<int, CancellationToken, Task> EsperarEntrenamiento { get; set; } =
            (segundos, token) => Task.Delay(segundos * 1000, token);
        public Func<int, Task> EsperarConstruccion { get; set; } =
            segundos => Task.Delay(segundos * 1000);

        // [Concurrencia] Toda tarea de fondo queda observada: sus excepciones no se
        // pierden como tareas no observadas y el Modelo las deja registradas.
        private void IniciarTarea(Task tarea, string nombre)
        {
            tarea.ContinueWith(
                completada => GestorArchivos.RegistrarAccion(
                    "Sistema",
                    "Error de tarea",
                    $"{nombre}: {completada.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        public Simulacion(string nombreJugador, bool localArriba = true,
            int basesEnemigas = 1, bool enemigoAvanzado = false, bool jugadorAvanzado = false,
            RitmoPartida ritmo = RitmoPartida.Normal, bool inicioRico = false)
        {
            _esHost = localArriba;
            JugadorLocal = new Jugador(nombreJugador);
            JugadorEnemigo = new Jugador("Enemigo");
            Tablero = new Mapa();
            EstadoPartida = new Partida();

            // [Concurrencia] RTS en tiempo real: desde el arranque, un Task de fondo
            // marca los segundos de partida mientras esta siga en ejecución.
            IniciarTarea(IniciarRelojAsync(), "Reloj");

            // [Concurrencia] Bucle de simulación (combate masivo). Late desde el
            // arranque pero NO hace nada hasta que IniciarBatalla() encienda BucleActivo.
            IniciarTarea(IniciarBucleSimulacionAsync(), "Bucle de simulacion");

            // [Concurrencia] El host SIEMBRA items solos en el mapa; su Task anuncia
            // cada colocación por red para que el espejo del cliente vea lo mismo.
            if (_esHost)
                IniciarTarea(IniciarSpawnerItemsAsync(), "Spawner de items");

            // [Concurrencia] La fauna vaga sola (ciervos del bosque dan vueltas
            // cerca de casa; la Vista los desliza para que se vean correr).
            IniciarTarea(VagarFaunaAsync(), "Fauna");

            // [Concurrencia] Economía pasiva: cada 4 s las Casas dan 1 comida
            // y los Centros 1 oro (solo operativos, ambos jugadores).
            IniciarTarea(EconomiaPasivaAsync(), "Economia");

            // Cada instancia coloca a SU jugador en su lado. El host (localArriba = true)
            // vive arriba; el cliente (localArriba = false) vive abajo. Así las copias
            // de ambos mundos concuerdan y la red puede espejar movimientos por casilla.
            // ESCENARIO: 1 base local + N bases enemigas en los BORDES del mapa
            // (repartidas y separadas: centro enemigo, esquinas y laterales).
            // El mundo es IDÉNTICO en ambas máquinas (mismas coordenadas absolutas);
            // solo cambia qué lado es "local". Con N=1 queda el clásico de siempre.
            // Avanzado = Cuartel operativo + 2 soldados por base desde el minuto 0.
            int frentes = Math.Max(1, Math.Min(5, basesEnemigas));
            int centroX = Mapa.Ancho / 2;
            // El Centro ocupa 3x3: ancla en y=1 (arriba) o y=Alto-3 (abajo).
            int centroLocalY = localArriba ? 1 : Mapa.Alto - 3;
            int centroEnemigoY = localArriba ? Mapa.Alto - 3 : 1;
            // Aldeanos iniciales 3 casillas hacia el centro (no pegados al borde).
            int aldeanoLocalY = localArriba ? centroLocalY + 3 : centroLocalY - 3;

            Edificio centroLocal = DatosDelJuego.CrearCentroUrbano(centroX, centroLocalY);
            JugadorLocal.AgregarEdificio(centroLocal);
            JugadorLocal.AgregarUnidad(DatosDelJuego.CrearUnidad(TipoUnidad.Aldeano, centroX - 1, aldeanoLocalY));
            if (jugadorAvanzado) EquiparBaseAvanzada(JugadorLocal, false, centroX, centroLocalY);
            // Inicio rico: el jugador arranca con colchón de recursos, 2
            // aldeanos extra y una Casa operativa (si hay hueco). El enemigo no.
            if (inicioRico) DarInicioRico(JugadorLocal, centroX, centroLocalY, aldeanoLocalY);

            // Puestos enemigos en orden de dispersión; se valida hueco real.
            int[][] puestos = new int[][]
            {
                new int[] { 50, 97 }, new int[] { 8, 8 }, new int[] { 90, 8 },
                new int[] { 8, 50 }, new int[] { 90, 50 },
                new int[] { 30, 90 }, new int[] { 70, 90 }
            };
            int colocadas = 0;
            foreach (int[] p in puestos)
            {
                if (colocadas >= frentes) break;
                // El puesto 0 es el clásico (ancla del otro lado según la máquina).
                int px = colocadas == 0 ? centroX : p[0];
                int py = colocadas == 0 ? centroEnemigoY : p[1];
                if (!Tablero.EsAreaEdificable(px, py, 3, JugadorLocal, JugadorEnemigo)) continue;
                string faccion = DatosDelJuego.FaccionEnemiga(colocadas);
                Edificio centro = DatosDelJuego.CrearCentroUrbano(px, py);
                centro.Faccion = faccion;
                JugadorEnemigo.AgregarEdificio(centro);
                if (CapitalEnemiga == null) CapitalEnemiga = centro; // la primera es la capital
                FaccionesRivales.Add(faccion);
                if (colocadas == 0)
                {
                    int aldeanoY = localArriba ? py - 3 : py + 3;
                    JugadorEnemigo.AgregarUnidad(DatosDelJuego.CrearUnidad(TipoUnidad.Aldeano, px - 1, aldeanoY));
                }
                if (enemigoAvanzado) EquiparBaseAvanzada(JugadorEnemigo, true, px, py);
                colocadas++;
            }

            JugadorEnemigo.Nombre = FaccionesRivales.Count > 0
                ? string.Join(" + ", FaccionesRivales)
                : "Enemigo";

            // Fauna neutral: ciervos en los puestos de caza libres.
            foreach (var p in DatosDelJuego.PuestosCaza())
            {
                if (!Tablero.EsCasillaLibre(p.X, p.Y, JugadorLocal, JugadorEnemigo)) continue;
                if (Tablero.CasillaTieneRecurso(p.X, p.Y)) continue;
                Unidad ciervo = DatosDelJuego.CrearUnidad(TipoUnidad.Ciervo, p.X, p.Y);
                Fauna.Add(ciervo);
            }

            GestorArchivos.GuardarConfiguracionInicial(
                $"Jugador: {nombreJugador} | Mapa: {Mapa.Ancho}x{Mapa.Alto} | " +
                $"Centro local ({centroLocal.PosicionX},{centroLocal.PosicionY}) | " +
                $"Frentes enemigos: {frentes} (avanzado={enemigoAvanzado}) | Yo avanzado={jugadorAvanzado} | " +
                $"Ritmo={ritmo} | Inicio rico={inicioRico}");
            GestorArchivos.RegistrarAccion(nombreJugador, "Inicio", "Partida inicializada.");
            AplicarRitmo(ritmo);
        }

        // Ritmo de partida: Rápida = guerra a los 60 s e IA al 85% de daño;
        // Normal = valores clásicos (180 s, 60%); Larga = 7 min de paz e IA
        // al 45% para una partida épica. Se puede cambiar en caliente.
        public void AplicarRitmo(RitmoPartida ritmo)
        {
            lock (Candado)
            {
                switch (ritmo)
                {
                    case RitmoPartida.Rapida:
                        GraciaMilitarSegundos = 60;
                        FactorDanoIA = 0.85;
                        break;
                    case RitmoPartida.Larga:
                        GraciaMilitarSegundos = 420;
                        FactorDanoIA = 0.45;
                        break;
                    default:
                        GraciaMilitarSegundos = 180;
                        FactorDanoIA = 0.6;
                        break;
                }
                GestorArchivos.RegistrarAccion(JugadorLocal.Nombre, "Ritmo",
                    $"Ritmo {ritmo}: gracia {GraciaMilitarSegundos} s, daño IA {(int)(FactorDanoIA * 100)}%.");
            }
        }

        // Inicio rico: colchón de recursos + 2 aldeanos extra + Casa operativa
        // (mejor esfuerzo: si no hay hueco, solo recursos y aldeanos).
        private void DarInicioRico(Jugador dueno, int bx, int by, int aldeanoY)
        {
            dueno.Recibir(250, 150, 250, 50, 50);
            int[][] huecos = { new int[] { bx + 1, aldeanoY }, new int[] { bx - 2, aldeanoY } };
            foreach (int[] h in huecos)
            {
                if (!Tablero.EsCasillaLibre(h[0], h[1], JugadorLocal, JugadorEnemigo)) continue;
                if (Tablero.CasillaTieneRecurso(h[0], h[1])) continue;
                dueno.AgregarUnidad(DatosDelJuego.CrearUnidad(TipoUnidad.Aldeano, h[0], h[1]));
            }
            int[][] candidatos =
            {
                new int[] { bx + 5, by }, new int[] { bx - 6, by },
                new int[] { bx, by + 5 }, new int[] { bx, by - 5 }
            };
            foreach (int[] c in candidatos)
            {
                if (!Tablero.EsAreaEdificable(c[0], c[1], DatosDelJuego.LadoSegunTipo(TipoEdificio.Casa), JugadorLocal, JugadorEnemigo)) continue;
                dueno.AgregarEdificio(DatosDelJuego.CrearEdificio(TipoEdificio.Casa, c[0], c[1], operativo: true));
                break;
            }
            GestorArchivos.RegistrarAccion(dueno.Nombre, "Inicio",
                "Inicio rico: +recursos, aldeanos extra y Casa operativa.");
        }

        // Equipa una base avanzada: Cuartel operativo cercano + 2 soldados en
        // huecos libres (mejor esfuerzo: si no hay sitio, solo lo que quepa).
        private void EquiparBaseAvanzada(Jugador dueno, bool esEnemigo, int bx, int by)
        {
            int[][] candidatos =
            {
                new int[] { bx + 4, by }, new int[] { bx - 5, by },
                new int[] { bx, by + 4 }, new int[] { bx, by - 4 }
            };
            foreach (int[] c in candidatos)
            {
                if (!Tablero.EsAreaEdificable(c[0], c[1], DatosDelJuego.LadoSegunTipo(TipoEdificio.Cuartel), JugadorLocal, JugadorEnemigo)) continue;
                Edificio cuartel = DatosDelJuego.CrearEdificio(TipoEdificio.Cuartel, c[0], c[1], operativo: true);
                dueno.AgregarEdificio(cuartel);
                break;
            }
            int puestos = 0;
            for (int radio = 1; radio <= 4 && puestos < 2; radio++)
                for (int dx = -radio; dx <= radio && puestos < 2; dx++)
                    for (int dy = -radio; dy <= radio && puestos < 2; dy++)
                    {
                        if (Math.Abs(dx) != radio && Math.Abs(dy) != radio) continue;
                        int x = bx + dx, y = by + dy;
                        if (!Tablero.EsCasillaLibre(x, y, JugadorLocal, JugadorEnemigo)) continue;
                        if (Tablero.CasillaTieneRecurso(x, y)) continue;
                        Unidad s = DatosDelJuego.CrearUnidad(TipoUnidad.Soldado, x, y);
                        s.ControladaPorIA = esEnemigo;
                        dueno.AgregarUnidad(s);
                        puestos++;
                    }
        }

        // API PARA LA VISTA (Unity)

        // [Concurrencia] FOTO segura del mundo para dibujar. Copia las listas bajo el
        // candado, así la Vista itera sus propias copias sin chocar con los Tasks.
        // La Vista debe llamar esto UNA vez por frame y pintar desde el resultado.
        public InstantaneaJuego Instantanea()
        {
            lock (Candado)
            {
                return new InstantaneaJuego(
                    new List<Unidad>(JugadorLocal.Unidades),
                    new List<Unidad>(JugadorEnemigo.Unidades),
                    new List<Unidad>(Fauna),
                    new List<Edificio>(JugadorLocal.Edificios),
                    new List<Edificio>(JugadorEnemigo.Edificios),
                    new List<Recurso>(Tablero.RecursosEnMapa),
                    new List<Item>(_itemsGlobales),
                    JugadorLocal.Oro, JugadorLocal.Madera, JugadorLocal.Comida, JugadorLocal.Hierro, JugadorLocal.Piedra,
                    EstadoPartida.TiempoJuegoSegundos,
                    EstadoPartida.EnEjecucion,
                    EstadoPartida.GanadorNombre,
                    EstadoPartida.MotivoVictoria);
            }
        }

        // [PVE] Arranca la IA enemiga (económica + militar). Solo en modo local contra
        // la máquina; el modo red/PvP no la enciende. Seguro de llamar una sola vez.
        public bool IniciarIA()
        {
            lock (Candado)
            {
                if (_detenido || _ia != null) return false;
                _ia = new IAEnemiga(this) { IntervaloDecisionMs = IntervaloIaMs };
                _ia.Iniciar();
                // El bucle de batalla avanza las unidades con ControladaPorIA de la IA.
                BucleActivo = true;
                GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "IA",
                    "IA enemiga iniciada (economía + militar, concurrente).");
                return true;
            }
        }

        public bool IAActiva => _ia != null && _ia.Activa;

        // [PVE→Red] Apaga SOLO la IA (para pasar a PvP sin tumbar la partida).
        // No cancela reloj/spawner: el motor sigue corriendo.
        public bool DetenerIA()
        {
            lock (Candado)
            {
                if (_ia == null) return false;
                _ia.Detener();
                _ia = null;
                GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "IA",
                    "IA enemiga detenida (cambio a modo red/PvP).");
                return true;
            }
        }

        // [Concurrencia] APAGA el motor: cancela el reloj, el spawner, la IA y todos
        // los trabajos en curso (entrenamientos y recolecciones). La Vista/Unity debe
        // llamar esto al salir de la escena o del modo Play, para no dejar Tasks
        // corriendo por detrás ("partida fantasma"). Es seguro llamarlo varias veces.
        public void Detener()
        {
            _detenido = true;
            _ia?.Detener();
            _ctsReloj.Cancel();
            _ctsSpawner.Cancel();
            _ctsEconomia.Cancel();
            _ctsSimulacion.Cancel();
            _ctsEfectosTemporales.Cancel();
            _ctsFauna.Cancel();

            // [Concurrencia] Foto de los trabajos bajo el candado y Cancel FUERA:
            // Cancel() dispara callbacks síncronos y nunca debe correr con el
            // candado del mundo tomado (reentrada al iterar los diccionarios).
            List<CancellationTokenSource> trabajos;
            lock (Candado)
            {
                EstadoPartida.EnEjecucion = false;
                trabajos = new List<CancellationTokenSource>(
                    _entrenamientosActivos.Count +
                    _construccionesActivas.Count +
                    _recolectoresActivos.Count);
                trabajos.AddRange(_entrenamientosActivos.Values);
                trabajos.AddRange(_construccionesActivas.Values);
                trabajos.AddRange(_recolectoresActivos.Values);
                _entrenamientosActivos.Clear();
                _construccionesActivas.Clear();
                _recolectoresActivos.Clear();
            }
            foreach (CancellationTokenSource cts in trabajos)
                cts.Cancel();
        }

        // [Concurrencia] FAUNA: cada ~2 s cada ciervo vivo da un paso aleatorio
        // a una casilla vecina transitable y sin yacimiento (las unidades no
        // estorban). Todo bajo lock(Candado); la Vista los desliza al correr.
        private async Task VagarFaunaAsync()
        {
            while (!_ctsFauna.IsCancellationRequested)
            {
                try { await Task.Delay(1800 + _rngFauna.Next(0, 900), _ctsFauna.Token); }
                catch (TaskCanceledException) { break; }

                lock (Candado)
                {
                    if (_detenido || !EstadoPartida.EnEjecucion) continue;
                    foreach (Unidad ciervo in Fauna)
                    {
                        if (ciervo == null || !ciervo.EstaViva) continue;
                        if (_rngFauna.NextDouble() > 0.65) continue;
                        int nx = ciervo.PosicionX + _rngFauna.Next(-1, 2);
                        int ny = ciervo.PosicionY + _rngFauna.Next(-1, 2);
                        if (!Tablero.EsTransitable(nx, ny, JugadorLocal, JugadorEnemigo)) continue;
                        if (Tablero.CasillaTieneRecurso(nx, ny)) continue;
                        ciervo.MoverA(nx, ny);
                    }
                }
            }
        }

        // [Concurrencia] ECONOMÍA PASIVA: cada 4 s, cada Casa operativa da
        // 1 de comida y cada Centro Urbano operativo da 1 de oro a su dueño
        // (ambos jugadores por igual). Todo bajo lock(Candado).
        private async Task EconomiaPasivaAsync()
        {
            while (!_ctsEconomia.IsCancellationRequested)
            {
                try { await Task.Delay(4000, _ctsEconomia.Token); }
                catch (TaskCanceledException) { break; }

                lock (Candado)
                {
                    if (_detenido || !EstadoPartida.EnEjecucion) continue;
                    foreach (Jugador dueno in new Jugador[] { JugadorLocal, JugadorEnemigo })
                    {
                        foreach (Edificio e in dueno.Edificios)
                        {
                            if (!e.EstaOperativo) continue;
                            if (e.Tipo == TipoEdificio.Casa) dueno.Recibir(0, 0, 1, 0, 0);
                            else if (e.Tipo == TipoEdificio.CentroUrbano) dueno.Recibir(0, 1, 0, 0, 0);
                        }
                    }
                }
            }
        }

        // ACCIONES DEL JUGADOR (todas con lock(Candado) interno)

        // 1. Mover Unidad
        public bool MoverUnidad(Unidad unidad, int nuevoX, int nuevoY) =>
            MoverUnidadPara(JugadorLocal, unidad, nuevoX, nuevoY);

        // [PVE] La IA mueve sus unidades con las mismas validaciones de casilla.
        public bool MoverUnidadIA(Unidad unidad, int nuevoX, int nuevoY) =>
            MoverUnidadPara(JugadorEnemigo, unidad, nuevoX, nuevoY);

        private bool MoverUnidadPara(Jugador dueno, Unidad unidad, int nuevoX, int nuevoY)
        {
            lock (Candado)
            {
                if (_detenido) return false;
                if (unidad == null) return false;
                if (!dueno.Unidades.Contains(unidad)) return false;
                if (!Tablero.EsCoordenadaValida(nuevoX, nuevoY)) return false;

                int origenX = unidad.PosicionX;
                int origenY = unidad.PosicionY;

                // Ya está en la casilla pedida: no hay nada que caminar.
                if (origenX == nuevoX && origenY == nuevoY) return true;
                // Destino transitable: solo un EDIFICIO bloquea; otras unidades
                // no impiden llegar (pueden compartir casilla al llegar).
                if (!Tablero.EsTransitable(nuevoX, nuevoY, JugadorLocal, JugadorEnemigo)) return false;

                // Nueva orden de movimiento: corta el viaje anterior, la recolección
                // y cualquier objetivo de combate en marcha.
                CortarRecoleccionYAnunciar(unidad, dueno);
                unidad.LimpiarDestino();
                unidad.Objetivo = null;
                unidad.FijarDestino(nuevoX, nuevoY);
                unidad.Estado = EstadoUnidad.Moviendo;

                GestorArchivos.RegistrarAccion(
                    dueno.Nombre,
                    "Mover",
                    $"{unidad.Tipo} de ({origenX},{origenY}) camina a ({nuevoX},{nuevoY})");
                return true;
            }
        }

        // [Movimiento/API] Corta la recolección en marcha (si la hay) y, si era del
        // jugador local, anuncia por red para que la copia rival deje de mostrar
        // "Recolectando" mientras la unidad camina a otro lado.
        private void CortarRecoleccionYAnunciar(Unidad unidad, Jugador dueno)
        {
            if (DetenerRecoleccion(unidad) && dueno == JugadorLocal)
                Transmitir($"RECOLECTAR;{unidad.PosicionX};{unidad.PosicionY};0");
        }

        // [Movimiento] Detiene el caminar de una unidad (y la acción pendiente al
        // llegar). Escape / órdenes manuales del jugador; también usable en pruebas.
        public bool CancelarDestino(Unidad unidad)
        {
            lock (Candado)
            {
                if (unidad == null) return false;
                if (!unidad.TieneDestino && unidad.ItemAlLlegar == null && unidad.RecursoAlLlegar == null)
                    return false;
                unidad.LimpiarDestino();
                unidad.Objetivo = null;
                if (unidad.Estado == EstadoUnidad.Moviendo) unidad.Estado = EstadoUnidad.Idle;
                GestorArchivos.RegistrarAccion(JugadorLocal.Nombre, "Mover",
                    $"Viaje de {unidad.Tipo} cancelado en ({unidad.PosicionX},{unidad.PosicionY}).");
                return true;
            }
        }

        // [Movimiento] El aldeano camina hacia un yacimiento: si ya está adyacente
        // inicia la recolección ya; si no, fija el destino a una casilla junto al
        // recurso y la inicia al llegar (ver LlegarADestino).
        public bool MoverARecolectar(Unidad aldeano, Recurso recurso) =>
            MoverARecolectarPara(aldeano, recurso, JugadorLocal);

        // [PVE/IA] El aldeano de la IA viaja SOLO hasta el yacimiento (en vez de
        // dar un paso cada 2 s): el bucle lo camina celda a celda y al llegar
        // LlegarADestino lanza la recolección.
        public bool MoverARecolectarIA(Unidad aldeano, Recurso recurso) =>
            MoverARecolectarPara(aldeano, recurso, JugadorEnemigo);

        private bool MoverARecolectarPara(Unidad aldeano, Recurso recurso, Jugador dueno)
        {
            lock (Candado)
            {
                if (_detenido || aldeano == null || recurso == null || !EstadoPartida.EnEjecucion) return false;
                if (!dueno.Unidades.Contains(aldeano)) return false;
                if (!aldeano.EsRecolector || !aldeano.EstaViva) return false;
                if (recurso.EstaAgotado || !Tablero.RecursosEnMapa.Contains(recurso)) return false;

                if (EstanAdyacentes(aldeano, recurso))
                {
                    // Ya estaba recolectando (este u otro yacimiento): corta y
                    // reinicia. Antes IniciarRecoleccionPara devolvía false por
                    // "ya trabaja" y el jugador veía "No se pudo: recolectar".
                    CortarRecoleccionYAnunciar(aldeano, dueno);
                    aldeano.LimpiarDestino();
                    aldeano.Objetivo = null;
                    return IniciarRecoleccionPara(aldeano, recurso, dueno);
                }

                // El yacimiento está ocupado (casilla del recurso): el destino es la
                // casilla libre junto a él más cercana al aldeano.
                (int X, int Y)? casilla = CasillaLibreJuntoA(
                    recurso.PosicionX, recurso.PosicionY, aldeano);
                if (!casilla.HasValue)
                {
                    // Sin hueco al lado (tapado por edificios/otros yacimientos):
                    // la casilla del recurso ES transitable (solo los edificios
                    // bloquean), y estar encima sigue siendo "adyacente" → recolecta.
                    if (Tablero.EsTransitable(recurso.PosicionX, recurso.PosicionY,
                            JugadorLocal, JugadorEnemigo))
                        casilla = (recurso.PosicionX, recurso.PosicionY);
                    else
                        return false;
                }

                CortarRecoleccionYAnunciar(aldeano, dueno);
                aldeano.LimpiarDestino();
                aldeano.Objetivo = null;
                aldeano.FijarDestino(casilla.Value.X, casilla.Value.Y);
                aldeano.RecursoAlLlegar = recurso;
                aldeano.Estado = EstadoUnidad.Moviendo;

                GestorArchivos.RegistrarAccion(
                    dueno.Nombre,
                    "Recolectar",
                    $"{aldeano.Tipo} camina a ({casilla.Value.X},{casilla.Value.Y}) hacia {recurso.Tipo}.");
                return true;
            }
        }

        // 2. Construir Edificio (valida coordenadas, choque, yacimiento y costos del catálogo)
        public bool ConstruirEdificio(TipoEdificio tipo, int x, int y) =>
            ConstruirEdificioPara(JugadorLocal, tipo, x, y);

        // [PVE] La IA construye para SU jugador (gasta sus recursos, misma validación).
        public bool ConstruirEdificioIA(TipoEdificio tipo, int x, int y) =>
            ConstruirEdificioPara(JugadorEnemigo, tipo, x, y);

        private bool ConstruirEdificioPara(Jugador dueno, TipoEdificio tipo, int x, int y)
        {
            lock (Candado)
            {
                if (_detenido || !EstadoPartida.EnEjecucion) return false;
                // La huella (Lado x Lado) debe caber libre y sin yacimientos.
                int lado = DatosDelJuego.LadoSegunTipo(tipo);
                if (!Tablero.EsAreaEdificable(x, y, lado, JugadorLocal, JugadorEnemigo)) return false;

                EdificioConfig config = DatosDelJuego.EdificiosBase[tipo];
                if (!dueno.Gastar(config.CostoMadera, config.CostoOro, config.CostoComida, config.CostoHierro, config.CostoPiedra))
                    return false;

                Edificio nuevoEdificio = DatosDelJuego.CrearEdificio(tipo, x, y);
                dueno.AgregarEdificio(nuevoEdificio);

                // La obra avanza sola en segundo plano y completa el edificio.
                CancellationTokenSource cts = new CancellationTokenSource();
                _construccionesActivas[nuevoEdificio] = cts;
                IniciarTarea(ConstruccionTaskAsync(
                    nuevoEdificio,
                    config.TiempoConstruccionSegundos,
                    dueno.Nombre,
                    cts), "Construccion");

                GestorArchivos.RegistrarAccion(
                    dueno.Nombre,
                    "Construir",
                    $"{tipo} en ({x},{y}). Madera: {dueno.Madera}, Oro: {dueno.Oro}, Comida: {dueno.Comida}");
                return true;
            }
        }

        // 3. Entrenar Unidad: valida, cobra y lanza el "trabajo" en segundo plano.
        // [Concurrencia] La Task no bloquea al usuario: la unidad aparece al terminar.
        public bool EntrenarUnidad(TipoUnidad tipo, TipoEdificio edificioOrigen) =>
            EntrenarUnidadPara(JugadorLocal, tipo, edificioOrigen);

        // [PVE] La IA entrena para SU jugador (misma regla, sus recursos).
        public bool EntrenarUnidadIA(TipoUnidad tipo, TipoEdificio edificioOrigen) =>
            EntrenarUnidadPara(JugadorEnemigo, tipo, edificioOrigen);

        private bool EntrenarUnidadPara(Jugador dueno, TipoUnidad tipo, TipoEdificio edificioOrigen)
        {
            lock (Candado)
            {
                if (_detenido || !EstadoPartida.EnEjecucion) return false;
                Edificio edificio = dueno.Edificios.FirstOrDefault(e => e.Tipo == edificioOrigen);
                if (edificio == null || !edificio.PuedeEntrenar(tipo)) return false;
                // [PVE] Clave por (jugador, tipo): IA y humano no se pisan el slot.
                if (_entrenamientosActivos.ContainsKey((dueno, tipo))) return false;

                UnidadConfig config = DatosDelJuego.UnidadesBase[tipo];
                if (!dueno.Gastar(config.CostoMadera, config.CostoOro, config.CostoComida, config.CostoHierro, config.CostoPiedra))
                    return false;

                CancellationTokenSource cts = new CancellationTokenSource();
                _entrenamientosActivos[(dueno, tipo)] = cts;

                // Lanzamos la tarea y seguimos: no bloqueamos al usuario.
                IniciarTarea(EntrenamientoTaskAsync(tipo, config, dueno, cts.Token), "Entrenamiento");

                GestorArchivos.RegistrarAccion(
                    dueno.Nombre,
                    "Entrenar",
                    $"{tipo} iniciado en {edificioOrigen}.");
                return true;
            }
        }

        // [Concurrencia] La obra avanza sola en segundo plano y completa el edificio;
        // la misma Task se usa para el espejo del edificio rival en la otra máquina.
        private async Task ConstruccionTaskAsync(
            Edificio edificio,
            int segundos,
            string nombreDueno,
            CancellationTokenSource cts)
        {
            try
            {
                Task espera = EsperarConstruccion(segundos);
                Task cancelacion = Task.Delay(Timeout.Infinite, cts.Token);
                Task terminada = await Task.WhenAny(espera, cancelacion);
                await terminada;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (Candado)
            {
                _construccionesActivas.Remove(edificio);
                if (cts.IsCancellationRequested || !EstadoPartida.EnEjecucion)
                    return;

                edificio.CompletarConstruccion();
                GestorArchivos.RegistrarAccion(
                    nombreDueno,
                    "Construir",
                    $"{edificio.Tipo} en ({edificio.PosicionX},{edificio.PosicionY}) terminado.");
            }
        }

        private async Task EntrenamientoTaskAsync(TipoUnidad tipo, UnidadConfig config, Jugador dueno, CancellationToken token)
        {
            bool completado = true;
            try
            {
                await EsperarEntrenamiento(config.TiempoEntrenamientoSegundos, token);
            }
            catch (OperationCanceledException)
            {
                completado = false;
            }

            lock (Candado)
            {
                // Limpieza con guardia: solo quita el registro cuyo token es el suyo.
                if (_entrenamientosActivos.TryGetValue((dueno, tipo), out CancellationTokenSource ctsActual) &&
                    ctsActual.Token == token)
                {
                    _entrenamientosActivos.Remove((dueno, tipo));
                }
                if (!completado || token.IsCancellationRequested) return;

                Unidad nueva = DatosDelJuego.CrearUnidad(tipo, 0, 0);
                Edificio origen = dueno.Edificios.FirstOrDefault(
                    e => e.UnidadesEntrenables.Contains(tipo) && e.EstaOperativo);
                if (origen != null)
                {
                    (int x, int y) = ObtenerPosicionDeSalida(origen);
                    nueva.MoverA(x, y);
                }

                // [PVE] Las tropas de la IA pelean solas vía el bucle de simulación;
                // las del jugador local las controla la persona (false por defecto).
                if (dueno == JugadorEnemigo && !nueva.EsRecolector)
                    nueva.ControladaPorIA = true;

                dueno.AgregarUnidad(nueva);
                if (dueno == JugadorLocal)
                    Transmitir($"ENTRENAR;{tipo};{nueva.PosicionX};{nueva.PosicionY}");
                GestorArchivos.RegistrarAccion(
                    dueno.Nombre,
                    "Entrenar",
                    $"{tipo} listo en ({nueva.PosicionX},{nueva.PosicionY}).");
            }
        }

        // 4. Recolectar recursos en segundo plano (un hilo por aldeano).
        // [Concurrencia] Task de fondo + lock(Candado) + CancellationToken:
        // el aldeano trabaja, suma recursos y el jugador puede seguir jugando.
        public bool IniciarRecoleccion(Unidad aldeano, Recurso recurso) =>
            IniciarRecoleccionPara(aldeano, recurso, JugadorLocal);

        // [PVE] El aldeano de la IA recolecta para el enemigo (mismas reglas).
        public bool IniciarRecoleccionIA(Unidad aldeano, Recurso recurso) =>
            IniciarRecoleccionPara(aldeano, recurso, JugadorEnemigo);

        private bool IniciarRecoleccionPara(Unidad aldeano, Recurso recurso, Jugador dueno)
        {
            lock (Candado)
            {
                if (_detenido || !EstadoPartida.EnEjecucion) return false;
                if (aldeano == null || recurso == null) return false;
                if (!dueno.Unidades.Contains(aldeano)) return false;
                if (!aldeano.EsRecolector || !aldeano.EstaViva) return false;
                if (recurso.EstaAgotado) return false;
                if (!Tablero.RecursosEnMapa.Contains(recurso)) return false;
                if (_recolectoresActivos.ContainsKey(aldeano)) return false; // ya trabaja
                if (!EstanAdyacentes(aldeano, recurso)) return false;

                aldeano.Estado = EstadoUnidad.Recolectando;
                CancellationTokenSource cts = new CancellationTokenSource();
                _recolectoresActivos[aldeano] = cts;

                IniciarTarea(RecoleccionTaskAsync(aldeano, recurso, dueno, cts.Token), "Recoleccion");

                GestorArchivos.RegistrarAccion(
                    dueno.Nombre,
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
                    // [M3] Se quita AHORA del diccionario (no al terminar la Task):
                    // un nuevo IniciarRecoleccion sobre el mismo aldeano no debe
                    // fallar mientras la tarea vieja termina de desenrollarse.
                    _recolectoresActivos.Remove(aldeano);
                    aldeano.Estado = EstadoUnidad.Idle;
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

        // MERCADO (trueque con tasa, solo jugador local): vende 100 de un
        // recurso por 60 de oro, o compra 100 por 75 de oro. El oro no se vende.
        public bool VenderRecurso(TipoRecurso tipo)
        {
            lock (Candado)
            {
                if (_detenido || !EstadoPartida.EnEjecucion) return false;
                if (tipo == TipoRecurso.Oro) return false;
                if (!QuitarDe(JugadorLocal, tipo, 100)) return false;
                JugadorLocal.Recibir(0, 60, 0, 0, 0);
                GestorArchivos.RegistrarAccion(JugadorLocal.Nombre, "Mercado",
                    $"Vendió 100 de {tipo} por 60 de oro.");
                return true;
            }
        }

        public bool ComprarRecurso(TipoRecurso tipo)
        {
            lock (Candado)
            {
                if (_detenido || !EstadoPartida.EnEjecucion) return false;
                if (tipo == TipoRecurso.Oro) return false;
                if (!JugadorLocal.Gastar(0, 75, 0, 0, 0)) return false;
                DarA(JugadorLocal, tipo, 100);
                GestorArchivos.RegistrarAccion(JugadorLocal.Nombre, "Mercado",
                    $"Compró 100 de {tipo} por 75 de oro.");
                return true;
            }
        }

        private static int CantidadDe(Jugador j, TipoRecurso tipo)
        {
            switch (tipo)
            {
                case TipoRecurso.Madera: return j.Madera;
                case TipoRecurso.Oro: return j.Oro;
                case TipoRecurso.Comida: return j.Comida;
                case TipoRecurso.Hierro: return j.Hierro;
                default: return j.Piedra;
            }
        }

        private static bool QuitarDe(Jugador j, TipoRecurso tipo, int cantidad)
        {
            if (CantidadDe(j, tipo) < cantidad) return false;
            switch (tipo)
            {
                case TipoRecurso.Madera: j.Madera -= cantidad; break;
                case TipoRecurso.Oro: j.Oro -= cantidad; break;
                case TipoRecurso.Comida: j.Comida -= cantidad; break;
                case TipoRecurso.Hierro: j.Hierro -= cantidad; break;
                default: j.Piedra -= cantidad; break;
            }
            return true;
        }

        private static void DarA(Jugador j, TipoRecurso tipo, int cantidad)
        {
            switch (tipo)
            {
                case TipoRecurso.Madera: j.Recibir(cantidad, 0, 0, 0, 0); break;
                case TipoRecurso.Oro: j.Recibir(0, cantidad, 0, 0, 0); break;
                case TipoRecurso.Comida: j.Recibir(0, 0, cantidad, 0, 0); break;
                case TipoRecurso.Hierro: j.Recibir(0, 0, 0, cantidad, 0); break;
                default: j.Recibir(0, 0, 0, 0, cantidad); break;
            }
        }

        // HERRERÍA (solo jugador local): 3 ramas x 3 niveles. Ataque +1/tropa,
        // defensa +1 y recolección +10% por nivel. La IA no mejora.
        public bool MejorarAtaque() => Mejorar(0,
            () => { JugadorLocal.MejoraAtaque++; JugadorLocal.BonoAtaque++; });

        public bool MejorarDefensa() => Mejorar(1,
            () => { JugadorLocal.MejoraDefensa++; JugadorLocal.DefensaBonus++; });

        public bool MejorarRecoleccion() => Mejorar(2,
            () => { JugadorLocal.MejoraRecoleccion++; JugadorLocal.BonusRecoleccion += 0.10; });

        private bool Mejorar(int rama, System.Action aplicar)
        {
            lock (Candado)
            {
                if (_detenido || !EstadoPartida.EnEjecucion) return false;
                int nivel = rama == 0 ? JugadorLocal.MejoraAtaque
                    : rama == 1 ? JugadorLocal.MejoraDefensa : JugadorLocal.MejoraRecoleccion;
                if (nivel < 0 || nivel >= 3) return false;
                int m = DatosDelJuego.CostosMejora[rama][nivel, 0];
                int o = DatosDelJuego.CostosMejora[rama][nivel, 1];
                int c = DatosDelJuego.CostosMejora[rama][nivel, 2];
                int h = DatosDelJuego.CostosMejora[rama][nivel, 3];
                int p = DatosDelJuego.CostosMejora[rama][nivel, 4];
                if (!JugadorLocal.Gastar(m, o, c, h, p)) return false;
                aplicar();
                string[] nombres = { "Ataque", "Defensa", "Recolección" };
                GestorArchivos.RegistrarAccion(JugadorLocal.Nombre, "Herrería",
                    $"Mejora de {nombres[rama]} al nivel {nivel + 1}.");
                return true;
            }
        }

        private async Task RecoleccionTaskAsync(Unidad aldeano, Recurso recurso, Jugador dueno, CancellationToken token)
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
                    cantidad += (int)Math.Round(cantidad * dueno.BonusRecoleccion);

                    // [Biomas] Los yacimientos en arena/bosque rinden +50%.
                    if (DatosDelJuego.EnBioma(recurso.PosicionX, recurso.PosicionY))
                        cantidad += (int)Math.Round(cantidad * DatosDelJuego.BonusBioma);

                    EntregarRecurso(dueno, recurso.Tipo, cantidad);
                    GestorArchivos.RegistrarAccion(
                        dueno.Nombre,
                        "Recolectar",
                        $"+{cantidad} de {recurso.Tipo}. Total O:{dueno.Oro} M:{dueno.Madera} C:{dueno.Comida} H:{dueno.Hierro} P:{dueno.Piedra}");
                }
            }

            lock (Candado)
            {
                // [M3] Limpieza con guardia: si DetenerRecoleccion ya removió la
                // entrada o un NUEVO ciclo tomó al aldeano, esta tarea vieja no pisa
                // el estado ajeno. Solo modifica el registro cuyo token es el suyo.
                if (_recolectoresActivos.TryGetValue(aldeano, out CancellationTokenSource ctsActual) &&
                    ctsActual.Token == token)
                {
                    _recolectoresActivos.Remove(aldeano);
                    aldeano.Estado = EstadoUnidad.Idle;
                }
            }

            // Si terminó solo (yacimiento vacío o aldeano muerto), avisa al rival para
            // que su copia también vuelva a Idle. Si lo detuvieron, ya avisó el Controlador.
            if (terminoSolo && dueno == JugadorLocal)
            {
                lock (Candado)
                {
                    Transmitir($"RECOLECTAR;{aldeano.PosicionX};{aldeano.PosicionY};0");
                }
            }
        }

        private void EntregarRecurso(Jugador dueno, TipoRecurso tipo, int cantidad)
        {
            switch (tipo)
            {
                case TipoRecurso.Oro: dueno.Recibir(0, cantidad, 0, 0, 0); break;
                case TipoRecurso.Madera: dueno.Recibir(cantidad, 0, 0, 0, 0); break;
                case TipoRecurso.Comida: dueno.Recibir(0, 0, cantidad, 0, 0); break;
                case TipoRecurso.Hierro: dueno.Recibir(0, 0, 0, cantidad, 0); break;
                case TipoRecurso.Piedra: dueno.Recibir(0, 0, 0, 0, cantidad); break;
            }
        }

        // [Combate] El jugador ordena atacar (clic en enemigo). Si ya está en
        // rango, pega YA; si no, fija el objetivo y el destino a la casilla del
        // rival (con apilamiento puede compartir casilla) y camina solo hasta
        // que esté a golpe — así "clic en enemigo" siempre avanza y termina
        // pegando aunque no estuvieras en rango.
        public bool MoverAAtacar(Unidad atacante, Unidad enemigo)
        {
            lock (Candado)
            {
                if (_detenido || !EstadoPartida.EnEjecucion) return false;
                if (atacante == null || enemigo == null) return false;
                // La víctima puede ser rival o fauna neutral (ciervo).
                if (!JugadorLocal.Unidades.Contains(atacante) ||
                    (!JugadorEnemigo.Unidades.Contains(enemigo) && !Fauna.Contains(enemigo))) return false;
                // Los aldeanos también pueden cazar ciervos (solo a ellos).
                bool cazaAldeana = enemigo.Tipo == TipoUnidad.Ciervo
                    && atacante.EsRecolector && atacante.EstaViva;
                if (!atacante.PuedeAtacar && !cazaAldeana) return false;
                if (!enemigo.EstaViva) return false;

                // Fuera de rango (o enfriando): fija objetivo y DESTINO a la
                // casilla del enemigo (es transitable aunque haya otras unidades).
                if (Distancia(atacante, enemigo) > atacante.RangoAtaque ||
                    atacante.TiempoEsperaAtaque > 0)
                {
                    atacante.Objetivo = enemigo;
                    CortarRecoleccionYAnunciar(atacante, JugadorLocal);
                    atacante.LimpiarDestino();
                    atacante.FijarDestino(enemigo.PosicionX, enemigo.PosicionY);
                    atacante.Estado = EstadoUnidad.Moviendo;
                    GestorArchivos.RegistrarAccion(
                        JugadorLocal.Nombre, "Ataque",
                        $"{atacante.Tipo} camina hacia {enemigo.Tipo} ({enemigo.PosicionX},{enemigo.PosicionY}) para atacar.");
                    return true;
                }

                // En rango y listo: pega de inmediato (Atacar pone el enfriamiento).
                return Atacar(atacante, enemigo);
            }
        }

        // [Combate] Tras cada paso (o quietos), si hay objetivo vivo y estamos en
        // rango con el enfriamiento listo → pega. Corre SIEMPRE en el latido de
        // movimiento (no solo con BucleActivo), para que el clic del jugador
        // termine en golpes aunque no haya modo batalla encendido.
        private void ProcesarObjetivoDe(Unidad u, Jugador dueno)
        {
            Unidad obj = u.Objetivo;
            if (obj == null) return;
            if (!obj.EstaViva) { u.Objetivo = null; return; }
            // Los aldeanos también cazan (solo ciervos, lo demás lo rechaza Atacar).
            bool cazaAldeana = obj.Tipo == TipoUnidad.Ciervo && u.EsRecolector && u.EstaViva;
            if (!u.PuedeAtacar && !cazaAldeana) return;
            if (u.TiempoEsperaAtaque > 0) return;
            if (Distancia(u, obj) > u.RangoAtaque) return;

            if (dueno == JugadorLocal)
            {
                // Atacar pone enfriamiento y limpia el destino (sigue con Objetivo
                // para volver a pegar cuando se enfríe).
                Atacar(u, obj);
            }
            // Las unidades de IA peleen en ResolverTick (AplicarPendientes).
        }

        // 5. Atacar (valida rango y usa la defensa del Modelo).
        public bool Atacar(Unidad atacante, Unidad enemigo)
        {
            lock (Candado)
            {
                if (_detenido || !EstadoPartida.EnEjecucion) return false;
                if (atacante == null || enemigo == null) return false;
                // La víctima puede ser rival o fauna neutral (ciervo).
                bool esCaza = Fauna.Contains(enemigo);
                if (!JugadorLocal.Unidades.Contains(atacante) ||
                    (!JugadorEnemigo.Unidades.Contains(enemigo) && !esCaza)) return false;
                // Los aldeanos también pueden cazar ciervos (solo a ellos).
                bool cazaAldeana = esCaza && atacante.EsRecolector && atacante.EstaViva;
                if ((!atacante.PuedeAtacar && !cazaAldeana) || !enemigo.EstaViva) return false;

                int distancia = Math.Abs(atacante.PosicionX - enemigo.PosicionX)
                               + Math.Abs(atacante.PosicionY - enemigo.PosicionY);
                if (distancia > atacante.RangoAtaque) return false; // fuera de alcance

                atacante.LimpiarDestino(); // orden de combate: corta cualquier viaje
                atacante.Estado = EstadoUnidad.Atacando;
                atacante.TiempoEsperaAtaque = TicksEntreAtaques; // enfriamiento compartido
                // El objetivo sigue vivo para repetir golpes cuando se enfríe.

                // [Items] Ataque con posible Espada (AtaqueTotal), herrería
                // (BonoAtaque) y defensa con el posible Casco del defensor.
                // El daño se calcula UNA vez y se manda: cada copia restará
                // exactamente lo mismo (RecibirGolpe = daño plano).
                // La caza no tiene bonus de nadie; la red no se entera (fauna local).
                int bonus = esCaza ? 0 : (JugadorEnemigo.Unidades.Contains(enemigo)
                    ? JugadorEnemigo.DefensaBonus : JugadorLocal.DefensaBonus);
                int danoReal = Math.Max(0, atacante.AtaqueTotal + JugadorLocal.BonoAtaque - (enemigo.Defensa + bonus));
                enemigo.RecibirGolpe(danoReal);

                if (!esCaza)
                {
                    // El Modelo avisa del ataque (con su daño ya calculado) para la red.
                    Transmitir(
                        $"ATACAR;{atacante.PosicionX};{atacante.PosicionY};{enemigo.PosicionX};{enemigo.PosicionY};{danoReal}");
                }
                GestorArchivos.RegistrarAccion(
                    JugadorLocal.Nombre,
                    "Ataque",
                    $"{atacante.Tipo} infligió {danoReal} de daño a {enemigo.Tipo}.");

                // [Combate] Si el golpe mató al objetivo, se limpia para no
                // perseguir un cadáver.
                if (!enemigo.EstaViva && atacante.Objetivo == enemigo)
                    atacante.Objetivo = null;

                if (!enemigo.EstaViva)
                {
                    if (esCaza)
                    {
                        Fauna.Remove(enemigo);
                        JugadorLocal.Recibir(0, 0, 100, 0, 0);
                        GestorArchivos.RegistrarAccion(
                            JugadorLocal.Nombre,
                            "Caza",
                            "Ciervo cazado: +100 de comida.");
                    }
                    else
                    {
                        JugadorEnemigo.EliminarUnidad(enemigo);
                        GestorArchivos.RegistrarAccion(
                            JugadorLocal.Nombre,
                            "Ataque",
                            $"Impacto - {enemigo.Tipo} enemigo destruido");
                        VerificarGanador();
                    }
                }
                return true;
            }
        }

        // 5b. Atacar Edificio (permite destruir estructuras y ganar por Centro Urbano).
        public bool AtacarEdificio(Unidad atacante, Edificio edificioEnemigo)
        {
            lock (Candado)
            {
                if (_detenido || !EstadoPartida.EnEjecucion) return false;
                if (atacante == null || edificioEnemigo == null) return false;
                if (!JugadorLocal.Unidades.Contains(atacante)) return false;
                if (!atacante.PuedeAtacar || !edificioEnemigo.EstaViva) return false;
                if (!JugadorEnemigo.Edificios.Contains(edificioEnemigo)) return false;

                // Alcance a la HUELLA (no al ancla): pegar pegado a cualquier
                // casilla del edificio vale, aunque el ancla quede a 2.
                int distancia = DistanciaAEdificio(atacante, edificioEnemigo);
                if (distancia > atacante.RangoAtaque) return false;

                atacante.LimpiarDestino(); // orden de combate: corta cualquier viaje
                atacante.Estado = EstadoUnidad.Atacando;
                atacante.TiempoEsperaAtaque = TicksEntreAtaques;
                // [Items] El ataque suma la Espada (AtaqueTotal) y la herrería
                // (BonoAtaque) si van equipadas/mejoradas.
                edificioEnemigo.RecibirDano(atacante.AtaqueTotal + JugadorLocal.BonoAtaque);

                // Se envía el ATAQUE (no el daño final): el rival aplica la MISMA fórmula
                // con su copia del edificio y los dos lados coinciden.
                Transmitir($"ATACAR_EDIFICIO;{atacante.PosicionX};{atacante.PosicionY};" +
                             $"{edificioEnemigo.PosicionX};{edificioEnemigo.PosicionY};{atacante.AtaqueTotal + JugadorLocal.BonoAtaque}");

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

        // VERIFICACIÓN DE GANADOR (revisa a AMBOS jugadores)

        public void VerificarGanador()
        {
            lock (Candado)
            {
                if (!EstadoPartida.EnEjecucion) return; // ya hay ganador: no refinalizar
                string causaEnemigo = CausaDerrota(JugadorEnemigo, esEnemigo: true);
                if (causaEnemigo != null)
                {
                    FinalizarPartida(JugadorLocal, causaEnemigo);
                }
                else
                {
                    string causaLocal = CausaDerrota(JugadorLocal, esEnemigo: false);
                    if (causaLocal != null)
                        FinalizarPartida(JugadorEnemigo, causaLocal);
                }
            }
        }

        // REGICIDIO (asimétrico, porque la IA no asedia edificios):
        // · El ENEMIGO cae al perder su capital, aunque le queden tropas o
        //   bases menores (puede reponerse: hay que rematar la capital).
        // · El JUGADOR cae si pierde su Centro o se queda sin unidades.
        // Devuelve la causa o null si ese lado sigue vivo.
        private string CausaDerrota(Jugador jugador, bool esEnemigo)
        {
            if (esEnemigo)
            {
                if (CapitalEnemiga != null)
                    return CapitalEnemiga.EstaViva ? null : "¡Capital enemiga destruida!";
                bool sinCentro = !jugador.Edificios.Exists(
                    e => e.Tipo == TipoEdificio.CentroUrbano && e.Vida > 0);
                return sinCentro ? "¡Centro enemigo destruido!" : null;
            }
            bool sinCentroPropio = !jugador.Edificios.Exists(
                e => e.Tipo == TipoEdificio.CentroUrbano && e.Vida > 0);
            if (sinCentroPropio) return "¡Tu Centro Urbano ha caído!";
            if (jugador.Unidades.Count == 0) return "¡Tu ejército ha sido aniquilado!";
            return null;
        }

        private bool JugadorDerrotado(Jugador jugador) =>
            CausaDerrota(jugador, jugador == JugadorEnemigo) != null;

        private void FinalizarPartida(Jugador ganador, string causa)
        {
            EstadoPartida.Finalizar(ganador.Nombre, causa);
            GestorArchivos.RegistrarAccion(ganador.Nombre, "Victoria", $"Partida terminada. {causa}");
            GestorArchivos.GuardarResultadoFinal($"¡Ganador: {ganador.Nombre}! {causa}");
        }

        // ITEMS (objetos del mapa, generados por concurrencia)

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
                    if (_itemsGlobales.Count >= MaxItemsEnMapa) continue;
                    (int X, int Y)? casilla = ElegirCasillaLibreParaItem();
                    if (!casilla.HasValue) continue; // sin hueco, se espera al próximo latido
                    TipoItem tipo = (TipoItem)_rng.Next(0, 4); // uno de los 4 al azar
                    if (ColocarItem(tipo, casilla.Value.X, casilla.Value.Y))
                        Transmitir($"ITEM;{tipo};{casilla.Value.X};{casilla.Value.Y}");
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
                if (_detenido || !EstadoPartida.EnEjecucion) return false;
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
                if (_detenido || unidad == null || item == null || !EstadoPartida.EnEjecucion) return false;
                if (!JugadorLocal.Unidades.Contains(unidad)) return false;
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

        // [Movimiento] La unidad camina a recoger un item: si ya está adyacente lo
        // recoge ya; si no, fija el destino (la casilla del item, o una libre a su
        // lado) y lo recoge al llegar (ver LlegarADestino).
        public bool MoverARecogerItem(Unidad unidad, Item item)
        {
            lock (Candado)
            {
                if (_detenido || unidad == null || item == null || !EstadoPartida.EnEjecucion) return false;
                if (!JugadorLocal.Unidades.Contains(unidad)) return false;
                if (!unidad.EstaViva) return false;
                if (item.Recogido || !_itemsGlobales.Contains(item)) return false;

                int dx = Math.Abs(unidad.PosicionX - item.PosicionX);
                int dy = Math.Abs(unidad.PosicionY - item.PosicionY);
                if (dx <= 1 && dy <= 1)
                {
                    unidad.LimpiarDestino(); // nueva orden: corta cualquier viaje previo
                    return RecogerItem(unidad, item);
                }

                // Destino preferente: la casilla del item si es transitable
                // (otras unidades no la bloquean); si no, una libre a su lado.
                (int X, int Y)? casilla = null;
                if (Tablero.EsCoordenadaValida(item.PosicionX, item.PosicionY) &&
                    Tablero.EsTransitable(item.PosicionX, item.PosicionY, JugadorLocal, JugadorEnemigo))
                {
                    casilla = (item.PosicionX, item.PosicionY);
                }
                else
                {
                    casilla = CasillaLibreJuntoA(item.PosicionX, item.PosicionY, unidad);
                }
                if (!casilla.HasValue) return false;

                CortarRecoleccionYAnunciar(unidad, JugadorLocal);
                unidad.LimpiarDestino();
                unidad.Objetivo = null;
                unidad.FijarDestino(casilla.Value.X, casilla.Value.Y);
                unidad.ItemAlLlegar = item;
                unidad.Estado = EstadoUnidad.Moviendo;

                GestorArchivos.RegistrarAccion(
                    JugadorLocal.Nombre,
                    "Item",
                    $"{unidad.Tipo} camina a ({casilla.Value.X},{casilla.Value.Y}) a recoger {DatosDelJuego.NombreDe(item.Tipo)}.");
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
                    IniciarTarea(
                        ExpiracionCascoAsync(dueno.Nombre, _ctsEfectosTemporales.Token),
                        "Expiracion de Casco");
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
                IniciarTarea(
                    ExpiracionCascoAsync(JugadorEnemigo.Nombre, _ctsEfectosTemporales.Token),
                    "Expiracion de Casco espejo");
            }
        }

        // [Concurrencia] El Casco es TEMPORAL: este Task lo apaga a los X segundos.
        private async Task ExpiracionCascoAsync(string nombreJugador, CancellationToken token)
        {
            try
            {
                await Task.Delay(DuracionCascoSegundos * 1000, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            lock (Candado)
            {
                if (token.IsCancellationRequested) return;
                Jugador jug = JugadorLocal.Nombre == nombreJugador ? JugadorLocal : JugadorEnemigo;
                if (jug != null) jug.DefensaBonus = Math.Max(0, jug.DefensaBonus - DatosDelJuego.BonoDefensaCasco);
            }
            GestorArchivos.RegistrarAccion(nombreJugador, "Item", "El Casco dejó de hacer efecto (defensa normal).");
        }

        // RELOJ DEL JUEGO (RTS en TIEMPO REAL, sin turnos)

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

        // BATALLA MASIVA (concurrencia — NIVEL 1)
        // Un solo Task de fondo (el "bucle de simulación") late cada TickSimulacionMs
        // y avanza TODAS las unidades de IA dentro del candado del mundo. La
        // concurrencia es real (corre a la vez que el jugador, la red y la Vista),
        // pero el trabajo del latido ocurre en un único hilo → sin carreras.

        // [Concurrencia/Demo] Enciende el "modo batalla": siembra N unidades por lado
        // controladas por la IA y activa el bucle. Sirve para que la concurrencia SE
        // VEA en pantalla (cientos de unidades avanzando y combatiendo solas).
        // Devuelve cuántas unidades se pudieron colocar por lado.
        public int IniciarBatalla(int porLado)
        {
            lock (Candado)
            {
                if (_detenido || !EstadoPartida.EnEjecucion) return 0;

                int creadas = 0;
                for (int i = 0; i < porLado; i++)
                {
                    Unidad local = ColocarUnidadDeBatalla(JugadorLocal, i, ladoLocal: true);
                    Unidad rival = ColocarUnidadDeBatalla(JugadorEnemigo, i, ladoLocal: false);
                    if (local == null || rival == null)
                    {
                        // Sin hueco: deshacemos el par para no dejar ventaja a nadie.
                        if (local != null) JugadorLocal.EliminarUnidad(local);
                        if (rival != null) JugadorEnemigo.EliminarUnidad(rival);
                        break;
                    }
                    creadas++;
                }

                BucleActivo = creadas > 0;
                GestorArchivos.RegistrarAccion(JugadorLocal.Nombre, "Batalla",
                    $"Modo batalla: {creadas} unidades por lado (apilables). Concurrencia NIVEL 1.");
                return creadas;
            }
        }

        // Apaga el modo batalla (deja de latir, las unidades se quedan quietas).
        public void DetenerBatalla() => BucleActivo = false;

        private Unidad ColocarUnidadDeBatalla(Jugador dueno, int indice, bool ladoLocal)
        {
            // Cada jugador recibe su mitad del mapa: el host arriba, el cliente abajo.
            bool enMitadArriba = ladoLocal ? _esHost : !_esHost;
            int yInicio = enMitadArriba ? 0 : Mapa.Alto - 1;
            int pasoY = enMitadArriba ? 1 : -1;

            for (int fila = 0; fila < Mapa.Alto / 2; fila++)
            {
                int y = yInicio + pasoY * fila;
                for (int col = 0; col < Mapa.Ancho; col++)
                {
                    int x = (indice + col + fila) % Mapa.Ancho; // desfase para repartir
                    if (Tablero.CasillaTieneRecurso(x, y)) continue;
                    // El spawner pide casilla sin unidad (item limpio); el resto
                    // del juego SÍ permite apilar unidades.
                    if (!Tablero.EsCasillaLibre(x, y, JugadorLocal, JugadorEnemigo)) continue;

                    Unidad u = DatosDelJuego.CrearUnidad(TipoUnidad.Soldado, x, y);
                    u.ControladaPorIA = true;
                    dueno.AgregarUnidad(u);
                    return u;
                }
            }
            return null;
        }

        // [Concurrencia] Bucle de fondo de la batalla. Late siempre: avanza los
        // destinos (movimiento caminando) con o sin batalla; el combate solo corre
        // si IniciarBatalla()/IniciarIA() encendió BucleActivo.
        private async Task IniciarBucleSimulacionAsync()
        {
            while (!_ctsSimulacion.IsCancellationRequested)
            {
                try { await Task.Delay(TickSimulacionMs, _ctsSimulacion.Token); }
                catch (TaskCanceledException) { break; } // Cancelado (fin de partida/aplicación).

                bool enEjecucion;
                lock (Candado) { enEjecucion = EstadoPartida.EnEjecucion; }

                // Los destinos avanzan SIEMPRE que la partida siga viva (nadie se
                // teletransporta: cada latido las unidades dan UN paso celda a celda).
                if (enEjecucion) AvanzarDestinos();

                if (!BucleActivo) continue;
                if (!enEjecucion) { BucleActivo = false; break; }

                ResolverTick();
                TicksSimulados++;
            }
        }

        // [Movimiento] Cada latido, TODAS las unidades con destino dan un paso hacia
        // su casilla objetivo. Al llegar se ejecuta la acción pendiente (item o
        // yacimiento); si está bloqueada ~50 latidos, se rinde y cancela el viaje.
        private void AvanzarDestinos()
        {
            lock (Candado)
            {
                AvanzarDestinosDe(JugadorLocal);
                AvanzarDestinosDe(JugadorEnemigo);
            }
        }

        private void AvanzarDestinosDe(Jugador dueno)
        {
            // Por índice: la lista puede cambiar si una unidad muere en este latido.
            for (int i = 0; i < dueno.Unidades.Count; i++)
            {
                Unidad u = dueno.Unidades[i];
                if (u == null || !u.EstaViva) continue;

                // [Enfriamiento] Baja aunque no tenga destino (objetivo quieto).
                if (u.TiempoEsperaAtaque > 0) u.TiempoEsperaAtaque--;

                // [Combate] Unidad con objetivo: pegar si está en rango (aunque
                // no tenga destino de viaje). Enfriamiento incluido.
                if (u.Objetivo != null)
                    ProcesarObjetivoDe(u, dueno);

                // [Persecución] Con objetivo vivo fuera de rango, el destino es
                // SIEMPRE su casilla actual: el ciervo vaga y la casilla vieja
                // queda obsoleta (antes el aldeano llegaba, no pegaba y se
                // quedaba quieto para siempre). Solo unidades del jugador
                // dirigidas por él (!ControladaPorIA: la IA mueve lo suyo en
                // ResolverTick) y sin otra tarea pendiente al llegar.
                if (u.Objetivo != null && u.Objetivo.EstaViva && !u.ControladaPorIA
                    && u.ItemAlLlegar == null && u.RecursoAlLlegar == null
                    && (u.PuedeAtacar || (u.Objetivo.Tipo == TipoUnidad.Ciervo && u.EsRecolector))
                    && Distancia(u, u.Objetivo) > u.RangoAtaque
                    && (u.DestinoX != u.Objetivo.PosicionX || u.DestinoY != u.Objetivo.PosicionY))
                {
                    u.FijarDestino(u.Objetivo.PosicionX, u.Objetivo.PosicionY);
                    u.Estado = EstadoUnidad.Moviendo;
                }

                if (!u.TieneDestino) continue;

                if (u.PosicionX == u.DestinoX && u.PosicionY == u.DestinoY)
                {
                    LlegarADestino(u, dueno);
                    continue;
                }

                // [Movimiento] BFS: el paso de HOY sale del camino real hacia el
                // destino (esquiva edificios; las unidades se comparten casilla).
                // Sin camino transitable este latido → se acumula el contador de bloqueo.
                (int X, int Y)? siguiente = SiguientePasoBFS(u, u.DestinoX, u.DestinoY);
                bool dioPaso = siguiente.HasValue && IntentarPasoA(u, siguiente.Value.X, siguiente.Value.Y);

                if (dioPaso)
                {
                    u.TicksBloqueoDestino = 0;
                    u.Estado = EstadoUnidad.Moviendo;
                    if (u.PosicionX == u.DestinoX && u.PosicionY == u.DestinoY)
                        LlegarADestino(u, dueno);
                    else if (u.Objetivo != null)
                        ProcesarObjetivoDe(u, dueno); // acercó un paso: ¿ya en rango?
                }
                else
                {
                    // Camino tapado: si tarda demasiado, cancela el viaje (el
                    // pendiente se pierde, como en un RTS normal).
                    u.TicksBloqueoDestino++;
                    if (u.TicksBloqueoDestino > 50)
                    {
                        u.LimpiarDestino();
                        if (u.Estado == EstadoUnidad.Moviendo) u.Estado = EstadoUnidad.Idle;
                    }
                }
            }
        }

        // Da un paso de UNA casilla hacia (x,y) si es válida y transitable
        // (otras unidades no bloquean: se pueden apilarse en la misma casilla).
        private bool IntentarPasoA(Unidad u, int x, int y)
        {
            if (x == u.PosicionX && y == u.PosicionY) return false;
            if (!Tablero.EsTransitable(x, y, JugadorLocal, JugadorEnemigo)) return false;
            u.MoverA(x, y);
            return true;
        }

        // [Movimiento] BFS 4-direcciones: casilla siguiente del camino real desde
        // la unidad hasta (metaX,metaY), esquivando unidades y edificios (los
        // yacimientos se pueden cruzar, igual que siempre permitió MoverUnidad).
        // null = destino no transitable ahora (ocupado) o sin camino.
        private (int X, int Y)? SiguientePasoBFS(Unidad u, int metaX, int metaY)
        {
            int w = Mapa.Ancho, h = Mapa.Alto;
            if (!Tablero.EsCoordenadaValida(metaX, metaY)) return null;
            if (!Tablero.EsTransitable(metaX, metaY, JugadorLocal, JugadorEnemigo)) return null;

            int inicio = u.PosicionY * w + u.PosicionX;
            int meta = metaY * w + metaX;
            if (inicio == meta) return null;

            var padre = new int[w * h];          // -1 = libre, -2 = inicio
            for (int i = 0; i < padre.Length; i++) padre[i] = -1;
            padre[inicio] = -2;

            var cola = new Queue<int>();
            cola.Enqueue(inicio);

            bool encontrado = false;
            while (cola.Count > 0 && !encontrado)
            {
                int cur = cola.Dequeue();
                int cx = cur % w, cy = cur / w;

                ExplorarVecino(cx + 1, cy, cur);
                ExplorarVecino(cx - 1, cy, cur);
                ExplorarVecino(cx, cy + 1, cur);
                ExplorarVecino(cx, cy - 1, cur);

                void ExplorarVecino(int nx, int ny, int desde)
                {
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) return;
                    int idx = ny * w + nx;
                    if (idx == meta) { padre[idx] = desde; encontrado = true; return; }
                    if (padre[idx] != -1) return;
                    // Solo los EDIFICIOS cortan el paso; las unidades se esquivan
                    // "a través" (apilamiento): el BFS no se atasca en multitudes.
                    if (!Tablero.EsTransitable(nx, ny, JugadorLocal, JugadorEnemigo)) return;
                    padre[idx] = desde;
                    cola.Enqueue(idx);
                }
            }

            if (!encontrado) return null;

            // Retroceso meta → hijo del inicio: ese hijo es el primer paso.
            int p = meta;
            int guard = 0;
            while (padre[p] != inicio && guard++ < w * h) p = padre[p];
            return (p % w, p / w);
        }

        // La unidad llegó a su destino: ejecuta la acción pendiente y se queda quieta.
        private void LlegarADestino(Unidad u, Jugador dueno)
        {
            Item item = u.ItemAlLlegar;
            Recurso recurso = u.RecursoAlLlegar;
            u.LimpiarDestino(); // primero limpia (también los pendientes)

            // [Combate] Llegó a la casilla del objetivo (o ya estaba en rango):
            // pega de inmediato si puede (los aldeanos también cazan ciervos).
            bool cazaAlLlegar = u.Objetivo != null && u.Objetivo.EstaViva
                && u.Objetivo.Tipo == TipoUnidad.Ciervo && u.EsRecolector && u.EstaViva;
            if (u.Objetivo != null && u.Objetivo.EstaViva && (u.PuedeAtacar || cazaAlLlegar) &&
                Distancia(u, u.Objetivo) <= u.RangoAtaque && u.TiempoEsperaAtaque <= 0)
            {
                ProcesarObjetivoDe(u, dueno);
            }

            if (item != null)
            {
                // El viaje lo ordenó SIEMPRE el jugador local (MoverARecogerItem).
                if (dueno == JugadorLocal && RecogerItem(u, item))
                {
                    // Anuncia por red en el MISMO punto en que lo hacía el Controlador
                    // en el camino corto (la recogida ya ocurrió aquí, en background).
                    Transmitir($"RECOGER_ITEM;{item.Tipo};{item.PosicionX};{item.PosicionY}");
                }
                if (u.Estado == EstadoUnidad.Moviendo) u.Estado = EstadoUnidad.Idle;
                return;
            }

            if (recurso != null)
            {
                if (IniciarRecoleccionPara(u, recurso, dueno))
                {
                    if (dueno == JugadorLocal)
                        Transmitir($"RECOLECTAR;{u.PosicionX};{u.PosicionY};1");
                    return; // Recolectando
                }
                // Falló (agotado / ya ocupado): NO se queda en Moviendo.
            }

            // Viaje terminado (o falló la acción pendiente): si aún figura
            // caminando y no está atacando, vuelve a quieto para que la barra
            // deje de mostrar "Moviendo" en cuanto el aldeano acabe de caminar.
            if (u.Estado == EstadoUnidad.Moviendo)
                u.Estado = EstadoUnidad.Idle;
        }

        // Un latido del mundo: avanzar TODAS las unidades de IA y aplicar los golpes.
        private void ResolverTick()
        {
            var pendientes = new List<(Unidad victima, int dano)>();

            lock (Candado)
            {
                foreach (Unidad u in JugadorLocal.Unidades)
                    ProcesarUnidadTactica(u, JugadorEnemigo, JugadorLocal, pendientes);
                foreach (Unidad u in JugadorEnemigo.Unidades)
                    ProcesarUnidadTactica(u, JugadorLocal, JugadorEnemigo, pendientes);

                AplicarPendientes(pendientes);
            }
        }

        // Decide lo que hace UNA unidad este latido: buscar rival, acercarse o pegar.
        // Corre dentro del candado (Nivel 1), así que no hay condiciones de carrera.
        private void ProcesarUnidadTactica(
            Unidad u, Jugador rival, Jugador dueno,
            List<(Unidad victima, int dano)> pendientes)
        {
            if (u == null || !u.EstaViva || !u.PuedeAtacar) return;

            // Enfriamiento global de golpe (IA y jugador manual).
            if (u.TiempoEsperaAtaque > 0) u.TiempoEsperaAtaque--;

            // Las unidades del jugador SOLO pelean si el jugador lo ordenó
            // (Objetivo fijado por MoverAAtacar). Las de IA persiguen solas.
            if (!u.ControladaPorIA)
            {
                if (u.Objetivo == null || !u.Objetivo.EstaViva) return;
                // El golpe del jugador se procesa en ProcesarObjetivoDe/Atacar
                // (aquí solo acercamos si aún no está en rango).
                if (Distancia(u, u.Objetivo) <= u.RangoAtaque || u.TiempoEsperaAtaque > 0) return;
                if (u.TieneDestino) return; // ya camina hacia él
                u.FijarDestino(u.Objetivo.PosicionX, u.Objetivo.PosicionY);
                u.Estado = EstadoUnidad.Moviendo;
                return;
            }

            // Gracia PVE: con IA directora sus tropas no INICIAN ataques hasta
            // el minuto de gracia (se preparan pero no pegan). Sin IA (batalla
            // de mentira) no aplica. Tus ataques valen siempre.
            if (_ia != null && EstadoPartida.TiempoJuegoSegundos < GraciaMilitarSegundos)
            {
                u.Objetivo = null;
                return;
            }

            Unidad objetivo = EnemigoMasCercano(u, rival);
            // ASEDIO (solo con IA directora): sin tropas enemigas CERCA, la
            // máquina no se queda quieta: marcha sobre el Centro del rival y
            // lo golpea (con su handicap). Así el jugador SÍ puede perder por
            // su Centro y el regicidio vale en ambos sentidos. Las batallas de
            // mentira (_ia == null) siguen como siempre.
            if (_ia != null && (objetivo == null || Distancia(u, objetivo) > RadioAsedioIA))
            {
                AsediarCentro(u, rival, dueno);
                return;
            }
            if (objetivo == null) { u.Objetivo = null; return; }
            u.Objetivo = objetivo;

            if (Distancia(u, objetivo) <= u.RangoAtaque)
            {
                u.Estado = EstadoUnidad.Atacando;
                if (u.TiempoEsperaAtaque == 0)
                {
                    pendientes.Add((objetivo, CalcularDano(u, objetivo, dueno, rival)));
                    u.TiempoEsperaAtaque = TicksEntreAtaques;
                }
                return;
            }

            // Fuera de rango: se acerca una casilla (el daño se aplicará al final
            // del latido, para no mutar la vida de una lista mientras se recorre).
            u.Estado = EstadoUnidad.Moviendo;
            IntentarPaso(u, objetivo);
        }

        private Unidad EnemigoMasCercano(Unidad u, Jugador rival)
        {
            Unidad mejor = null;
            int mejorDist = int.MaxValue;
            List<Unidad> lista = rival.Unidades;
            for (int i = 0; i < lista.Count; i++) // índice, no enumerador (listas que cambian)
            {
                Unidad e = lista[i];
                if (!e.EstaViva) continue;
                int d = Distancia(u, e);
                if (d < mejorDist) { mejorDist = d; mejor = e; }
            }
            return mejor;
        }

        private static int Distancia(Unidad a, Unidad b) =>
            Math.Abs(a.PosicionX - b.PosicionX) + Math.Abs(a.PosicionY - b.PosicionY);

        // Manhattan a la casilla más cercana de la huella (0 si la pisa).
        private static int DistanciaAEdificio(Unidad u, Edificio e)
        {
            int dx = u.PosicionX < e.PosicionX ? e.PosicionX - u.PosicionX
                : u.PosicionX >= e.PosicionX + e.Lado ? u.PosicionX - (e.PosicionX + e.Lado - 1) : 0;
            int dy = u.PosicionY < e.PosicionY ? e.PosicionY - u.PosicionY
                : u.PosicionY >= e.PosicionY + e.Lado ? u.PosicionY - (e.PosicionY + e.Lado - 1) : 0;
            return dx + dy;
        }

        private int CalcularDano(Unidad atacante, Unidad defensor, Jugador duenoAtacante, Jugador duenoDefensor)
        {
            int bono = duenoAtacante == JugadorLocal ? duenoAtacante.BonoAtaque : 0;
            int baseDano = Math.Max(0, atacante.AtaqueTotal + bono - (defensor.Defensa + duenoDefensor.DefensaBonus));
            // Este método solo lo usa el bucle de la IA: handicap aplicado.
            if (atacante.ControladaPorIA)
                baseDano = (int)Math.Round(baseDano * FactorDanoIA);
            return baseDano;
        }

        // Radio (casillas) sin tropas enemigas a partir del cual la IA deja
        // de perseguir y ASEDIA el Centro rival. Ajustable (tests/PVE).
        public int RadioAsedioIA { get; set; } = 15;

        // Primer Centro Urbano vivo de ese jugador (objetivo del asedio).
        private static Edificio CentroVivoDe(Jugador dueno)
        {
            foreach (Edificio e in dueno.Edificios)
                if (e.Tipo == TipoEdificio.CentroUrbano && e.EstaViva) return e;
            return null;
        }

        // La unidad de la IA marcha sobre el Centro rival y lo golpea con su
        // handicap (misma fórmula que CalcularDano; RecibirDano quita la
        // coraza). Corre dentro del candado: daño directo, sin pendientes.
        private void AsediarCentro(Unidad u, Jugador rival, Jugador dueno)
        {
            u.Objetivo = null; // el objetivo es el edificio, no una unidad
            Edificio centro = CentroVivoDe(rival);
            if (centro == null) return;
            if (DistanciaAEdificio(u, centro) <= u.RangoAtaque)
            {
                u.Estado = EstadoUnidad.Atacando;
                if (u.TiempoEsperaAtaque > 0) return;
                int bono = dueno == JugadorLocal ? dueno.BonoAtaque : 0;
                int dano = (int)Math.Round(Math.Max(0, u.AtaqueTotal + bono) * FactorDanoIA);
                centro.RecibirDano(dano);
                u.TiempoEsperaAtaque = TicksEntreAtaques;
                GestorArchivos.RegistrarAccion(dueno.Nombre, "Asedio",
                    $"{u.Tipo} golpea el {centro.Tipo} rival (vida restante {centro.Vida}).");
                if (!centro.EstaViva)
                {
                    rival.EliminarEdificio(centro);
                    GestorArchivos.RegistrarAccion(dueno.Nombre, "Asedio", $"{centro.Tipo} rival destruido.");
                    VerificarGanador();
                }
                return;
            }
            u.Estado = EstadoUnidad.Moviendo;
            // Punto de la huella más cercano: la unidad se pega al edificio.
            int tx = u.PosicionX < centro.PosicionX ? centro.PosicionX
                : u.PosicionX >= centro.PosicionX + centro.Lado ? centro.PosicionX + centro.Lado - 1 : u.PosicionX;
            int ty = u.PosicionY < centro.PosicionY ? centro.PosicionY
                : u.PosicionY >= centro.PosicionY + centro.Lado ? centro.PosicionY + centro.Lado - 1 : u.PosicionY;
            IntentarPasoHacia(u, tx, ty);
        }

        // Da un paso de una casilla hacia el objetivo (primero el eje "más lejano").
        private void IntentarPaso(Unidad u, Unidad objetivo) =>
            IntentarPasoHacia(u, objetivo.PosicionX, objetivo.PosicionY);

        // Paso hacia una COORDENADA (para asediar edificios: la huella).
        private void IntentarPasoHacia(Unidad u, int metaX, int metaY)
        {
            int difX = metaX - u.PosicionX;
            int difY = metaY - u.PosicionY;
            int pasoX = Math.Sign(difX);
            int pasoY = Math.Sign(difY);

            if (Math.Abs(difX) >= Math.Abs(difY))
            {
                if (pasoX != 0 && Tablero.EsTransitable(u.PosicionX + pasoX, u.PosicionY, JugadorLocal, JugadorEnemigo))
                { u.MoverA(u.PosicionX + pasoX, u.PosicionY); return; }
                if (pasoY != 0 && Tablero.EsTransitable(u.PosicionX, u.PosicionY + pasoY, JugadorLocal, JugadorEnemigo))
                { u.MoverA(u.PosicionX, u.PosicionY + pasoY); return; }
            }
            else
            {
                if (pasoY != 0 && Tablero.EsTransitable(u.PosicionX, u.PosicionY + pasoY, JugadorLocal, JugadorEnemigo))
                { u.MoverA(u.PosicionX, u.PosicionY + pasoY); return; }
                if (pasoX != 0 && Tablero.EsTransitable(u.PosicionX + pasoX, u.PosicionY, JugadorLocal, JugadorEnemigo))
                { u.MoverA(u.PosicionX + pasoX, u.PosicionY); return; }
            }
        }

        // Aplica TODOS los golpes del latido de una vez (así nadie muta la vida de
        // una unidad mientras se recorre la lista) y retira a los muertos.
        private void AplicarPendientes(List<(Unidad victima, int dano)> pendientes)
        {
            foreach ((Unidad victima, int dano) golpe in pendientes)
            {
                if (golpe.victima == null || !golpe.victima.EstaViva) continue; // murió antes en este mismo latido
                golpe.victima.RecibirGolpe(golpe.dano);
            }

            int bajasLocal = JugadorLocal.Unidades.RemoveAll(u => !u.EstaViva);
            int bajasEnemigo = JugadorEnemigo.Unidades.RemoveAll(u => !u.EstaViva);
            BajasLocal += bajasLocal;
            BajasEnemigo += bajasEnemigo;

            if (bajasLocal > 0 || bajasEnemigo > 0)
                GestorArchivos.RegistrarAccion(JugadorLocal.Nombre, "Batalla",
                    $"Latido {TicksSimulados}: bajas local {bajasLocal}, enemigo {bajasEnemigo}.");

            VerificarGanador();
            VerificarFinBatalla();

            // Reposición: DESPUÉS de verificar ganador (una derrota total no
            // se evita con un aldeano nuevo; además Entrenar exige partida viva).
            if (ReposicionAldeanos && EstadoPartida.EnEjecucion)
            {
                ReponerAldeanos(JugadorLocal, false);
                ReponerAldeanos(JugadorEnemigo, true);
            }
        }

        private void ReponerAldeanos(Jugador dueno, bool esIA)
        {
            if (JugadorDerrotado(dueno)) return;
            int aldeanos = 0;
            foreach (Unidad u in dueno.Unidades)
                if (u.EstaViva && u.Tipo == TipoUnidad.Aldeano) aldeanos++;
            if (aldeanos >= 4) return;
            bool ok = esIA
                ? EntrenarUnidadIA(TipoUnidad.Aldeano, TipoEdificio.CentroUrbano)
                : EntrenarUnidad(TipoUnidad.Aldeano, TipoEdificio.CentroUrbano);
            if (ok) GestorArchivos.RegistrarAccion(dueno.Nombre, "Reposición",
                "Aldeano en camino (reemplazo automático).");
        }

        // [Batalla] Fin del modo batalla (pieza decidida): si un flanco se
        // queda sin combatientes controlados por IA, el bucle se apaga en vez
        // de latir para siempre. Con regicidio, arrasar el ejército ya no da
        // la victoria (hay que tumbar la capital), pero la pieza sí terminó.
        // Con IA activa NO se apaga: la máquina repone ejército y la partida
        // continúa.
        private void VerificarFinBatalla()
        {
            if (!BucleActivo || _ia != null || !EstadoPartida.EnEjecucion) return;

            bool quedanLocal =
                JugadorLocal.Unidades.Any(u => u.EstaViva && u.ControladaPorIA);
            bool quedanEnemigo =
                JugadorEnemigo.Unidades.Any(u => u.EstaViva && u.ControladaPorIA);
            if (quedanLocal && quedanEnemigo) return;

            BucleActivo = false;
            GestorArchivos.RegistrarAccion(JugadorLocal.Nombre, "Batalla",
                "Un flanco se quedó sin combatientes: pieza decidida, bucle apagado.");
        }

        // ESPEJO DE LA RED (el motor refleja la copia del rival)
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
                // Espejo de red: la copia rival OBEDECE, no opina. Si el rival dice
                // que movió a su unidad, se la mueve (solo se protege el mapa).
                if (!Tablero.EsCoordenadaValida(nuevoX, nuevoY)) return null;

                unidad.LimpiarDestino();
                unidad.Objetivo = null;
                if (unidad.PosicionX == nuevoX && unidad.PosicionY == nuevoY) return unidad;
                unidad.FijarDestino(nuevoX, nuevoY);
                unidad.Estado = EstadoUnidad.Moviendo;
                return unidad;
            }
        }

        public Unidad AplicarAtaqueRivalAUnidad(int ax, int ay, int bx, int by, int dano)
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

        public Edificio AplicarAtaqueRivalAEdificio(int ax, int ay, int bx, int by, int ataque)
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
                // Espejo de red: la copia rival replica la construcción sin opinar
                // (la casilla pudo estar libre en la máquina del rival y ocupada aquí).
                if (!Tablero.EsCoordenadaValida(x, y)) return false;

                Edificio espejo = DatosDelJuego.CrearEdificio(tipo, x, y);
                JugadorEnemigo.AgregarEdificio(espejo);
                CancellationTokenSource cts = new CancellationTokenSource();
                _construccionesActivas[espejo] = cts;
                IniciarTarea(ConstruccionTaskAsync(espejo,
                    DatosDelJuego.EdificiosBase[tipo].TiempoConstruccionSegundos,
                    JugadorEnemigo.Nombre,
                    cts), "Construccion espejo");
                return true;
            }
        }

        public bool CrearUnidadRival(TipoUnidad tipo, int x, int y)
        {
            lock (Candado)
            {
                // Espejo de red: la copia rival replica la unidad sin opinar.
                if (!Tablero.EsCoordenadaValida(x, y)) return false;

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
            lock (Candado)
            {
                // Espejo de red: replica SIN opinar (misma filosofía que los *Rival).
                // La ocupación de casilla la validó el host en su máquina; aquí solo
                // se protege el mapa y la duplicidad (idempotencia de la retransmisión).
                if (_detenido || !EstadoPartida.EnEjecucion) return false;
                if (!Tablero.EsCoordenadaValida(x, y)) return false;
                if (_itemsGlobales.Any(i => i.PosicionX == x && i.PosicionY == y)) return false;

                _itemsGlobales.Add(new Item(tipo, x, y));
                return true;
            }
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

        // AYUDANTES

        private bool EstanAdyacentes(Unidad unidad, Recurso recurso)
        {
            int dx = Math.Abs(unidad.PosicionX - recurso.PosicionX);
            int dy = Math.Abs(unidad.PosicionY - recurso.PosicionY);
            return dx <= 1 && dy <= 1;
        }

        // [Movimiento] Casilla libre en las 8 adyacentes a (x,y), la más cercana a
        // la unidad "respecto" (si no hay ninguna libre, null).
        private (int X, int Y)? CasillaLibreJuntoA(int x, int y, Unidad respecto)
        {
            int mejorX = -1, mejorY = -1, mejorD = int.MaxValue;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (!Tablero.EsCoordenadaValida(nx, ny)) continue;
                    if (Tablero.CasillaTieneRecurso(nx, ny)) continue;
                    // Transitable: las unidades no impiden ser el "punto de llegada"
                    // (varios aldeanos pueden apilarse junto al mismo yacimiento).
                    if (!Tablero.EsTransitable(nx, ny, JugadorLocal, JugadorEnemigo)) continue;
                    int d = Math.Abs(nx - respecto.PosicionX) + Math.Abs(ny - respecto.PosicionY);
                    if (d < mejorD) { mejorD = d; mejorX = nx; mejorY = ny; }
                }
            }
            return mejorX < 0 ? ((int X, int Y)?)null : (mejorX, mejorY);
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
                        if (Tablero.EsTransitable(x, y, JugadorLocal, JugadorEnemigo))
                            return (x, y);
                    }
                }
            }
            return (edificio.PosicionX, edificio.PosicionY);
        }
    }
}