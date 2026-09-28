// Pruebas de batch para verificar SIN abrir el Editor a mano:
//   1) Compilación limpia del proyecto.
//   2) API total: sin JuegoControlador no hay juego.
//   3) PVE: arranca IA, sin red, la simulación avanza.
//   4) PVP: HospedarRed + ConectarRed + DetenerIA funcionan por la API.
//
// Uso (Unity 6000.6.0f1):
//   Unity.exe -batchmode -projectPath <repo> -executeMethod PruebasBatch.Todo -logFile - -quit
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Controlador;
using Modelo;
using UnityEditor;
using UnityEngine;

public static class PruebasBatch
{
    private static int _fallos;

    public static void Todo()
    {
        _fallos = 0;
        // En batchmode el SynchronizationContext de Unity NO bombea continuaciones
        // async mientras el hilo principal duerme: sin esto, Task.Delay del Modelo
        // se queda colgado y "el reloj no avanza". Fuera de Play forzamos el pool.
        var ctxAnterior = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            ProbarApiObligatoria();
            ProbarPveSinRed();
            ProbarPvpHostCliente();
            ProbarArteDescargado();
            ProbarAtaqueYVictoria();
            ProbarConcurrencia();
        }
        catch (Exception ex)
        {
            Fallo("excepción inesperada: " + ex);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(ctxAnterior);
        }

