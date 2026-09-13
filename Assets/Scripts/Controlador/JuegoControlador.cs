using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modelo;

namespace Controlador
{
    public class JuegoControlador
    {
        public Jugador JugadorLocal { get; private set; }
        public Jugador JugadorEnemigo { get; private set; }
        public Mapa Tablero { get; private set; }
        public Partida EstadoPartida { get; private set; }
        public ConectorRed RedPartida { get; private set; }
        public string NombreRivalRed { get; private set; }

        // Candado compartido: todo cambio a recursos/listas pasa por aquí para
        // evitar condiciones de carrera cuando los hilos de recolección corren.
        private readonly object _lockJuego = new object();

        // Trabajos en segundo plano activos: para poder cancelarlos.
        private readonly Dictionary<TipoUnidad, CancellationTokenSource> _entrenamientosActivos =
            new Dictionary<TipoUnidad, CancellationTokenSource>();
        private readonly Dictionary<Unidad, CancellationTokenSource> _recolectoresActivos =
            new Dictionary<Unidad, CancellationTokenSource>();

        public JuegoControlador(string nombreJugador, bool localArriba = true)
        {
            JugadorLocal = new Jugador(nombreJugador);
            JugadorEnemigo = new Jugador("Enemigo");
            Tablero = new Mapa();
            EstadoPartida = new Partida();

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

        // ============ ACCIONES DE JUEGO ============

        // 1. Mover Unidad
        public bool MoverUnidad(Unidad unidad, int nuevoX, int nuevoY)
        {
            lock (_lockJuego)
            {
                if (unidad == null) return false;
                if (!Tablero.EsCoordenadaValida(nuevoX, nuevoY)) return false;
                if (!Tablero.EsCasillaLibre(nuevoX, nuevoY, JugadorLocal, JugadorEnemigo)) return false;

                int origenX = unidad.PosicionX;
                int origenY = unidad.PosicionY;
                unidad.MoverA(nuevoX, nuevoY);

                EnviarPorRed($"MOVER;{origenX};{origenY};{nuevoX};{nuevoY}");

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
            lock (_lockJuego)
            {
                if (!Tablero.EsCoordenadaValida(x, y)) return false;
                if (Tablero.CasillaTieneRecurso(x, y)) return false;
                if (!Tablero.EsCasillaLibre(x, y, JugadorLocal, JugadorEnemigo)) return false;

                EdificioConfig config = DatosDelJuego.EdificiosBase[tipo];
                if (!JugadorLocal.Gastar(config.CostoMadera, config.CostoOro, config.CostoComida))
                    return false;

                Edificio nuevoEdificio = DatosDelJuego.CrearEdificio(tipo, x, y);
                JugadorLocal.AgregarEdificio(nuevoEdificio);

                EnviarPorRed($"CONSTRUIR;{tipo};{x};{y}");

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
        public bool EntrenarUnidad(TipoUnidad tipo, TipoEdificio edificioOrigen)
        {
            lock (_lockJuego)
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

            lock (_lockJuego)
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
                EnviarPorRed($"ENTRENAR;{tipo};{nueva.PosicionX};{nueva.PosicionY}");
                GestorArchivos.RegistrarAccion(
                    JugadorLocal.Nombre,
                    "Entrenar",
                    $"{tipo} listo en ({nueva.PosicionX},{nueva.PosicionY}).");
            }
        }

        // La construcción avanza sola: tras los segundos del catálogo, el edificio
        // queda operativo y ya puede entrenar. Se usa igual para el rival espejo.
        private async Task ConstruccionTaskAsync(Edificio edificio, int segundos, string nombreDueno)
        {
            await EsperarConstruccion(segundos);

            lock (_lockJuego)
            {
                edificio.CompletarConstruccion();
                GestorArchivos.RegistrarAccion(
                    nombreDueno,
                    "Construir",
                    $"{edificio.Tipo} en ({edificio.PosicionX},{edificio.PosicionY}) terminado.");
            }
        }

        // 4. Recolectar recursos en segundo plano (un hilo por aldeano).
        public bool IniciarRecoleccion(Unidad aldeano, Recurso recurso)
        {
            lock (_lockJuego)
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
                EnviarPorRed($"RECOLECTAR;{aldeano.PosicionX};{aldeano.PosicionY};1");

                GestorArchivos.RegistrarAccion(
                    JugadorLocal.Nombre,
                    "Recolectar",
                    $"{aldeano.Tipo} recolecta {recurso.Tipo} en ({recurso.PosicionX},{recurso.PosicionY}).");
                return true;
            }
        }

        public bool DetenerRecoleccion(Unidad aldeano)
        {
            lock (_lockJuego)
            {
                if (_recolectoresActivos.TryGetValue(aldeano, out CancellationTokenSource cts))
                {
                    cts.Cancel();
                    EnviarPorRed($"RECOLECTAR;{aldeano.PosicionX};{aldeano.PosicionY};0");
                    return true;
                }
                return false;
            }
        }

        public bool EstaRecolectando(Unidad aldeano)
        {
            lock (_lockJuego)
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

                lock (_lockJuego)
                {
                    if (!aldeano.EstaViva || recurso.EstaAgotado) { terminoSolo = true; break; }
                    int cantidad = recurso.Extraer(cantidadPorCiclo);
                    if (cantidad <= 0) { terminoSolo = true; break; }

                    EntregarRecurso(recurso.Tipo, cantidad);
                    GestorArchivos.RegistrarAccion(
                        JugadorLocal.Nombre,
                        "Recolectar",
                        $"+{cantidad} de {recurso.Tipo}. Total {JugadorLocal.Oro}/{JugadorLocal.Madera}/{JugadorLocal.Comida}");
                }
            }

            lock (_lockJuego)
            {
                _recolectoresActivos.Remove(aldeano);
                aldeano.Estado = EstadoUnidad.Idle;
            }

            // Si terminó solo (yacimiento vacío o aldeano muerto), avisa al rival para
            // que su copia también vuelva a Idle. Si lo detuvieron, ya avisó DetenerRecoleccion.
            if (terminoSolo)
                EnviarPorRed($"RECOLECTAR;{aldeano.PosicionX};{aldeano.PosicionY};0");
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
            lock (_lockJuego)
            {
                if (atacante == null || enemigo == null) return false;
                if (!atacante.PuedeAtacar || !enemigo.EstaViva) return false;

                int distancia = Math.Abs(atacante.PosicionX - enemigo.PosicionX)
                              + Math.Abs(atacante.PosicionY - enemigo.PosicionY);
                if (distancia > atacante.RangoAtaque) return false; // fuera de alcance

                atacante.Estado = EstadoUnidad.Atacando;
                enemigo.RecibirDano(atacante.Ataque);

                int danoReal = Math.Max(0, atacante.Ataque - enemigo.Defensa);
                EnviarPorRed($"ATACAR;{atacante.PosicionX};{atacante.PosicionY};{enemigo.PosicionX};{enemigo.PosicionY};{danoReal}");
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
            lock (_lockJuego)
            {
                if (atacante == null || edificioEnemigo == null) return false;
                if (!atacante.PuedeAtacar || !edificioEnemigo.EstaViva) return false;
                if (!JugadorEnemigo.Edificios.Contains(edificioEnemigo)) return false;

                int distancia = Math.Abs(atacante.PosicionX - edificioEnemigo.PosicionX)
                              + Math.Abs(atacante.PosicionY - edificioEnemigo.PosicionY);
                if (distancia > atacante.RangoAtaque) return false;

                atacante.Estado = EstadoUnidad.Atacando;
                edificioEnemigo.RecibirDano(atacante.Ataque);

                // Se envía el ATAQUE (no el daño final): el rival aplica la MISMA fórmula
                // con su copia del edificio y los dos lados coinciden.
                EnviarPorRed($"ATACAR_EDIFICIO;{atacante.PosicionX};{atacante.PosicionY};" +
                             $"{edificioEnemigo.PosicionX};{edificioEnemigo.PosicionY};{atacante.Ataque}");

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
            if (JugadorDerrotado(JugadorEnemigo))
            {
                FinalizarPartida(JugadorLocal);
            }
            else if (JugadorDerrotado(JugadorLocal))
            {
                FinalizarPartida(JugadorEnemigo);
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

        // ============ RED (sockets TCP) ============

        // Modo host: abre el puerto y espera a que un compañero se conecte.
        public bool HospedarRed(int puerto = 5505)
        {
            RedPartida = new ConectorRed();
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
            if (!RedPartida.Conectar(ip, puerto))
            {
                GestorArchivos.RegistrarAccion(JugadorLocal.Nombre, "Red",
                    "Error al conectar: " + RedPartida.UltimoError);
                return false;
            }
            GestorArchivos.RegistrarAccion(JugadorLocal.Nombre, "Red", $"Conectado a {ip}:{puerto}");
            EnviarPorRed($"SALUDO;{JugadorLocal.Nombre}");
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
        // Devuelve cuántos mensajes se procesaron.
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
        private void ProcesarMensajeRed(string mensaje)
        {
            string[] p = mensaje.Split(';');
            if (p.Length < 2) return;

            lock (_lockJuego)
            {
                switch (p[0])
                {
                    case "SALUDO":
                        NombreRivalRed = p[1];
                        JugadorEnemigo.Nombre = p[1]; // El rival presenta su nombre real.
                        GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "Red",
                            $"Rival conectado: {p[1]}");
                        break;

                    case "MOVER":
                    {
                        if (!int.TryParse(p[1], out int ox) || !int.TryParse(p[2], out int oy) ||
                            !int.TryParse(p[3], out int nx) || !int.TryParse(p[4], out int ny)) return;
                        Unidad unidad = JugadorEnemigo.Unidades.FirstOrDefault(
                            u => u.PosicionX == ox && u.PosicionY == oy);
                        if (unidad != null && unidad.EstaViva &&
                            Tablero.EsCoordenadaValida(nx, ny) &&
                            Tablero.EsCasillaLibre(nx, ny, JugadorLocal, JugadorEnemigo))
                        {
                            unidad.MoverA(nx, ny);
                            GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "Red",
                                $"Rival movió {unidad.Tipo} a ({nx},{ny}).");
                        }
                        break;
                    }

                    case "ATACAR":
                    {
                        if (!int.TryParse(p[1], out int ax) || !int.TryParse(p[2], out int ay) ||
                            !int.TryParse(p[3], out int bx) || !int.TryParse(p[4], out int by) ||
                            !int.TryParse(p[5], out int dano)) return;
                        Unidad atacante = JugadorEnemigo.Unidades.FirstOrDefault(
                            u => u.PosicionX == ax && u.PosicionY == ay);
                        Unidad objetivo = JugadorLocal.Unidades.FirstOrDefault(
                            u => u.PosicionX == bx && u.PosicionY == by);
                        if (atacante == null || objetivo == null || !objetivo.EstaViva) return;
                        objetivo.RecibirDano(dano);
                        GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "Red",
                            $"Rival atacó a {objetivo.Tipo} ({dano} de daño).");
                        if (!objetivo.EstaViva)
                        {
                            JugadorLocal.EliminarUnidad(objetivo);
                            VerificarGanador();
                        }
                        break;
                    }

                    case "CONSTRUIR":
                    {
                        if (!int.TryParse(p[2], out int bx) || !int.TryParse(p[3], out int by)) return;
                        if (!Tablero.EsCoordenadaValida(bx, by)) return;
                        if (Tablero.CasillaTieneRecurso(bx, by)) return;
                        if (!Tablero.EsCasillaLibre(bx, by, JugadorLocal, JugadorEnemigo)) return;
                        TipoEdificio tipo = DatosDelJuego.ObtenerTipoEdificio(p[1]);
                        Edificio espejo = DatosDelJuego.CrearEdificio(tipo, bx, by);
                        JugadorEnemigo.AgregarEdificio(espejo);
                        _ = ConstruccionTaskAsync(espejo,
                            DatosDelJuego.EdificiosBase[tipo].TiempoConstruccionSegundos,
                            JugadorEnemigo.Nombre);
                        GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "Red",
                            $"Rival construyó {tipo} en ({bx},{by}).");
                        break;
                    }

                    case "ENTRENAR":
                    {
                        if (!Enum.TryParse(p[1], true, out TipoUnidad tipo) ||
                            !int.TryParse(p[2], out int ux) || !int.TryParse(p[3], out int uy)) return;
                        if (!Tablero.EsCoordenadaValida(ux, uy)) return;
                        if (!Tablero.EsCasillaLibre(ux, uy, JugadorLocal, JugadorEnemigo)) return;
                        JugadorEnemigo.AgregarUnidad(DatosDelJuego.CrearUnidad(tipo, ux, uy));
                        GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "Red",
                            $"Rival entrenó {tipo} en ({ux},{uy}).");
                        break;
                    }

                    case "RECOLECTAR":
                    {
                        // RECOLECTAR;x;y;1|0  → el rival encendió/apagó la recolección de su aldeano.
                        if (!int.TryParse(p[1], out int rx) || !int.TryParse(p[2], out int ry) ||
                            !int.TryParse(p[3], out int flag)) return;
                        Unidad enemigo = JugadorEnemigo.Unidades.FirstOrDefault(
                            u => u.PosicionX == rx && u.PosicionY == ry);
                        // Solo reflejamos el estado visual: NO se lanza otro bucle (eso ya pasa en el lado del rival).
                        if (enemigo == null || !enemigo.EstaViva || !enemigo.EsRecolector) return;
                        enemigo.Estado = flag == 1 ? EstadoUnidad.Recolectando : EstadoUnidad.Idle;
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
                        Unidad atacante = JugadorEnemigo.Unidades.FirstOrDefault(
                            u => u.PosicionX == ax && u.PosicionY == ay);
                        Edificio objetivo = JugadorLocal.Edificios.FirstOrDefault(
                            e => e.PosicionX == bx && e.PosicionY == by);
                        if (atacante == null || objetivo == null || !objetivo.EstaViva) return;
                        objetivo.RecibirDano(ataque); // misma fórmula de daño que en el lado del rival
                        GestorArchivos.RegistrarAccion(JugadorEnemigo.Nombre, "Red",
                            $"Rival atacó {objetivo.Tipo} ({ataque} de ataque).");
                        if (!objetivo.EstaViva)
                        {
                            JugadorLocal.EliminarEdificio(objetivo);
                            VerificarGanador();
                        }
                        break;
                    }
                }
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

        // Métodos virtuales: las pruebas pueden sobrescribirlos para no esperar el tiempo real.
        protected virtual Task EsperarEntrenamiento(int segundos, CancellationToken token)
        {
            return Task.Delay(segundos * 1000, token);
        }

        protected virtual Task EsperarConstruccion(int segundos)
        {
            return Task.Delay(segundos * 1000);
        }

        protected virtual int CicloRecoleccionMs => 1000;
    }
}