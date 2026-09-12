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

        // Candado compartido: todo cambio a recursos/listas pasa por aquí para
        // evitar condiciones de carrera cuando los hilos de recolección corren.
        private readonly object _lockJuego = new object();

        // Trabajos en segundo plano activos: para poder cancelarlos.
        private readonly Dictionary<TipoUnidad, CancellationTokenSource> _entrenamientosActivos =
            new Dictionary<TipoUnidad, CancellationTokenSource>();
        private readonly Dictionary<Unidad, CancellationTokenSource> _recolectoresActivos =
            new Dictionary<Unidad, CancellationTokenSource>();

        public JuegoControlador(string nombreJugador)
        {
            JugadorLocal = new Jugador(nombreJugador);
            JugadorEnemigo = new Jugador("Enemigo");
            Tablero = new Mapa();
            EstadoPartida = new Partida();

            // Inicialización: Centro Urbano y aldeanos iniciales en extremos opuestos.
            Edificio centroLocal = DatosDelJuego.CrearCentroUrbano(7, 1);
            Edificio centroEnemigo = DatosDelJuego.CrearCentroUrbano(7, 13);
            JugadorLocal.AgregarEdificio(centroLocal);
            JugadorEnemigo.AgregarEdificio(centroEnemigo);

            JugadorLocal.AgregarUnidad(PuebloInicial(JugadorLocal.Nombre, 6, 1));
            JugadorEnemigo.AgregarUnidad(PuebloInicial(JugadorEnemigo.Nombre, 6, 13));

            GestorArchivos.GuardarConfiguracionInicial(
                $"Jugador: {nombreJugador} | Mapa: {Mapa.Ancho}x{Mapa.Alto} | " +
                $"Centro local ({centroLocal.PosicionX},{centroLocal.PosicionY}) | " +
                $"Centro enemigo ({centroEnemigo.PosicionX},{centroEnemigo.PosicionY})");
            GestorArchivos.RegistrarAccion(nombreJugador, "Inicio", "Partida inicializada.");
        }

        private Unidad PuebloInicial(string nombre, int x, int y)
        {
            return DatosDelJuego.CrearUnidad(TipoUnidad.Aldeano, x, y);
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
                GestorArchivos.RegistrarAccion(
                    JugadorLocal.Nombre,
                    "Entrenar",
                    $"{tipo} listo en ({nueva.PosicionX},{nueva.PosicionY}).");
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

            while (true)
            {
                try { await Task.Delay(CicloRecoleccionMs, token); }
                catch (TaskCanceledException) { break; }

                lock (_lockJuego)
                {
                    if (!aldeano.EstaViva || recurso.EstaAgotado) break;
                    int cantidad = recurso.Extraer(cantidadPorCiclo);
                    if (cantidad <= 0) break;

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

        protected virtual int CicloRecoleccionMs => 1000;
    }
}