using System;
using Modelo;

namespace Controlador
{
    public class JuegoControlador
    {
        public Jugador JugadorLocal { get; private set; }
        public Jugador JugadorEnemigo { get; private set; }
        public Mapa Tablero { get; private set; }
        public Partida EstadoPartida { get; private set; }

        public JuegoControlador(string nombreJugador)
        {
            JugadorLocal = new Jugador(nombreJugador);
            JugadorEnemigo = new Jugador("Enemigo");
            Tablero = new Mapa();
            EstadoPartida = new Partida();

            GestorArchivos.GuardarConfiguracionInicial(
                $"Jugador: {nombreJugador} | Mapa: {Mapa.Ancho}x{Mapa.Alto}");
            GestorArchivos.RegistrarAccion(nombreJugador, "Inicio", "Partida inicializada.");
        }

        // 1. Mover Unidad
        public bool MoverUnidad(Unidad unidad, int nuevoX, int nuevoY)
        {
            if (unidad == null) return false;
            if (!Tablero.EsCoordenadaValida(nuevoX, nuevoY)) return false;

            int origenX = unidad.PosicionX;
            int origenY = unidad.PosicionY;
            unidad.PosicionX = nuevoX;
            unidad.PosicionY = nuevoY;

            GestorArchivos.RegistrarAccion(
                JugadorLocal.Nombre,
                "Mover",
                $"{unidad.Tipo} de ({origenX},{origenY}) a ({nuevoX},{nuevoY})");
            return true;
        }

        // 2. Construir Edificio (valida coordenadas, choque y recursos)
        public bool ConstruirEdificio(string tipo, int x, int y, int costoMadera)
        {
            if (!Tablero.EsCoordenadaValida(x, y)) return false;
            if (JugadorLocal.Madera < costoMadera) return false;
            if (JugadorLocal.Edificios.Exists(e => e.PosicionX == x && e.PosicionY == y))
                return false; // ya hay un edificio ahí

            JugadorLocal.Madera -= costoMadera;
            Edificio nuevoEdificio = DatosDelJuego.CrearEdificio(
                DatosDelJuego.ObtenerTipoEdificio(tipo), x, y);
            JugadorLocal.Edificios.Add(nuevoEdificio);

            GestorArchivos.RegistrarAccion(
                JugadorLocal.Nombre,
                "Construir",
                $"{tipo} en ({x},{y}). Madera restante: {JugadorLocal.Madera}");
            return true;
        }

        // 3. Atacar
        public void Atacar(Unidad atacante, Unidad enemigo)
        {
            if (atacante == null || enemigo == null) return;
            if (atacante.Vida <= 0) return; // una unidad muerta no ataca

            enemigo.Vida -= atacante.Ataque;
            GestorArchivos.RegistrarAccion(
                JugadorLocal.Nombre,
                "Ataque",
                $"{atacante.Tipo} infligió {atacante.Ataque} de daño a {enemigo.Tipo}.");

            if (enemigo.Vida <= 0)
            {
                JugadorEnemigo.Unidades.Remove(enemigo);
                GestorArchivos.RegistrarAccion(
                    JugadorLocal.Nombre,
                    "Ataque",
                    $"Impacto - {enemigo.Tipo} enemiga destruida");
                VerificarGanador();
            }
        }

        // 4. Verificación de Ganador (revisa a AMBOS jugadores)
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
            GestorArchivos.GuardarResultadoFinal($"¡Ganador: {ganador.Nombre}!");
        }
    }
}