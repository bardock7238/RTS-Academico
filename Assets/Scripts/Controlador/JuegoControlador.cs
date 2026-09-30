using System;
using System.Collections.Generic;
using Modelo;

namespace Controlador
{
    // ============================================================================
    //  CONTROLADOR — el PUENTE entre Vista y Modelo
    // ============================================================================
    //  REGLA DE ORO DE ESTA CAPA: aquí NO hay ni un Task, ni un Thread, ni un
    //  lock. El Controlador es un simple traductor que corre SIEMPRE en el hilo
    //  principal de Unity. Toda la concurrencia vive en Modelo/Simulacion.cs.
    //
    //  LOS INTERCAMBIOS EXACTOS (los 7 puntos por los que pasa la información):
    //  ---------------------------------------------------------------------------
    //  (1) VISTA -> CONTROLADOR, al arrancar la escena
    //      GestorJuego.Awake() hace:  new JuegoControlador(nombre, localArriba)
    //      El Controlador construye el Modelo (new Simulacion(...)), que a su vez
    //      arma el mundo y ARRANCA SUS PROPIAS TASKS (reloj, bucle, fauna, etc.).
    //
    //  (2) VISTA -> CONTROLADOR, cada frame (en GestorJuego.Update())
    //      a) ProcesarMensajesRedPendientes()  (red -> modelo, ver punto 4)
    //      b) Instantanea()                     (modelo -> vista, ver punto 5)
    //      El orden importa: la red primero, para pintar el frame ya al día.
    //
    //  (3) VISTA -> CONTROLADOR, cuando el usuario actúa
    //      ControlInputUsuario / MenuMercado / MenuMejoras llaman aquí las
    //      acciones (MoverUnidad, ConstruirEdificio, EntrenarUnidad, Atacar,
    //      MoverARecolectar, MoverARecogerItem, VenderRecurso, Mejorar*, ...).
    //      Cada una son 2 líneas: la primera delega en el Modelo (que valida
    //      bajo lock) y la segunda anuncia la acción al rival por red.
    //
    //  (4) CONTROLADOR -> MODELO, al recibir red
    //      ProcesarMensajesRedPendientes() -> ProcesarMensajeRed(mensaje) ->
    //      Motor.MoverUnidadRival / AplicarAtaqueRivalAUnidad / CrearUnidadRival
    //      ... El mensaje se traduce a un método "Espejo" del Modelo, que vuelve
    //      a tomar su candado para mutar SU copia del mundo.
    //
    //  (5) MODELO -> VISTA, al pintar (el único camino de regreso de datos)
    //      Motor.Instantanea() copia las listas bajo lock(Candado) y devuelve un
    //      InstantaneaJuego INMUTABLE. La Vista dibuja sobre esa copia. Nunca
    //      lee las listas vivas, así que no hay riesgo de "Collection was
    //      modified" aunque 10 Tasks estén mutando el mundo a la vez.
    //
    //  (6) MODELO -> RED, cuando el Modelo decide algo solo
    //      El Modelo llama a su propio Transmitir(mensaje), que mete el texto en
    //      la ConcurrentQueue _salientes. NO escribe al socket (¡nunca dentro de
    //      lock!). El Controlador la drena aquí, en el hilo principal, y la
    //      manda por ConectorRed. Así es como un ataque del bucle de simulación
    //      o un item del spawner llegan al rival.
    //
    //  (7) VISTA -> CONTROLADOR, al apagar
    //      GestorJuego.OnDestroy() / OnApplicationQuit() llaman Detener(), que
    //      cancela los 6 CancellationTokenSource del motor, drena la cola
    //      saliente, cierra el socket y hace Flush de los logs a disco.
    //
    //  LO QUE ESTA CAPA NO HACE JAMÁS:
    //    · No toca un Transform, un SpriteRenderer ni un Text.
    //    · No crea hilos ni toma candados.
    //    · No inventa reglas: si algo no es válido, lo dice el Modelo (false).
    //  ============================================================================
    public class JuegoControlador
    {
        // [Concurrencia aquí? NO.] El cerebro concurrente ES el Modelo.
        // Exponemos el motor para que la Vista y las pruebas configuren la simulación.
        public Simulacion Motor { get; }

