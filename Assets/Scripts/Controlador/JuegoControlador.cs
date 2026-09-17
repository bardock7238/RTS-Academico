using System;
using System.Collections.Generic;
using Modelo;

namespace Controlador
{
    // ============================================================================
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
    // ============================================================================
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

            // Cuando el Modelo produce algo que debe anunciarse por red (un ataque
            // con su daño calculado, una unidad que terminó de entrenar, un item que
            // apareció solo, un aldeano que terminó su cosecha) lo reenviamos aquí.
            Motor.ParaTransmitir += EnviarPorRed;
        }

        // ============ ACCIONES DE JUEGO (todas delegan al Modelo) ============

        // 1. Mover Unidad
        public bool MoverUnidad(Unidad unidad, int nuevoX, int nuevoY)
        {
            int origenX = unidad?.PosicionX ?? 0;
            int origenY = unidad?.PosicionY ?? 0;
            if (!Motor.MoverUnidad(unidad, nuevoX, nuevoY)) return false;

            EnviarPorRed($"MOVER;{origenX};{origenY};{nuevoX};{nuevoY}");
            return true;
        }

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
        public bool AtacarEdificio(Unidad atacante, Edificio edificioEnemigo) => Motor.AtacarEdificio(atacante, edificioEnemigo);

        public void VerificarGanador() => Motor.VerificarGanador();

        // ============ MODO BATALLA (concurrencia masiva — Nivel 1) ============
        // El motor (Modelo) late solo y hace pelear a las unidades de IA. La Vista
        // solo enciende/apaga el modo y lee las métricas para el HUD.
        public int IniciarBatalla(int unidadesPorLado) => Motor.IniciarBatalla(unidadesPorLado);
        public void DetenerBatalla() => Motor.DetenerBatalla();

        public bool BucleBatallaActivo => Motor.BucleActivo;
        public int TicksSimulados => Motor.TicksSimulados;
        public int BajasLocal => Motor.BajasLocal;
        public int BajasEnemigo => Motor.BajasEnemigo;

        // ============ API PARA LA VISTA (Unity) ============

        // Foto segura del mundo para pintar. La Vista la llama UNA vez por frame y
        // dibuja desde el resultado (evita leer listas que un Task está modificando).
        public InstantaneaJuego Instantanea() => Motor.Instantanea();

        // Apaga todo al salir de la escena o del modo Play: detiene el motor
        // (reloj, spawner, entrenamientos, recolecciones) y cierra la conexión de red.
        public void Detener()
        {
            Motor.Detener();
            RedPartida?.Dispose();
            RedPartida = null;
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

        // ============ RED (sockets TCP) ============

        // Modo host: abre el puerto y espera a que un compañero se conecte.
        public bool HospedarRed(int puerto = 5505)
        {
            RedPartida = new ConectorRed();
            RedPartida.AlConectar += EnviarSaludoRed; // saludo inicial Y cada reconexión
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
            RedPartida = new ConectorRed();
            RedPartida.AlConectar += EnviarSaludoRed; // saludo inicial Y cada reconexión
            if (!RedPartida.Conectar(ip, puerto))
            {
                GestorArchivos.RegistrarAccion(JugadorLocal.Nombre, "Red",
                    "Error al conectar: " + RedPartida.UltimoError);
                return false;
            }
            GestorArchivos.RegistrarAccion(JugadorLocal.Nombre, "Red", $"Conectado a {ip}:{puerto}");
            return true;
        }

        // Los efectos DE MI jugador NO se aplican dos veces: solo se avisa al rival
        // para que refleje la acción en su copia. El rival procesa con ProcesarMensajesRedPendientes.
        private void EnviarPorRed(string mensaje)
        {
            if (RedPartida != null && RedPartida.EstaConectado)
                RedPartida.Enviar(mensaje);
        }

        public void EnviarSaludoRed()
        {
            EnviarPorRed($"SALUDO;{JugadorLocal.Nombre}");
        }

        // La Vista llama esto en cada Update: aplica los mensajes que llegaron.
        // [Concurrencia] Quien LLENA la cola es el hilo de escucha de la red
        // (vive en Modelo/ConectorRed); quien la VACÍA es el hilo del juego (aquí).
        // Nunca se tocan entre sí. Devuelve cuántos mensajes se procesaron.
        public int ProcesarMensajesRedPendientes()
        {
            if (RedPartida == null) return 0;
            int procesados = 0;
            while (RedPartida.HayMensajes)
            {
                string mensaje = RedPartida.RecibirMensaje();
                if (mensaje == null) break;
                ProcesarMensajeRed(mensaje);
                procesados++;
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
        //
        // Cada mensaje se traduce a un método "Espejo" del Modelo, que se encarga de
        // su candado y de mutar la copia rival. Aquí solo parseamos y llevamos cuenta.
        private void ProcesarMensajeRed(string mensaje)
        {
            string[] p = mensaje.Split(';');
            if (p.Length < 2) return;

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
                    Unidad objetivo = Motor.AplicarAtaqueEnUnidadLocal(ax, ay, bx, by, dano);
                    if (objetivo != null)
                        GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "Red",
                            $"Rival atacó a {objetivo.Tipo} ({dano} de daño).");
                    break;
                }

                case "CONSTRUIR":
                {
                    if (!int.TryParse(p[2], out int bx) || !int.TryParse(p[3], out int by)) return;
                    TipoEdificio tipo = DatosDelJuego.ObtenerTipoEdificio(p[1]);
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
                    Edificio objetivo = Motor.AplicarAtaqueEnEdificioLocal(ax, ay, bx, by, ataque);
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
            }
        }
    }
}