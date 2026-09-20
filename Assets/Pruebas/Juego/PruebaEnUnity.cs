using UnityEngine;
using Controlador;
using Modelo;

//  PRUEBA DE INTEGRACIÓN: verificar que el Modelo + Controlador funcionan DENTRO
//  de Unity (capa Vista vacía todavía; es la misma llamada que usará la Vista).
//
//  Cómo usarla (solo en modo edición):
//    1) GameObject -> Create Empty
//    2) Add Component -> PruebaEnUnity
//    3) Play y mirar la Console. La prueba avisa cuando termina y sale sola.
//
//  Sigue el PATRÓN de la Vista: GestorArchivos a persistentDataPath, una
//  Instantanea() por frame, ProcesarMensajesRedPendientes() en Update y
//  Detener() al salir.
public class PruebaEnUnity : MonoBehaviour
{
    private JuegoControlador ctrl;
    private bool batallaIniciada;
    private bool finalizada;
    private float ultimoInforme;
    private float tiempoPrueba;

    private const float InformeCadaSegundos = 2f;
    private const float TimeoutSegundos = 90f;

    void Start()
    {
        GestorArchivos.CarpetaDestino = Application.persistentDataPath;

        ctrl = new JuegoControlador("Johan", true);

        // Estado inicial real del mundo.
        var foto = ctrl.Instantanea();
        Debug.Log($"[PRUEBA] Mundo creado. Local: {foto.UnidadesLocal.Count}U/{foto.EdificiosLocal.Count}E | " +
                  $"Enemigo: {foto.UnidadesEnemigo.Count}U/{foto.EdificiosEnemigo.Count}E | " +
                  $"Recursos O{foto.Oro} M{foto.Madera} C{foto.Comida} | Ejecucion={foto.EnEjecucion}");

        // 1) Mover el aldeano local.
        var aldeano = foto.UnidadesLocal.Find(u => u.Tipo == TipoUnidad.Aldeano && u.EstaViva);
        bool movido = aldeano != null && ctrl.MoverUnidad(aldeano, 6, 2);
        Debug.Log($"[PRUEBA] MoverUnidad({aldeano?.Tipo}) a (6,2): {movido}");

        // 2) Construir un Cuartel (el Modelo lo va terminando en segundo plano).
        bool construyendo = ctrl.ConstruirEdificio(TipoEdificio.Cuartel, 9, 2);
        Debug.Log($"[PRUEBA] ConstruirEdificio(Cuartel, 9, 2) aceptado: {construyendo}");

        // 3) Sembrar un item y recogerlo con el aldeano (ItemsVisibles en acción).
        bool itemColocado = ctrl.ColocarItem(TipoItem.Yogur, 2, 3, false);
        Debug.Log($"[PRUEBA] ColocarItem(Yogur, 2, 3): {itemColocado}");
        if (itemColocado)
        {
            Item yogur = null;
            foreach (var it in ctrl.ItemsVisibles)
                if (it.Tipo == TipoItem.Yogur && !it.Recogido) yogur = it;
            bool recogido = yogur != null && aldeano != null && ctrl.RecogerItem(aldeano, yogur);
            Debug.Log($"[PRUEBA] RecogerItem(Yogur): {recogido}");
        }

        // 4) Modo batalla: la concurrencia SE VE (bucle + candado + IA).
        int colocadas = ctrl.IniciarBatalla(8);
        batallaIniciada = colocadas > 0;
        Debug.Log($"[PRUEBA] IniciarBatalla(8) colocó {colocadas}u por lado. Bucle={ctrl.BucleBatallaActivo}");
        ultimoInforme = Time.time;
    }

    void Update()
    {
        if (ctrl == null || finalizada) return;

        // Patrón de la Vista: red + instantánea por frame.
        ctrl.ProcesarMensajesRedPendientes();
        var foto = ctrl.Instantanea();
        tiempoPrueba += Time.deltaTime;

        // Informe periódico para ver avanzar la simulación.
        if (Time.time - ultimoInforme >= InformeCadaSegundos)
        {
            ultimoInforme = Time.time;
            int vivasL = 0, vivasE = 0;
            foreach (var u in foto.UnidadesLocal) if (u.EstaViva) vivasL++;
            foreach (var u in foto.UnidadesEnemigo) if (u.EstaViva) vivasE++;
            Debug.Log($"[BATALLA] t={ctrl.TicksSimulados} ticks | vivas L/E={vivasL}/{vivasE} | " +
                      $"bajas L/E={ctrl.BajasLocal}/{ctrl.BajasEnemigo}");
        }

        // Fin natural: el bucle se apaga solo cuando un flanco cae.
        if (batallaIniciada && !ctrl.BucleBatallaActivo)
        {
            finalizada = true;
            Debug.Log($"[PRUEBA] Batalla terminó sola. Ganador={foto.GanadorNombre} | " +
                      $"ticks={ctrl.TicksSimulados} | bajas L/E={ctrl.BajasLocal}/{ctrl.BajasEnemigo}");
            Debug.Log("[PRUEBA] OK: Modelo+Controlador funcionan dentro de Unity.");
            Terminar();
        }
        else if (tiempoPrueba >= TimeoutSegundos)
        {
            finalizada = true;
            Debug.LogWarning($"[PRUEBA] Timeout de {TimeoutSegundos}s: batalla sin terminar " +
                             $"(bajas L/E={ctrl.BajasLocal}/{ctrl.BajasEnemigo}). Cerrando igual.");
            Terminar();
        }
    }

    void OnApplicationQuit() => ctrl?.Detener();

    private void Terminar()
    {
        ctrl.Detener();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}