        public Jugador JugadorLocal => Motor.JugadorLocal;
        public Jugador JugadorEnemigo => Motor.JugadorEnemigo;
        // Capital enemiga (su primer Centro): objetivo del regicidio.
        public Edificio CapitalEnemiga => Motor.CapitalEnemiga;        public Mapa Tablero => Motor.Tablero;
        public Partida EstadoPartida => Motor.EstadoPartida;
        public IReadOnlyList<Item> ItemsVisibles => Motor.ItemsVisibles;

        public ConectorRed RedPartida { get; private set; }
        public string NombreRivalRed { get; private set; }

        // Escenario opcional: nº de bases enemigas (1..4) y si arrancan
        // avanzadas (Cuartel + soldados) o básicas; igual para el jugador.
        // Ritmo (Rápida/Normal/Larga) e inicio rico (colchón + aldeanos + Casa).
        // Todo por defecto = partida clásico. No rompe llamadas existentes.
        public JuegoControlador(string nombreJugador, bool localArriba = true,
            int basesEnemigas = 1, bool enemigoAvanzado = false, bool jugadorAvanzado = false,
            RitmoPartida ritmo = RitmoPartida.Normal, bool inicioRico = false,
            bool exploracion = false)
        {
            // === INTERCAMBIO (1): VISTA -> MODELO, construcción del mundo ===
            // El Modelo arma el mundo entero (jugadores, mapa, partida, posiciones)
            // y arranca SUS tareas de fondo (reloj, bucle, fauna, economía y, si
            // soy host, el spawner). Todo eso ocurre aquí, en el hilo principal,
            // pero las Tasks que lanza siguen corriendo en el pool después.
            Motor = new Simulacion(nombreJugador, localArriba, basesEnemigas, enemigoAvanzado, jugadorAvanzado, ritmo, inicioRico, exploracion);
        }

        // [Concurrencia aquí? NO.] El cerebro concurrente ES el Modelo.
        // Exponemos el motor para que la Vista y las pruebas configuren la simulación.
        public Simulacion Motor { get; }

        public Jugador JugadorLocal => Motor.JugadorLocal;
        public Jugador JugadorEnemigo => Motor.JugadorEnemigo;
        // Capital enemiga (su primer Centro): objetivo del regicidio.
        public Edificio CapitalEnemiga => Motor.CapitalEnemiga;        public Mapa Tablero => Motor.Tablero;
        public Partida EstadoPartida => Motor.EstadoPartida;
        public IReadOnlyList<Item> ItemsVisibles => Motor.ItemsVisibles;

        public ConectorRed RedPartida { get; private set; }
        public string NombreRivalRed { get; private set; }

        // Escenario opcional: nº de bases enemigas (1..4) y si arrancan
        // avanzadas (Cuartel + soldados) o básicas; igual para el jugador.
        // Ritmo (Rápida/Normal/Larga) e inicio rico (colchón + aldeanos + Casa).
        // Todo por defecto = partida clásica. No rompe llamadas existentes.
        public JuegoControlador(string nombreJugador, bool localArriba = true,
            int basesEnemigas = 1, bool enemigoAvanzado = false, bool jugadorAvanzado = false,
            RitmoPartida ritmo = RitmoPartida.Normal, bool inicioRico = false,
            bool exploracion = false)
        {
            // El Modelo arma el mundo entero (jugadores, mapa, partida, posiciones)
            // y arranca SUS tareas de fondo (reloj y, si soy host, el spawner).
            Motor = new Simulacion(nombreJugador, localArriba, basesEnemigas, enemigoAvanzado, jugadorAvanzado, ritmo, inicioRico, exploracion);
        }

        // Exploración (niebla): revela el mapa con tus unidades.
        public bool ModoExploracion => Motor.ModoExploracion;
        public bool EsVisible(int x, int y) => Motor.EsVisible(x, y);

        // Cambia el ritmo en caliente (gracia + daño IA).
        public void AplicarRitmo(RitmoPartida ritmo) => Motor.AplicarRitmo(ritmo);

        // ========================================================================
        //  INTERCAMBIO (3): VISTA -> MODELO   (y de vuelta: MODELO -> RED)
        // ========================================================================
        //  Este es el BLOQUE COMPLETO de cada acción del jugador. Son 3 pasos
        //  fijos, y solo cambian los datos:
        //
        //    Paso 1  Guardar la posición de ORIGEN (el rival la necesitará).
        //    Paso 2  Delegar en el Modelo. ÉL valida (recursos, coordenadas,
        //            unidad en rango, partida viva) con lock(Candado) y muta.
        //            Si devuelve false, la acción es inválida y se aborta aquí.
        //    Paso 3  Si fue aceptada, ENCOLAR el comando para el rival.
        //            EnviarPorRed NO escribe al socket: mete el texto en la
        //            cola y el hilo de red lo saca. Por eso nunca hay bloqueo.
        //
        //  La Vista recibe el `true`/`false` de retorno y decide si muestra
        //  un mensaje de éxito o llama a AccionRechazada().
        // ========================================================================

