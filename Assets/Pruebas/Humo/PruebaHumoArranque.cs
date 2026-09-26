using System;
using UnityEngine;
using Vista;

// Humo headless: verifica que el juego REAL arranca en Unity (Play) con la
// escena Juego: GestorJuego + Controlador(API) + IA en PVE + foto del mundo +
// menú de red + sprites descargados.
//
// SOLO se activa si hay argumento "-humo" en la línea de comandos:
//   RTS.exe -batchmode -nographics -humo -logFile humo.log
// En Play normal (sin -humo) no hace nada.
public class PruebaHumoArranque : MonoBehaviour
{
    private static bool _registrado;
    private static int _fallos;
    private float _inicio = -1f;
    private const float EsperarSegundos = 5f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoRegistrar()
    {
        if (_registrado) return;
        bool humo = false;
        foreach (string a in Environment.GetCommandLineArgs())
            if (a == "-humo") { humo = true; break; }
        if (!humo) return;
        _registrado = true;
        var go = new GameObject("PruebaHumoArranque");
        DontDestroyOnLoad(go);
        go.AddComponent<PruebaHumoArranque>();
    }

    private void Update()
    {
        if (_inicio < 0f) _inicio = Time.realtimeSinceStartup;
        // El menú inicial bloquea la IA: se elige VS MÁQUINA nada más arrancar.
        var gestorMenu = UnityEngine.Object.FindAnyObjectByType<GestorJuego>();
        if (gestorMenu != null && gestorMenu.MenuInicio != null && gestorMenu.MenuInicio.Abierto)
        {
            gestorMenu.MenuInicio.ElegirPve();
            _inicio = Time.realtimeSinceStartup;
            return;
        }
        if (Time.realtimeSinceStartup - _inicio < EsperarSegundos) return;
        enabled = false;
        try { Verificar(); }
        catch (Exception ex) { Fallo("excepción: " + ex.Message); }
        if (_fallos == 0) Debug.Log("[HUMO] TODO OK");
        else Debug.LogError($"[HUMO] {_fallos} FALLO(S)");
        Application.Quit();
    }

    private static void Verificar()
    {
        var gestor = UnityEngine.Object.FindAnyObjectByType<GestorJuego>();
        Chequeo(gestor != null, "hay GestorJuego");
        if (gestor == null) return;
        Chequeo(gestor.Controlador != null, "hay Controlador (API)");
        if (gestor.Controlador == null) return;
        Chequeo(gestor.Controlador.IAActiva, "PVE con IA activa");
        Chequeo(gestor.Controlador.RedPartida == null, "PVE sin red");
        Chequeo(gestor.UltimaFoto != null, "hay foto del mundo");
        Chequeo(gestor.UltimaFoto != null && gestor.UltimaFoto.UnidadesLocal.Count > 0,
            "mundo con unidades");
        Chequeo(gestor.MenuInicio != null && !gestor.MenuInicio.Abierto, "menú inicial cerrado tras elegir");
        var ed = ArteRecursos.CargarEdificios();
        Chequeo(ed != null && ed.Length == 4, "4 sprites de edificios");
        var rec = ArteRecursos.CargarRecursos();
        Chequeo(rec != null && rec.Length == 5, "5 sprites de recursos");
        Chequeo(ArteRecursos.CargarTile() != null, "tile de césped");
    }

    private static void Chequeo(bool cond, string nombre)
    {
        if (cond) Debug.Log("[HUMO] OK: " + nombre);
        else { _fallos++; Debug.LogError("[HUMO] FALLO: " + nombre); }
    }

    private static void Fallo(string msg)
    {
        _fallos++;
        Debug.LogError("[HUMO] FALLO: " + msg);
    }
}