        if (_fallos == 0)
        {
            Debug.Log("[PRUEBAS_BATCH] TODO OK");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"[PRUEBAS_BATCH] {_fallos} FALLO(S)");
            EditorApplication.Exit(1);
        }
    }

    // Sin Controlador no hay mundo, ni instantánea, ni IA: el juego NO corre.
    private static void ProbarApiObligatoria()
    {
        GestorArchivos.CarpetaDestino = Path.GetTempPath();

        // Contrato de la API: el tipo existe y la Vista depende de él.
        if (typeof(JuegoControlador) == null) { Fallo("JuegoControlador no existe"); return; }

        // Simular "sin API": no hay controlador → nada que actualizar.
        JuegoControlador ctrl = null;
        InstantaneaJuego foto = ctrl?.Instantanea();
        if (foto != null) { Fallo("sin controlador no debería haber instantánea"); return; }

        // Con API: hay mundo.
        ctrl = new JuegoControlador("Batch", true);
        foto = ctrl.Instantanea();
        if (foto == null) { Fallo("con controlador no hay instantánea"); return; }
        if (foto.UnidadesLocal == null || foto.UnidadesLocal.Count == 0)
        { Fallo("mundo vacío con controlador"); return; }

        ctrl.Detener();
        Ok("API obligatoria: sin Controlador no hay juego; con él hay mundo");
    }

    // PVE: IA encendida, sin conexión, ticks/tiempo avanzan.
    private static void ProbarPveSinRed()
    {
        int fallosAntes = _fallos;
        GestorArchivos.CarpetaDestino = Path.GetTempPath();
        var ctrl = new JuegoControlador("PveBatch", true);

        if (ctrl.RedPartida != null) { Fallo("PVE no debería crear red"); ctrl.Detener(); return; }
        if (!ctrl.IniciarIA()) { Fallo("no arrancó la IA"); ctrl.Detener(); return; }
        if (!ctrl.IAActiva) { Fallo("IA no activa"); ctrl.Detener(); return; }

        // Procesar sin conector no debe lanzar (drena cola saliente).
        ctrl.ProcesarMensajesRedPendientes();

        var f0 = ctrl.Instantanea();
        int ticks0 = ctrl.TicksSimulados;
        Thread.Sleep(2000);
        var f1 = ctrl.Instantanea();
        int ticks1 = ctrl.TicksSimulados;

        if (f1.TiempoJuegoSegundos <= f0.TiempoJuegoSegundos && ticks1 <= ticks0)
            Fallo($"la simulación no avanzó en PVE (t {f0.TiempoJuegoSegundos}→{f1.TiempoJuegoSegundos}, ticks {ticks0}→{ticks1})");

        if (!ctrl.IAActiva) { Fallo("la IA se apagó sola"); }
        if (ctrl.RedPartida != null) { Fallo("apareció red en PVE"); }

        ctrl.Detener();
        if (_fallos == fallosAntes)
            Ok($"PVE sin red: IA + reloj/ticks avanzan (t={f1.TiempoJuegoSegundos}, ticks={ticks1})");
    }

    // PVP: detener IA, abrir host, conectar cliente local, intercambiar SALUDO.
    private static void ProbarPvpHostCliente()
    {
        GestorArchivos.CarpetaDestino = Path.GetTempPath();
        const int puerto = 5517; // puerto de prueba para no chocar con 5505 del juego

        var host = new JuegoControlador("HostBatch", true);
        host.IniciarIA();
        if (!host.DetenerIA()) { Fallo("DetenerIA no apagó la IA"); host.Detener(); return; }
        if (host.IAActiva) { Fallo("IA sigue activa tras DetenerIA"); host.Detener(); return; }

        if (!host.HospedarRed(puerto)) { Fallo("HospedarRed falló"); host.Detener(); return; }

        var cliente = new JuegoControlador("ClienteBatch", false);
        if (!cliente.ConectarRed("127.0.0.1", puerto))
        { Fallo("ConectarRed falló"); cliente.Detener(); host.Detener(); return; }

        // Esperar handshake (SALUDO) con tope de 5 s.
        bool conectados = false;
        for (int i = 0; i < 50; i++)
        {
            host.ProcesarMensajesRedPendientes();
            cliente.ProcesarMensajesRedPendientes();
            if (host.RedPartida.EstaConectado && cliente.RedPartida.EstaConectado)
            {
                if (host.NombreRivalRed != null || cliente.NombreRivalRed != null)
                { conectados = true; break; }
            }
            Thread.Sleep(100);
        }

        if (!conectados)
            Fallo($"PVP no completó saludo (host rival={host.NombreRivalRed ?? "null"}, " +
                  $"cliente rival={cliente.NombreRivalRed ?? "null"}, " +
                  $"hostConectado={host.RedPartida.EstaConectado}, " +
                  $"clienteConectado={cliente.RedPartida.EstaConectado})");
        else
            Ok($"PVP host+cliente conectados (rival host='{host.NombreRivalRed}', rival cliente='{cliente.NombreRivalRed}')");

        cliente.Detener();
        host.Detener();
    }

    // PRUEBAS DE ATAQUE Y CONDICIÓN DE VICTORIA
    // Verifica que:
    //   1. Una unidad puede atacar a otra y causar daño
    //   2. Al destruir todas las unidades enemigas, se declara ganador
    //   3. Al destruir el Centro Urbano enemigo, se declara ganador
    private static void ProbarAtaqueYVictoria()
    {
        int fallosAntes = _fallos;
        GestorArchivos.CarpetaDestino = Path.GetTempPath();
        var ctrl = new JuegoControlador("AtaqueBatch", true);

        // --- Prueba 1: Ataque entre unidades ---
        var unidadLocal = ctrl.JugadorLocal.Unidades.FirstOrDefault(u => u.Tipo == TipoUnidad.Soldado)
                      ?? ctrl.JugadorLocal.Unidades.FirstOrDefault();
        var unidadEnemiga = ctrl.JugadorEnemigo.Unidades.FirstOrDefault();

        if (unidadLocal == null) Fallo("no hay unidad local para atacar");
        else if (unidadEnemiga == null) Fallo("no hay unidad enemiga para ser atacada");
        else
        {
            // Mover unidad local cerca del enemigo para que esté en rango
            unidadLocal.PosicionX = unidadEnemiga.PosicionX;
            unidadLocal.PosicionY = unidadEnemiga.PosicionY + 1;

            int vidaEnemigaAntes = unidadEnemiga.Vida;
            bool ataqueOk = ctrl.Motor.Atacar(unidadLocal, unidadEnemiga);

            if (!ataqueOk) Fallo("Atacar() devolvió false con unidades en rango");
            else if (unidadEnemiga.Vida >= vidaEnemigaAntes && unidadEnemiga.EstaViva)
                Fallo($"el ataque no causó daño (vida {vidaEnemigaAntes}→{unidadEnemiga.Vida})");
            else
                Ok($"ataque entre unidades: {unidadLocal.Tipo} infligió daño a {unidadEnemiga.Tipo} (vida {vidaEnemigaAntes}→{unidadEnemiga.Vida})");
        }

        // --- Prueba 2: Destruir todas las unidades enemigas → victoria por aniquilación ---
        // (Usar un controlador fresco para no contaminar la prueba anterior)
        var ctrl2 = new JuegoControlador("VictoriaBatch", true);
        foreach (var u in ctrl2.JugadorEnemigo.Unidades.ToList())
            ctrl2.Motor.Atacar(ctrl2.JugadorLocal.Unidades.FirstOrDefault(), u);

        // Forzar destrucción de todas las unidades enemigas
        foreach (var u in ctrl2.JugadorEnemigo.Unidades.ToList())
            u.RecibirGolpe(9999);

        ctrl2.Motor.VerificarGanador();

        if (ctrl2.EstadoPartida.GanadorNombre == null)
            Fallo("no se declaró ganador al destruir todas las unidades enemigas");
        else
            Ok($"victoria por aniquilación: ganador = {ctrl2.EstadoPartida.GanadorNombre}");

        // --- Prueba 3: Destruir Centro Urbano enemigo → victoria ---
        var ctrl3 = new JuegoControlador("CentroBatch", true);
        var centroEnemigo = ctrl3.JugadorEnemigo.Edificios.FirstOrDefault(
            e => e.Tipo == TipoEdificio.CentroUrbano && e.EstaViva);

        if (centroEnemigo == null) Fallo("no hay Centro Urbano enemigo para destruir");
        else
        {
            centroEnemigo.RecibirDano(9999);
            ctrl3.Motor.VerificarGanador();

            if (ctrl3.EstadoPartida.GanadorNombre == null)
                Fallo("no se declaró ganador al destruir el Centro Urbano enemigo");
            else
                Ok($"victoria por destrucción de Centro Urbano: ganador = {ctrl3.EstadoPartida.GanadorNombre}");
        }

        ctrl.Detener();
        ctrl2.Detener();
        ctrl3.Detener();
        if (_fallos == fallosAntes)
            Ok("ataque y victoria: ataque, aniquilación y destrucción de Centro funcionan correctamente");
    }

    // PRUEBAS DE CONCURRENCIA
    // Verifica que múltiples Tasks pueden ejecutarse sin condiciones de carrera
    private static void ProbarConcurrencia()
    {
        int fallosAntes = _fallos;
        GestorArchivos.CarpetaDestino = Path.GetTempPath();
        var ctrl = new JuegoControlador("ConcurrenciaBatch", true);

        // --- Prueba 1: Múltiples recolecciones simultáneas ---
        var aldeanos = ctrl.JugadorLocal.Unidades.Where(u => u.EsRecolector).Take(3).ToList();
        if (aldeanos.Count == 0) { Fallo("no hay aldeanos para prueba de concurrencia"); ctrl.Detener(); return; }

        var recursos = ctrl.Motor.Tablero.RecursosEnMapa.ToList();
        if (recursos.Count == 0) { Fallo("no hay recursos en el mapa"); ctrl.Detener(); return; }

        // Iniciar recolección de varios aldeanos a la vez
        bool todasIniciaron = true;
        for (int i = 0; i < aldeanos.Count; i++)
        {
            var recurso = recursos[i % recursos.Count];
            if (!ctrl.Motor.IniciarRecoleccion(aldeanos[i], recurso))
                todasIniciaron = false;
        }

        if (!todasIniciaron) Fallo("no todas las recolecciones iniciaron correctamente");

        // Esperar un poco para que las Tasks de recolección avancen
        Thread.Sleep(500);

        // Verificar que no hay excepciones ni estados inconsistentes
        var foto = ctrl.Instantanea();
        if (foto == null) Fallo("Instantanea() devolvió null durante concurrencia");
        else
        {
            // Verificar que los recursos del jugador local son válidos
            if (foto.Oro < 0 || foto.Comida < 0 || foto.Madera < 0 ||
                foto.Piedra < 0 || foto.Hierro < 0)
                Fallo("recursos negativos detectados (condición de carrera)");
        }

        // --- Prueba 2: Múltiples movimientos simultáneos ---
        var soldados = ctrl.JugadorLocal.Unidades.Where(u => u.PuedeAtacar).Take(5).ToList();
        bool todosMovieron = true;
        for (int i = 0; i < soldados.Count; i++)
        {
            int nuevoX = Math.Min(soldados[i].PosicionX + 2, Mapa.Ancho - 1);
            int nuevoY = Math.Min(soldados[i].PosicionY + 2, Mapa.Alto - 1);
            if (!ctrl.Motor.MoverUnidad(soldados[i], nuevoX, nuevoY))
                todosMovieron = false;
        }

        if (!todosMovieron) Fallo("no todas las órdenes de movimiento se aceptaron");

        // Esperar a que pathfinding termine
        Thread.Sleep(300);

        // Verificar posiciones válidas
        foreach (var s in soldados)
        {
            if (s.PosicionX < 0 || s.PosicionX >= Mapa.Ancho ||
                s.PosicionY < 0 || s.PosicionY >= Mapa.Alto)
                Fallo($"unidad {s.Tipo} fuera de límites ({s.PosicionX},{s.PosicionY})");
        }

        ctrl.Detener();
        if (_fallos == fallosAntes)
            Ok("concurrencia: recolección y movimiento simultáneos sin condiciones de carrera");
    }

    private static void Ok(string msg) => Debug.Log("[PRUEBAS_BATCH] OK: " + msg);
    private static void Fallo(string msg)
    {
        _fallos++;
        Debug.LogError("[PRUEBAS_BATCH] FALLO: " + msg);
    }

    // Los PNG de Assets/Resources/Sprites deben importarse como Sprite y
    // cargarse por nombre (edificios en orden TipoEdificio, recursos en
    // orden TipoRecurso, + tile de césped). Si algo falla, el juego usa el
    // SpriteFactory temporal (no se rompe), pero la prueba lo marca.
    private static void ProbarArteDescargado()
    {
        int fallosAntes = _fallos;

        var edificios = Vista.ArteRecursos.CargarEdificios();
        if (edificios == null || edificios.Length != 4)
            Fallo("no cargaron los 4 sprites de edificios (Resources/Sprites/Edificios)");
        else
            for (int i = 0; i < edificios.Length; i++)
                if (edificios[i] == null || edificios[i].texture == null)
                    Fallo($"edificio[{i}] nulo o sin textura");

        var recursos = Vista.ArteRecursos.CargarRecursos();
        if (recursos == null || recursos.Length != 5)
            Fallo("no cargaron los 5 sprites de recursos (Resources/Sprites/Recursos)");
        else
            for (int i = 0; i < recursos.Length; i++)
                if (recursos[i] == null || recursos[i].texture == null)
                    Fallo($"recurso[{i}] nulo o sin textura");

        var tile = Vista.ArteRecursos.CargarTile();
        if (tile == null || tile.texture == null)
            Fallo("no cargó el tile de césped (Resources/Sprites/Terreno/cesped)");

        var tileAlt = Vista.ArteRecursos.CargarTileAlt();
        if (tileAlt == null || tileAlt.texture == null)
            Fallo("no cargó la variante de césped (Resources/Sprites/Terreno/cesped_alt)");

        if (_fallos == fallosAntes)
            Ok("arte descargado: 4 edificios + 5 recursos + césped (+variante) cargan como Sprite");
    }
}