        // ACCIONES DE JUEGO (todas delegan en el Modelo)

        // 1. Mover Unidad
        public bool MoverUnidad(Unidad unidad, int nuevoX, int nuevoY)
        {
            int origenX = unidad?.PosicionX ?? 0;
            int origenY = unidad?.PosicionY ?? 0;
            if (!Motor.MoverUnidad(unidad, nuevoX, nuevoY)) return false;

            EnviarPorRed($"MOVER;{origenX};{origenY};{nuevoX};{nuevoY}");
            return true;
        }

        // [Movimiento] Corta el caminar de una unidad (Escape / órdenes manuales).
        public bool CancelarDestino(Unidad unidad) => Motor.CancelarDestino(unidad);

        // 2. Construir Edificio
        public bool ConstruirEdificio(TipoEdificio tipo, int x, int y)
        {
            if (!Motor.ConstruirEdificio(tipo, x, y)) return false;

            EnviarPorRed($"CONSTRUIR;{tipo};{x};{y}");
            return true;
        }

        // 3. Entrenar Unidad: el Modelo lanza el "trabajo"; al terminar avisa por
        // su evento (ENTRENAR;...) y esta capa lo manda por red.
        public bool EntrenarUnidad(TipoUnidad tipo, TipoEdificio edificioOrigen)
        {
            return Motor.EntrenarUnidad(tipo, edificioOrigen);
        }

        // 4. Recolectar recursos en segundo plano.
        public bool IniciarRecoleccion(Unidad aldeano, Recurso recurso)
        {
            if (!Motor.IniciarRecoleccion(aldeano, recurso)) return false;

            EnviarPorRed($"RECOLECTAR;{aldeano.PosicionX};{aldeano.PosicionY};1");
            return true;
        }

        public bool DetenerRecoleccion(Unidad aldeano)
        {
            if (!Motor.DetenerRecoleccion(aldeano)) return false;

            EnviarPorRed($"RECOLECTAR;{aldeano.PosicionX};{aldeano.PosicionY};0");
            return true;
        }

        public bool EstaRecolectando(Unidad aldeano) => Motor.EstaRecolectando(aldeano);

        // 5. Atacar / 5b. Atacar Edificio. El daño se calcula DENTRO del Modelo y
        // su evento lo anuncia por red con los datos exactos que se aplicarán.
        public bool Atacar(Unidad atacante, Unidad enemigo) => Motor.Atacar(atacante, enemigo);

        // [Combate] Clic en enemigo: si está en rango pega; si no, la unidad
        // CAMINA hacia él (objetivo fijado) y pega sola al estar a golpe.
        public bool MoverAAtacar(Unidad atacante, Unidad enemigo) => Motor.MoverAAtacar(atacante, enemigo);

        public bool AtacarEdificio(Unidad atacante, Edificio edificioEnemigo) => Motor.AtacarEdificio(atacante, edificioEnemigo);

        // [Combate] Clic en edificio enemigo: si está a golpe demuele; si no,
        // la unidad CAMINA hasta su huella (asedio fijado) y demuele sola.
        // Como MoverAAtacar: los golpes se anuncian por red al pegar.
        public bool MoverAAtacarEdificio(Unidad atacante, Edificio edificioEnemigo) =>
            Motor.MoverAAtacarEdificio(atacante, edificioEnemigo);

        public void VerificarGanador() => Motor.VerificarGanador();

        // MODO BATALLA (concurrencia masiva — Nivel 1)
        // El motor (Modelo) late solo y hace pelear a las unidades de IA. La Vista
        // solo enciende/apaga el modo y lee las métricas para el HUD.
        public int IniciarBatalla(int unidadesPorLado) => Motor.IniciarBatalla(unidadesPorLado);
        public void DetenerBatalla() => Motor.DetenerBatalla();

