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
        public bool ConstruirEdificio(TipoEdificio tipo, int x, int y)
        {
            if (!Tablero.EsCoordenadaValida(x, y)) return false;
            if (Tablero.CasillaTieneRecurso(x, y)) return false;
            if (!Tablero.EsCasillaLibre(x, y, JugadorLocal, JugadorEnemigo)) return false;

            EdificioConfig config = DatosDelJuego.EdificiosBase[tipo];

            if (!JugadorLocal.PuedePagar(config.CostoMadera, config.CostoOro, config.CostoComida))
                return false;

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

        // 3. Atacar
        public void Atacar(Unidad atacante, Unidad enemigo)
        {
            if (atacante == null || enemigo == null) return;
            if (!atacante.PuedeAtacar) return;

            enemigo.RecibirDano(atacante.Ataque);

            int danoReal = Math.Max(0, atacante.Ataque - enemigo.Defensa);
            GestorArchivos.RegistrarAccion(
                JugadorLocal.Nombre,
                "Ataque",
                $"{atacante.Tipo} infligió {danoReal} de daño a {enemigo.Tipo}.");

            if (!enemigo.EstaViva)
            {
                JugadorEnemigo.Unidades.Remove(enemigo);
                GestorArchivos.RegistrarAccion(
                    JugadorLocal.Nombre,
                    "Ataque",
                    $"Impacto - {enemigo.Tipo} enemiga destruida");
                VerificarGanador();
            }
        }

        // 4. Entrenar Unidad (valida edificio, entrenabilidad, coords y recursos)
        public bool EntrenarUnidad(Edificio edificio, TipoUnidad tipo, int x, int y)
        {
            if (edificio == null) return false;
            if (!JugadorLocal.Edificios.Contains(edificio)) return false;
            if (!edificio.PuedeEntrenar(tipo)) return false;
            if (!Tablero.EsCoordenadaValida(x, y)) return false;
            if (!Tablero.EsCasillaLibre(x, y, JugadorLocal, JugadorEnemigo)) return false;

            UnidadConfig config = DatosDelJuego.UnidadesBase[tipo];
            if (!JugadorLocal.Gastar(config.CostoMadera, config.CostoOro, config.CostoComida))
                return false;

            Unidad nuevaUnidad = DatosDelJuego.CrearUnidad(tipo, x, y);
            JugadorLocal.AgregarUnidad(nuevaUnidad);

            GestorArchivos.RegistrarAccion(
                JugadorLocal.Nombre,
                "Entrenar",
                $"{tipo} entrenado en {edificio.Tipo}. Posición ({x},{y}).");
            return true;
        }

        // 5. Recolectar Recurso (valida aldeano, distancia y agotamiento)
        public bool RecolectarRecursos(Unidad recolector, Recurso recurso, int cantidad)
        {
            if (recolector == null || recurso == null) return false;
            if (!recolector.EsRecolector) return false;
            if (!recolector.EstaViva) return false;
            if (cantidad <= 0) return false;
            if (recurso.EstaAgotado) return false;

            bool adyacenteAlRecurso =
                Math.Abs(recolector.PosicionX - recurso.PosicionX) <= 1 &&
                Math.Abs(recolector.PosicionY - recurso.PosicionY) <= 1;
            if (!adyacenteAlRecurso) return false;

            int extraido = recurso.Extraer(cantidad);
            if (extraido <= 0) return false;

            switch (recurso.Tipo)
            {
                case TipoRecurso.Madera: JugadorLocal.Recibir(extraido, 0, 0); break;
                case TipoRecurso.Oro: JugadorLocal.Recibir(0, extraido, 0); break;
                case TipoRecurso.Comida: JugadorLocal.Recibir(0, 0, extraido); break;
            }

            GestorArchivos.RegistrarAccion(
                JugadorLocal.Nombre,
                "Recolectar",
                $"{recolector.Tipo} recolectó {extraido} de {recurso.Tipo}." +
                $" Oro: {JugadorLocal.Oro}, Madera: {JugadorLocal.Madera}, Comida: {JugadorLocal.Comida}");
            return true;
        }

        // 6. Verificación de Ganador (revisa a AMBOS jugadores)
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