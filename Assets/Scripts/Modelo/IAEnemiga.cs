using System;
using System.Threading;
using System.Threading.Tasks;

namespace Modelo
{
    //  IA ENEMIGA (PVE) — vive en el Modelo y corre en SU Task (concurrencia real).
    //
    //  Cada IntervaloDecisionMs decide y actúa sobre JugadorEnemigo:
    //    1. Economía: manda a sus aldeanos a recolectar el yacimiento más cercano.
    //    2. Construcción: Cuartel, Casas (comida pasiva) y Torres (defensa).
    //    3. Militar: ejército mixto (soldados, arqueros, caballeros).
    //    4. Reunión: en gracia mueve sus tropas al punto de reunión (sin pegar).
    //
    //  Las tropas militares que entrena quedan con ControladaPorIA=true y las
    //  mueve/ hace pelear el bucle de simulación de Simulacion (Nivel 1, lock).
    //
    //  Todas las mutaciones pasan por los métodos *IA / *Para de Simulacion,
    //  que ya toman lock(Candado). Esta clase no toca listas "peladas" sin candado.
    public class IAEnemiga
    {
        private readonly Simulacion _mundo;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private volatile bool _activa;

        // Cada cuántos ms la IA reevalúa su economía/militar (1000 = cada segundo).
        public int IntervaloDecisionMs { get; set; } = 2000;

        // Ejército objetivo por tipo (presión razonable y mixta).
        public int SoldadosObjetivo { get; set; } = 4;
        public int ArquerosObjetivo { get; set; } = 2;
        public int CaballerosObjetivo { get; set; } = 1;

        public bool Activa => _activa;
        public int DecisionesEjecutadas { get; private set; }

        public IAEnemiga(Simulacion mundo)
        {
            _mundo = mundo ?? throw new ArgumentNullException(nameof(mundo));
        }