        public bool BucleBatallaActivo => Motor.BucleActivo;
        public int TicksSimulados => Motor.TicksSimulados;
        public int BajasLocal => Motor.BajasLocal;
        public int BajasEnemigo => Motor.BajasEnemigo;

        // [PVE] Arranca la IA económica + militar del oponente (todo el hilo vive
        // en el Modelo). La Vista solo llama esto al elegir modo máquina.
        public bool IniciarIA() => Motor.IniciarIA();
        public bool IAActiva => Motor.IAActiva;
        // Handicap de la IA (1.0 = sin handicap). Ajustable sin romper la API.
        public double FactorDanoIA { get => Motor.FactorDanoIA; set => Motor.FactorDanoIA = value; }
        // Segundos de gracia sin ataques de la IA (PVE). Ajustable.
        public int GraciaMilitarSegundos { get => Motor.GraciaMilitarSegundos; set => Motor.GraciaMilitarSegundos = value; }
        // Reposición automática de aldeanos. Ajustable.
        public bool ReposicionAldeanos { get => Motor.ReposicionAldeanos; set => Motor.ReposicionAldeanos = value; }
        // Mercado y herrería (solo jugador local).
        public bool VenderRecurso(TipoRecurso tipo) => Motor.VenderRecurso(tipo);
        public bool ComprarRecurso(TipoRecurso tipo) => Motor.ComprarRecurso(tipo);
        public bool MejorarAtaque() => Motor.MejorarAtaque();
        public bool MejorarDefensa() => Motor.MejorarDefensa();
        public bool MejorarRecoleccion() => Motor.MejorarRecoleccion();
        // Facciones enemigas de la partida (una por base, en orden).
        public IReadOnlyList<string> FaccionesRivales => Motor.FaccionesRivales;
        // Cambio de modo en caliente: PVE → red/PvP apaga la IA sin detener la partida.
        public bool DetenerIA() => Motor.DetenerIA();

        // ========================================================================
        //  INTERCAMBIO (5): MODELO -> VISTA   (el único camino de regreso)
        // ========================================================================
        //  Instantanea() es la ÚNICA forma en que los datos del Modelo llegan
        //  a la Vista. Motor.Instantanea() toma lock(Candado) una sola vez,
        //  copia las 7 listas y los 5 recursos a un objeto InstantaneaJuego
        //  whose propiedades son de solo lectura, y lo devuelve.
        //
        //  Por qué una copia y no las listas vivas:
        //    · La Vista las recorre en cada frame, y a la vez 10 Tasks pueden
        //      estar agregando/quitando unidades. Recorrer la original sin
        //      candado lanzaría "Collection was modified" y dibujaría a medias.
        //    · Con la copia, la Vista itera SUYA lista: si en el Modelo muere
        //      una unidad a media partida, en la Vista esa unidad simplemente
        //      aparece en el frame siguiente. Cero excepciones.
        //  La Vista debe llamar esto UNA vez por frame y pintar desde ahí.
        // ========================================================================
        // API PARA LA VISTA (Unity)

        // Foto segura del mundo para pintar. La Vista la llama UNA vez por frame y
        // dibuja desde el resultado (evita leer listas que un Task está modificando).
        public InstantaneaJuego Instantanea() => Motor.Instantanea();

        // Apaga todo al salir de la escena o del modo Play: detiene el motor
        // (reloj, spawner, entrenamientos, recolecciones) y cierra la conexión de red.
        public void Detener()
        {
            Motor.Detener();

            // [Red] Antes de cortar el tubo se drena la cola de salida: un FIN o el
            // último evento encolado todavía tiene la oportunidad de salir.
            while (RedPartida != null && Motor.HaySalientes && RedPartida.EstaConectado)
            {
                string saliente = Motor.SiguienteSaliente();
                if (saliente == null) break;
                RedPartida.Enviar(saliente);
            }

            if (RedPartida != null)
            {
                RedPartida.AlConectar -= EnviarSaludoRed;
                RedPartida.Dispose();
                RedPartida = null;
            }

            // Asegura que los .txt quedaron escritos antes de salir del proceso.
            GestorArchivos.Flush();
        }

        // Items: el Modelo ejecuta la lógica; esta capa anuncia por red.
        public bool ColocarItem(TipoItem tipo, int x, int y, bool enviarPorRed)
        {
            if (!Motor.ColocarItem(tipo, x, y)) return false;

            if (enviarPorRed) EnviarPorRed($"ITEM;{tipo};{x};{y}");
            return true;
        }

