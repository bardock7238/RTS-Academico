using System;
using System.Collections.Generic;
using Modelo;

namespace Controlador
{
    //  CONTROLADOR (capa delgada)
    //  ----------------------------------------------------------------------------
    //  Aquí NO vive la concurrencia: el motor concurrente está en Modelo/Simulacion
    //  (reloj, entrenamiento, construcción, recolección, spawner de items y el
    //  candado del mundo). Este Controlador:
    //     1. Expone el mundo (los objetos ENTEROS vienen del Modelo).
    //     2. Traduce acciones del usuario → métodos del Modelo (que se andan solos).
    //     3. Traduce mensajes de red → métodos "Espejo" del Modelo.
    //     4. Anuncia al rival las acciones (enviar por TCP) y escribe logs.
    //
    //  El único "hilo" que sigue aquí es el del socket TCP (ConectorRed), que ahora
    //  también vive en Modelo, y la cola que llena ese hilo; el procesamiento de los
    //  mensajes lo hace la Vista desde el hilo principal (ver ProcesarMensajesRedPendientes).
    //
    //  Los tiempos (cuánto tarda un entrenamiento, cada cuánto late el reloj...) los
    //  define el Modelo (Simulacion); el Controlador solo pide y avisa.
    public class JuegoControlador
    {
        // [Concurrencia aquí? NO.] El cerebro concurrente ES el Modelo.
        // Exponemos el motor para que la Vista y las pruebas configuren la simulación.
        public Simulacion Motor { get; }

        public Jugador JugadorLocal => Motor.JugadorLocal;
        public Jugador JugadorEnemigo => Motor.JugadorEnemigo;
        public Mapa Tablero => Motor.Tablero;
        public Partida EstadoPartida => Motor.EstadoPartida;
        public IReadOnlyList<Item> ItemsVisibles => Motor.ItemsVisibles;

        public ConectorRed RedPartida { get; private set; }
        public string NombreRivalRed { get; private set; }

        public JuegoControlador(string nombreJugador, bool localArriba = true)
        {
            // El Modelo arma el mundo entero (jugadores, mapa, partida, posiciones)
            // y arranca SUS tareas de fondo (reloj y, si soy host, el spawner).
            Motor = new Simulacion(nombreJugador, localArriba);
        }

        // ACCIONES DE JUEGO (todas delegan al Modelo)

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
            if (RedPartida.EstaConectado)
            {
                while (Motor.HaySalientes)
                {
                    string saliente = Motor.SiguienteSaliente();
                    if (saliente == null) break;
                    EnviarPorRed(saliente);
                }
            }

            int procesados = 0;
            while (RedPartida.HayMensajes)
            {
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
            { "FIN", 2 }
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