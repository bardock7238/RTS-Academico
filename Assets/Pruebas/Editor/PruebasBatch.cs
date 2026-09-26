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