        public bool RecogerItem(Unidad unidad, Item item)
        {
            if (!Motor.RecogerItem(unidad, item)) return false;

            EnviarPorRed($"RECOGER_ITEM;{item.Tipo};{item.PosicionX};{item.PosicionY}");
            return true;
        }

        // [Movimiento] Viaja a recoger un item / yacimiento (el Modelo fija el destino
        // y la unidad camina celda a celda). Si ya estaba adyacente, la acción es
        // inmediata y ESTA capa anuncia por red (como antes); si hay viaje, el
        // anuncio lo hace el Modelo al llegar (LlegarADestino).
        public bool MoverARecogerItem(Unidad unidad, Item item)
        {
            bool adyacente = unidad != null && item != null &&
                Math.Abs(unidad.PosicionX - item.PosicionX) <= 1 &&
                Math.Abs(unidad.PosicionY - item.PosicionY) <= 1;
            int origenX = unidad?.PosicionX ?? 0;
            int origenY = unidad?.PosicionY ?? 0;
            if (!Motor.MoverARecogerItem(unidad, item)) return false;

            if (adyacente)
                EnviarPorRed($"RECOGER_ITEM;{item.Tipo};{item.PosicionX};{item.PosicionY}");
            else if (unidad.TieneDestino)
                // Hay viaje: el rival camina SU copia hacia la misma casilla.
                EnviarPorRed($"MOVER;{origenX};{origenY};{unidad.DestinoX};{unidad.DestinoY}");
            return true;
        }

        public bool MoverARecolectar(Unidad aldeano, Recurso recurso)
        {
            bool adyacente = aldeano != null && recurso != null &&
                Math.Abs(aldeano.PosicionX - recurso.PosicionX) <= 1 &&
                Math.Abs(aldeano.PosicionY - recurso.PosicionY) <= 1;
            int origenX = aldeano?.PosicionX ?? 0;
            int origenY = aldeano?.PosicionY ?? 0;
            if (!Motor.MoverARecolectar(aldeano, recurso)) return false;

            if (adyacente && Motor.EstaRecolectando(aldeano))
                EnviarPorRed($"RECOLECTAR;{aldeano.PosicionX};{aldeano.PosicionY};1");
            else if (aldeano.TieneDestino)
                // Hay viaje: la llegada anuncia RECOLECTAR;1 desde el Modelo.
                EnviarPorRed($"MOVER;{origenX};{origenY};{aldeano.DestinoX};{aldeano.DestinoY}");
            return true;
        }

        // ========================================================================
        //  INTERCAMBIO (6): MODELO -> RED
        // ========================================================================
        //  El Modelo, desde cualquier Task, llama a su Transmitir(mensaje),
        //  que solo hace _salientes.Enqueue(mensaje) sobre una
        //  ConcurrentQueue. OJO: el Modelo NUNCA escribe al socket, porque
        //  hacerlo dentro de lock(Candado) podría congelar el mundo entero si
        //  el rival tiene la red lenta.
        //
        //  El socket se toca acá, en el hilo principal, dentro de
        //  ProcesarMensajesRedPendientes(). Ese es el diseño completo del
        //  productor-consumidor de la capa de red.
        // ========================================================================
        // RED (sockets TCP)

        // Modo host: abre el puerto y espera a que un rival se conecte.
        public bool HospedarRed(int puerto = 5505)
        {
            PrepararConector();
            if (!RedPartida.IniciarHost(puerto))
            {
                GestorArchivos.RegistrarAccion(JugadorLocal.Nombre, "Red",
                    "Error al hospedar: " + RedPartida.UltimoError);
                return false;
            }
            GestorArchivos.RegistrarAccion(JugadorLocal.Nombre, "Red",
                $"Esperando conexiones en {ConectorRed.ObtenerIpLocal()}:{puerto}");
            return true;
        }

        // Modo cliente: se une a la partida de otro host.
        public bool ConectarRed(string ip, int puerto = 5505)
        {
            PrepararConector();
            if (!RedPartida.Conectar(ip, puerto))
            {
                GestorArchivos.RegistrarAccion(JugadorLocal.Nombre, "Red",
                    "Error al conectar: " + RedPartida.UltimoError);
                return false;
            }
            GestorArchivos.RegistrarAccion(JugadorLocal.Nombre, "Red", $"Conectado a {ip}:{puerto}");
            return true;
        }