        public void Iniciar()
        {
            if (_activa) return;
            _activa = true;
            Task tarea = BucleDecisionAsync();
            tarea.ContinueWith(
                t => GestorArchivos.RegistrarAccion(
                    "Sistema", "IA",
                    $"Bucle de IA terminado: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        public void Detener()
        {
            _activa = false;
            try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        private async Task BucleDecisionAsync()
        {
            while (_activa && !_cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(IntervaloDecisionMs, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    DecidirUnaVez();
                    DecisionesEjecutadas++;
                }
                catch (Exception ex)
                {
                    // Una decisión fallida no debe matar el bucle de la IA.
                    GestorArchivos.RegistrarAccion("Sistema", "IA",
                        $"Error de decisión ({ex.GetType().Name}): {ex.Message}");
                }
            }
        }

        // Una pasada de decisión. Cada llamada a *IA valida y aplica bajo lock(Candado).
        private void DecidirUnaVez()
        {
            if (_mundo.EstadoPartida == null || !_mundo.EstadoPartida.EnEjecucion) return;

            // 1) Economía: aldeanos ociosos → yacimiento adyacente o el más cercano.
            GestionarRecoleccion();

            // 2) Construcción: Cuartel, Casas y Torres (en ese orden de prisa).
            GestionarConstruccion();

            // 3) Militar: ejército mixto según objetivos por tipo.
            GestionarEntrenamiento();

            // 4) Reunión: en gracia, las tropas se concentran (mover sí, pegar no).
            GestionarReunion();
        }

        private void GestionarRecoleccion()
        {
            var foto = _mundo.Instantanea(); // copia segura bajo candado

            // Si le falta madera para el Cuartel, prioriza yacimientos de madera.
            // [PVE] Lee los recursos DEL ENEMIGO (no los del local de la instantánea).
            EdificioConfig cfgCuartel = DatosDelJuego.EdificiosBase[TipoEdificio.Cuartel];
            bool necesitaMadera;
            lock (_mundo.Candado)
            {
                necesitaMadera = _mundo.JugadorEnemigo.Madera < cfgCuartel.CostoMadera;
            }

            foreach (Unidad aldeano in foto.UnidadesEnemigo)
            {
                if (aldeano == null || !aldeano.EstaViva || !aldeano.EsRecolector) continue;
                if (_mundo.EstaRecolectando(aldeano)) continue;

                Recurso mejor = null;
                int mejorDist = int.MaxValue;
                foreach (Recurso r in foto.Recursos)
                {
                    if (r == null || r.EstaAgotado) continue;
                    if (necesitaMadera && r.Tipo != TipoRecurso.Madera) continue;

                    int d = Math.Abs(aldeano.PosicionX - r.PosicionX)
                          + Math.Abs(aldeano.PosicionY - r.PosicionY);
                    // En modo madera, desempata por cercanía; si no hay madera, any.
                    if (mejor == null || d < mejorDist)
                    {
                        mejorDist = d;
                        mejor = r;
                    }
                }

                // Si priorizaba madera y no quedan yacimientos, toma el más cercano de cualquier tipo.
                if (mejor == null && necesitaMadera)
                {
                    foreach (Recurso r in foto.Recursos)
                    {
                        if (r == null || r.EstaAgotado) continue;
                        int d = Math.Abs(aldeano.PosicionX - r.PosicionX)
                              + Math.Abs(aldeano.PosicionY - r.PosicionY);
                        if (mejor == null || d < mejorDist)
                        {
                            mejorDist = d;
                            mejor = r;
                        }
                    }
                }

                if (mejor == null) continue;

                // [Movimiento] Viaje completo: el aldeano camina solo hasta el
                // yacimiento (MoverARecolectarIA) y la recolección arranca al llegar
                // (LlegarADestino). Si ya está adyacente, empieza ya mismo.
                // Varias celdas alrededor del yacimiento → los aldeanos se apilan
                // juntos sin bloquearse (EsTransitable ignora unidades).
                _mundo.MoverARecolectarIA(aldeano, mejor);
            }
        }

        private void GestionarConstruccion()
        {
            var foto = _mundo.Instantanea();
            bool tieneCuartel = false;
            bool construyendoCuartel = false;
            int casas = 0, torres = 0;
            Edificio centro = null;

            foreach (Edificio e in foto.EdificiosEnemigo)
            {
                if (e.Tipo == TipoEdificio.CentroUrbano && e.EstaViva) centro = e;
                if (e.Tipo == TipoEdificio.Cuartel)
                {
                    if (e.Estado == EstadoEdificio.Operativo) tieneCuartel = true;
                    else construyendoCuartel = true;
                }
                if (e.Tipo == TipoEdificio.Casa && e.EstaViva) casas++;
                if (e.Tipo == TipoEdificio.Torre && e.EstaViva) torres++;
            }
            if (centro == null) return; // sin centro no puede expandirse con sentido

            // Orden de prisa: Cuartel → Casa → Torres (máx 2 y 2). ConstruirEdificioIA
            // gasta y revalida; si no alcanzan los recursos devuelve false y se
            // reintenta en la próxima decisión.
            if (!tieneCuartel && !construyendoCuartel)
            {
                (int X, int Y)? hueco = BuscarCasillaEdificableCerca(
                    centro.PosicionX, centro.PosicionY, radioMax: 5, TipoEdificio.Cuartel);
                if (hueco.HasValue)
                    _mundo.ConstruirEdificioIA(TipoEdificio.Cuartel, hueco.Value.X, hueco.Value.Y);
                return; // el Cuartel primero; lo demás espera
            }
            if (casas < 2)
            {
                (int X, int Y)? hueco = BuscarCasillaEdificableCerca(
                    centro.PosicionX, centro.PosicionY, radioMax: 6, TipoEdificio.Casa);
                if (hueco.HasValue
                    && _mundo.ConstruirEdificioIA(TipoEdificio.Casa, hueco.Value.X, hueco.Value.Y))
                    return;
            }
            if (tieneCuartel && torres < 2)
            {
                (int X, int Y)? hueco = BuscarCasillaEdificableCerca(
                    centro.PosicionX, centro.PosicionY, radioMax: 7, TipoEdificio.Torre);
                if (hueco.HasValue)
                    _mundo.ConstruirEdificioIA(TipoEdificio.Torre, hueco.Value.X, hueco.Value.Y);
            }
        }

        private void GestionarEntrenamiento()
        {
            var foto = _mundo.Instantanea();
            bool cuartelListo = false;
            foreach (Edificio e in foto.EdificiosEnemigo)
            {
                if (e.Tipo == TipoEdificio.Cuartel && e.EstaOperativo)
                {
                    cuartelListo = true;
                    break;
                }
            }
            if (!cuartelListo) return;

            int soldados = 0, arqueros = 0, caballeros = 0;
            foreach (Unidad u in foto.UnidadesEnemigo)
            {
                if (!u.EstaViva || u.EsRecolector) continue;
                if (u.Tipo == TipoUnidad.Soldado) soldados++;
                else if (u.Tipo == TipoUnidad.Arquero) arqueros++;
                else if (u.Tipo == TipoUnidad.Caballero) caballeros++;
            }

            // Un slot por tipo en paralelo: infantería, luego apoyo a distancia,
            // luego caballería si hay con qué (si falta, el intento falla y vuelve).
            if (soldados < SoldadosObjetivo)
                _mundo.EntrenarUnidadIA(TipoUnidad.Soldado, TipoEdificio.Cuartel);
            if (arqueros < ArquerosObjetivo)
                _mundo.EntrenarUnidadIA(TipoUnidad.Arquero, TipoEdificio.Cuartel);
            if (caballeros < CaballerosObjetivo)
                _mundo.EntrenarUnidadIA(TipoUnidad.Caballero, TipoEdificio.Cuartel);
        }

        // Reunión en gracia: las tropas se concentran junto a su centro (mover
        // sí vale en gracia; pegar no: el tick de combate las frena). Así el
        // ejército sale formado y no desperdigado por el mapa.
        private void GestionarReunion()
        {
            if (_mundo.EstadoPartida.TiempoJuegoSegundos >= _mundo.GraciaMilitarSegundos) return;
            var foto = _mundo.Instantanea();
            Edificio centro = null;
            foreach (Edificio e in foto.EdificiosEnemigo)
                if (e.Tipo == TipoEdificio.CentroUrbano && e.EstaViva) { centro = e; break; }
            if (centro == null) return;

            int i = 0;
            foreach (Unidad u in foto.UnidadesEnemigo)
            {
                if (u == null || !u.EstaViva || u.EsRecolector || !u.ControladaPorIA) continue;
                // Punto de reunión en anillo alrededor del centro (reparto).
                int mx = centro.PosicionX + 4 + (i % 3) - 1;
                int my = centro.PosicionY + 4 + (i / 3) % 3 - 1;
                i++;
                if (u.TieneDestino) continue; // ya marcha a algún lado
                if (Math.Abs(u.PosicionX - mx) + Math.Abs(u.PosicionY - my) <= 3) continue;
                _mundo.MoverUnidadIA(u, mx, my);
            }
        }

        // Busca en anillos alrededor de (cx,cy) un ancla donde quepa la HUELLA
        // del tipo pedido (libre, sin yacimiento).
        private (int X, int Y)? BuscarCasillaEdificableCerca(int cx, int cy, int radioMax, TipoEdificio tipo)
        {
            int lado = DatosDelJuego.LadoSegunTipo(tipo);
            for (int radio = 1; radio <= radioMax; radio++)
            {
                for (int dx = -radio; dx <= radio; dx++)
                {
                    for (int dy = -radio; dy <= radio; dy++)
                    {
                        if (Math.Abs(dx) != radio && Math.Abs(dy) != radio) continue; // solo borde del anillo
                        int x = cx + dx;
                        int y = cy + dy;
                        if (!EsCasillaEdificableIA(x, y, lado)) continue;
                        return (x, y);
                    }
                }
            }
            return null;
        }

        private bool EsCasillaEdificableIA(int x, int y, int lado)
        {
            // Consulta barata sobre el mundo vivo; Simulacion.Tablero es POCO y
            // EsAreaEdificable solo lee listas. Para mayor rigor se podría lockear,
            // pero aquí la decisión es best-effort: ConstruirEdificioIA revalida.
            return _mundo.Tablero.EsAreaEdificable(x, y, lado, _mundo.JugadorLocal, _mundo.JugadorEnemigo);
        }
    }
}