        // [Red] Antes de crear un conector nuevo se libera el anterior: un
        // TcpListener vivo tiene el puerto 5505 tomado y el segundo intento
        // (reintento, cambio host/cliente) fallaría con "address already in use".
        private void PrepararConector()
        {
            if (RedPartida != null)
            {
                RedPartida.AlConectar -= EnviarSaludoRed;
                RedPartida.Dispose();
                RedPartida = null;
            }
            RedPartida = new ConectorRed();
            RedPartida.AlConectar += EnviarSaludoRed; // saludo inicial Y cada reconexión
        }

        // Los efectos DE MI jugador NO se aplican dos veces: solo se avisa al rival
        // para que refleje la acción en su copia. El rival procesa con ProcesarMensajesRedPendientes.
        public int MensajesDescartados { get; private set; }

        private void EnviarPorRed(string mensaje)
        {
            // [PVE] Sin conector (modo máquina): no hay tubo que notificar; no se
            // cuenta como desync ni se ensucia el log con "NO ENVIADO".
            if (RedPartida == null) return;

            if (RedPartida.EstaConectado)
            {
                if (RedPartida.Enviar(mensaje)) return;
            }
            // [Red] Un mensaje que no pudo salir queda a la vista (HUD) y en el log:
            // es exactamente el punto donde nace un desync, y no debe pasar en silencio.
            MensajesDescartados++;
            GestorArchivos.RegistrarAccion("Sistema", "Red",
                $"NO ENVIADO (sin conexión): {mensaje}");
        }

        public void EnviarSaludoRed()
        {
            EnviarPorRed($"SALUDO;{JugadorLocal.Nombre}");
        }

        // La Vista llama esto en cada Update: aplica los mensajes que llegaron y drena los
        // que el Modelo quiere enviar. [Concurrencia] Quien LLENA las colas son los
        // Tasks del Modelo (red y simulacion); quien las VACÍA es el hilo del juego
        // (aquí, hilo principal). Nunca se tocan entre sí. Devuelve cuántos mensajes
        // DE ENTRADA se procesaron.
        public int ProcesarMensajesRedPendientes()
        {
            // [PVE] Sin conector: drenar y descartar la cola saliente del Modelo
            // (ataques/entrenamientos encolados) para no acumular memoria en modo local.
            if (RedPartida == null)
            {
                while (Motor.HaySalientes)
                {
                    if (Motor.SiguienteSaliente() == null) break;
                }
                return 0;
            }

            // [Concurrencia] SALIDA: lo que el Modelo encoló (ataque, entrenamiento
            // terminado, item, recoleccion...) se envía SOLO si el tubo está vivo.
            // Si el rival está desconectado, se quedan en cola y salen al reconectar.
            // Tope por frame: una ráfaga (WiFi con jitter) se drena en varios
            // frames en vez de congelar uno solo.
            if (RedPartida.EstaConectado)
            {
                int enviados = 0;
                while (Motor.HaySalientes && enviados < 500)
                {
                    string saliente = Motor.SiguienteSaliente();
                    if (saliente == null) break;
                    EnviarPorRed(saliente);
                    enviados++;
                }
            }

            int procesados = 0;
            while (RedPartida.HayMensajes && procesados < 500)            {
                string mensaje = RedPartida.RecibirMensaje();
                if (mensaje == null) break;
                try
                {
                    ProcesarMensajeRed(mensaje);
                }
                catch (Exception ex)
                {
                    // [Red] Frontera con datos que vienen de fuera del proceso: un
                    // mensaje malo NO debe tumbar el Update() del juego. Se descarta
                    // y se deja constancia para no perder el desync en silencio.
                    GestorArchivos.RegistrarAccion("Sistema", "Red",
                        $"Mensaje descartado por error ({ex.GetType().Name}): {mensaje}");
                }
                procesados++;
            }

            // [Red] Si este lado fue el que detectó la victoria, anuncia el FIN una
            // sola vez para que la otra máquina cierre con el MISMO ganador (M6).
            string ganador = EstadoPartida.GanadorNombre;
            if (ganador != null && _ganadorAnunciado == null)
            {
                _ganadorAnunciado = ganador;
                EnviarPorRed($"FIN;{ganador}");
            }

            // [Red] Latido anti-tubo-muerto: el WiFi a veces deja la conexión
            // a medias (sin FIN): todo callado y el espejo congelado. Si el
            // tubo lleva 3 s sin enviar, PING silencioso; si lleva 15 s sin
            // recibir NADA, se corta y el hilo reconecta solo (o el menú a
            // los 5 s si no vuelve). Requiere el juego actualizado en ambos.
            if (RedPartida.EstaConectado)
            {
                DateTime ahora = DateTime.UtcNow;
                if ((ahora - RedPartida.UltimoEnviado).TotalSeconds > 3)
                    EnviarPorRed("PING");
                if ((ahora - RedPartida.UltimoRecibido).TotalSeconds > 15)
                {
                    GestorArchivos.RegistrarAccion("Sistema", "Red",
                        "Tubo callado 15 s: cortando para reconectar.");
                    RedPartida.CortarConexion();
                }
            }
            return procesados;
        }

        // "MOVER;xOrigen;yOrigen;xNuevo;yNuevo"
        // "ATACAR;xAtacante;yAtacante;xObjetivo;yObjetivo;dano"
        // "ATACAR_EDIFICIO;xAtacante;yAtacante;xEdificio;yEdificio;ataque"
        // "CONSTRUIR;Tipo;x;y"
        // "ENTRENAR;Tipo;x;y"
        // "SALUDO;nombre"
        // "RECOLECTAR;x;y;1|0"
        // "ITEM;TipoItem;x;y"
        // "RECOGER_ITEM;TipoItem;x;y"
        // "FIN;ganador"
        // "PING" / "PONG" (latido silencioso anti-tubo-muerto)
        //
        // Cada mensaje se traduce a un método "Espejo" del Modelo, que se encarga de
        // su candado y de mutar la copia rival. Aquí solo parseamos y llevamos cuenta.
        // [Concurrencia] Un mensaje truncado (por una reconexión a medias) NO debe
        // reventar el parser: antes del switch se valida la aridad de cada comando.
        private static readonly IReadOnlyDictionary<string, int> Aridad = new Dictionary<string, int>
        {
            { "SALUDO", 2 },
            { "MOVER", 5 },
            { "ATACAR", 6 },
            { "ATACAR_EDIFICIO", 6 },
            { "CONSTRUIR", 4 },
            { "ENTRENAR", 4 },
            { "RECOLECTAR", 4 },
            { "ITEM", 4 },
            { "RECOGER_ITEM", 4 },
            { "FIN", 2 },
            { "PING", 1 },
            { "PONG", 1 }
        };

        // [Concurrencia] El guard del FIN anunciado: evita el rebote infinito si las
        // dos máquinas detectan la victoria a la vez y que el Local no anuncie dos veces.
        private string _ganadorAnunciado;

        private void ProcesarMensajeRed(string mensaje)
        {
            string[] p = mensaje.Split(';');
            if (p.Length == 0) return;
            if (!Aridad.TryGetValue(p[0], out int esperado) || p.Length < esperado)
            {
                GestorArchivos.RegistrarAccion("Sistema", "Red",
                    $"Mensaje mal formado o incompleto: {mensaje}");
                return;
            }

            switch (p[0])
            {
                case "SALUDO":
                    NombreRivalRed = p[1];
                    Motor.EstablecerNombreRival(p[1]); // El rival presenta su nombre real.
                    GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "Red",
                        $"Rival conectado: {p[1]}");
                    break;

                case "PING":
                    EnviarPorRed("PONG"); // latido: se responde sin loguear
                    break;

                case "PONG":
                    break; // latido: basta con haberlo recibido

                case "MOVER":
                {
                    if (!int.TryParse(p[1], out int ox) || !int.TryParse(p[2], out int oy) ||
                        !int.TryParse(p[3], out int nx) || !int.TryParse(p[4], out int ny)) return;
                    Unidad unidad = Motor.MoverUnidadRival(ox, oy, nx, ny);
                    if (unidad != null)
                        GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "Red",
                            $"Rival movió {unidad.Tipo} a ({nx},{ny}).");
                    break;
                }

                case "ATACAR":
                {
                    if (!int.TryParse(p[1], out int ax) || !int.TryParse(p[2], out int ay) ||
                        !int.TryParse(p[3], out int bx) || !int.TryParse(p[4], out int by) ||
                        !int.TryParse(p[5], out int dano)) return;
                    Unidad objetivo = Motor.AplicarAtaqueRivalAUnidad(ax, ay, bx, by, dano);
                    if (objetivo != null)
                        GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "Red",
                            $"Rival atacó a {objetivo.Tipo} ({dano} de daño).");
                    break;
                }

                case "CONSTRUIR":
                {
                    if (!int.TryParse(p[2], out int bx) || !int.TryParse(p[3], out int by)) return;
                    // [Red] Sin Enum.TryParse aquí el default silencioso de
                    // ObtenerTipoEdificio regalaría un Centro Urbano al rival.
                    if (!Enum.TryParse(p[1], true, out TipoEdificio tipo))
                    {
                        GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "Red",
                            $"Tipo de edificio inválido: {p[1]}");
                        return;
                    }
                    if (Motor.CrearEdificioRival(tipo, bx, by))
                        GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "Red",
                            $"Rival construyó {tipo} en ({bx},{by}).");
                    break;
                }

                case "ENTRENAR":
                {
                    if (!Enum.TryParse(p[1], true, out TipoUnidad tipo) ||
                        !int.TryParse(p[2], out int ux) || !int.TryParse(p[3], out int uy)) return;
                    if (Motor.CrearUnidadRival(tipo, ux, uy))
                        GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "Red",
                            $"Rival entrenó {tipo} en ({ux},{uy}).");
                    break;
                }

                case "RECOLECTAR":
                {
                    // RECOLECTAR;x;y;1|0  → el rival encendió/apagó la recolección de su aldeano.
                    if (!int.TryParse(p[1], out int rx) || !int.TryParse(p[2], out int ry) ||
                        !int.TryParse(p[3], out int flag)) return;
                    if (Motor.CambiarEstadoRecoleccionRival(rx, ry, flag == 1))
                        GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "Red",
                            $"Rival {(flag == 1 ? "empezó a recolectar" : "detuvo la recolección")} en ({rx},{ry}).");
                    break;
                }

                case "ATACAR_EDIFICIO":
                {
                    // ATACAR_EDIFICIO;xAtacante;yAtacante;xEdificio;yEdificio;ataque
                    if (!int.TryParse(p[1], out int ax) || !int.TryParse(p[2], out int ay) ||
                        !int.TryParse(p[3], out int bx) || !int.TryParse(p[4], out int by) ||
                        !int.TryParse(p[5], out int ataque)) return;
                    Edificio objetivo = Motor.AplicarAtaqueRivalAEdificio(ax, ay, bx, by, ataque);
                    if (objetivo != null)
                        GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "Red",
                            $"Rival atacó {objetivo.Tipo} ({ataque} de ataque).");
                    break;
                }

                case "ITEM":
                {
                    // ITEM;Tipo;x;y → el host sembró un item; el espejo lo coloca igual.
                    if (!Enum.TryParse(p[1], true, out TipoItem tipo) ||
                        !int.TryParse(p[2], out int ix) || !int.TryParse(p[3], out int iy)) return;
                    if (Motor.ColocarItemRival(tipo, ix, iy))
                        GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "Red",
                            $"Apareció {DatosDelJuego.NombreDe(tipo)} en ({ix},{iy}).");
                    break;
                }

                case "RECOGER_ITEM":
                {
                    // RECOGER_ITEM;Tipo;x;y → el rival tomó un item: yo dejo de
                    // verlo en mi copia y replico los efectos compartidos (Casco).
                    if (!Enum.TryParse(p[1], true, out TipoItem tipo) ||
                        !int.TryParse(p[2], out int ix) || !int.TryParse(p[3], out int iy)) return;
                    if (Motor.AplicarRecogidaRival(tipo, ix, iy))
                        GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "Red",
                            $"Rival recogió {DatosDelJuego.NombreDe(tipo)} en ({ix},{iy}).");
                    break;
                }

                case "FIN":
                {
                    // FIN;ganador → el rival ya detectó la victoria y la anunció.
                    // Si yo aún no declaré ganador, me alineo con su resultado
                    // (así ningún lado escribe dos archivos de resultado distintos).
                    if (_ganadorAnunciado != null) break;
                    _ganadorAnunciado = p[1];
                    Motor.EstadoPartida.Finalizar(p[1]);
                    GestorArchivos.GuardarResultadoFinal($"¡Ganador: {p[1]}!");
                    GestorArchivos.RegistrarAccion("Sistema", "Red",
                        $"Fin de partida anunciado por el rival: {p[1]}");
                    break;
                }
            }
        }
    }
}
