# GUÍA DE LA VISTA — "Imperios en Guerra"

<style>
  @page { margin: 10mm 12mm; }
  body { font-family: "Segoe UI", system-ui, sans-serif; line-height: 1.5; }
  h1, h2, h3, h4 { line-height: 1.25; }
  pre { background: #f6f8fa; border: 1px solid #d0d7de; border-radius: 6px; padding: 10px 12px; overflow-x: auto; }
  code { background: #f6f8fa; border-radius: 4px; padding: 1px 5px; }
  pre code { background: none; padding: 0; }
  table { border-collapse: collapse; }
  th, td { border: 1px solid #d0d7de; padding: 6px 10px; }
  th { background: #f6f8fa; }
  @media print {
    body { font-size: 11pt; line-height: 1.35; }
    p, li { margin: 3px 0; }
    h1 { margin: 0 0 8px; }
    h2 { margin: 14px 0 6px; }
    h3 { margin: 10px 0 4px; }
    h1, h2, h3, h4 { break-after: avoid; page-break-after: avoid; }
    pre, table, img { margin: 5px 0; break-inside: avoid; page-break-inside: avoid; }
  }
</style>

> **Regla de oro MVC:** la Vista **solo pinta** (lee la instantánea) y **llama al Controlador** (traduce clics del usuario a acciones). Nunca toca el Modelo directamente. Nunca crea `Task`, `Thread` ni `lock`.

---

## 0. Por qué la Vista es "tonta" (y cómo funciona MVC aquí)

```
┌──────────────────────────────────────────────────────────────────┐
│  MODELO (Simulacion.cs + todo Assets/Scripts/Modelo/)            │
│  → Toda la concurrencia vive AQUÍ:                               │
│     · lock(Candado)  · Task + CancellationToken                  │
│     · ConcurrentQueue · IniciarRelojAsync()                      │
│     · IniciarBucleSimulacionAsync()  · IniciarSpawnerItemsAsync()│
│     · ConstruccionTaskAsync() · EntrenamientoTaskAsync()         │
│     · RecoleccionTaskAsync()  · ExpiracionCascoAsync()           │
└────────────────────┬─────────────────────────────────────────────┘
                     │  expone solo: Instantanea(), métodos de acción
                     ▼
┌──────────────────────────────────────────────────────────────────┐
│  CONTROLADOR (JuegoControlador.cs)                               │
│  → Puente DELGADO. CERO Task, CERO Thread, CERO lock.           │
│     · Traduce acciones Vista → Modelo                            │
│     · Traduce mensajes de red → métodos espejo del Modelo        │
│     · Anuncia por red + escribe logs                             │
│     · Expone métricas de batalla y estado de red                 │
└────────────────────┬─────────────────────────────────────────────┘
                     │  devuelve: InstantaneaJuego, bool, métricas
                     ▼
┌──────────────────────────────────────────────────────────────────┐
│  VISTA (Assets/Scripts/Vista/ — MonoBehaviours de Unity)        │
│  → SOLO PINTA. Una llamada por frame a Instantanea().            │
│     · Dibuja sprites/UI desde la foto (no desde el Modelo vivo) │
│     · Reacciona a Input → llama al Controlador                   │
│     · Llama ProcesarMensajesRedPendientes() en Update()          │
└──────────────────────────────────────────────────────────────────┘
```

---

## 1. El esqueleto: MonoBehaviour raíz

Crea **`Assets/Scripts/Vista/GestorJuego.cs`**. Este es el único script que tiene el `JuegoControlador` y que llama a sus métodos en cada frame.

```csharp
using UnityEngine;
using Controlador;
using Modelo;

// Adjuntar a: GameObject vacío "GestorJuego" en la escena principal.
public class GestorJuego : MonoBehaviour
{
    // ── Referencia al controlador (toda la API pasa por aquí) ──
    private JuegoControlador ctrl;

    // ── Nombres (configurar desde el Inspector o pantalla de inicio) ──
    [SerializeField] private string nombreJugador = "Jugador1";
    [SerializeField] private bool soyElHost = true;

    // ── Referencias a los sub-MonoBehaviours de la Vista ──
    [SerializeField] private HudRecursos hudRecursos;
    [SerializeField] private HudRed hudRed;
    [SerializeField] private HudBatalla hudBatalla;
    [SerializeField] private VistaTablero vistaTablero;
    [SerializeField] private PanelFinPartida panelFin;

    void Start()
    {
        // ❶ Inicializa el destino de los archivos de log en la carpeta de Unity
        GestorArchivos.CarpetaDestino = Application.persistentDataPath;

        // ❷ Crea el motor completo (Modelo + concurrencia arranca aquí)
        ctrl = new JuegoControlador(nombreJugador, localArriba: soyElHost);

        // ❸ Pasa el controlador a los sub-componentes de la Vista
        hudRecursos.Inicializar(ctrl);
        hudRed.Inicializar(ctrl);
        hudBatalla.Inicializar(ctrl);
        vistaTablero.Inicializar(ctrl);
        panelFin.Inicializar(ctrl);
    }

    void Update()
    {
        if (ctrl == null) return;

        // ❹ PATRÓN OBLIGATORIO: procesar red ANTES de leer la instantánea
        ctrl.ProcesarMensajesRedPendientes();

        // ❺ Foto segura del mundo (una sola por frame)
        InstantaneaJuego foto = ctrl.Instantanea();

        // ❻ Cada sub-componente de la Vista se actualiza con la foto
        hudRecursos.Actualizar(foto);
        hudRed.Actualizar(ctrl);
        hudBatalla.Actualizar(ctrl);
        vistaTablero.Actualizar(foto);
        panelFin.Actualizar(foto);
    }

    void OnDestroy()
    {
        // ❼ SIEMPRE llamar Detener() al salir: cancela Tasks y cierra el socket
        ctrl?.Detener();
    }
}
```

> **Importante:** `ProcesarMensajesRedPendientes()` va **antes** de `Instantanea()` en cada frame. Así los mensajes del rival ya están aplicados cuando se pinta el mundo.

---

## 2. La API completa del Controlador (lo que la Vista puede usar)

### 2.1 Crear el controlador

```csharp
// Host (lado de arriba del mapa): localArriba = true
ctrl = new JuegoControlador("Jugador1", localArriba: true);

// Cliente (lado de abajo del mapa): localArriba = false
ctrl = new JuegoControlador("Jugador2", localArriba: false);
```

### 2.2 Leer el mundo (InstantaneaJuego)

Llama `ctrl.Instantanea()` **una sola vez por frame**. El objeto devuelto es una copia segura.

```csharp
InstantaneaJuego foto = ctrl.Instantanea();

// Unidades y edificios
foto.UnidadesLocal      // List<Unidad>  — tus tropas
foto.UnidadesEnemigo    // List<Unidad>  — las del rival
foto.EdificiosLocal     // List<Edificio>
foto.EdificiosEnemigo   // List<Edificio>
foto.Recursos           // List<Recurso> — yacimientos en el mapa
foto.Items              // List<Item>    — items sembrados en el mapa

// HUD de recursos del jugador local
foto.Oro                // int
foto.Madera             // int
foto.Comida             // int
foto.Hierro             // int  ← nuevo recurso
foto.Piedra             // int  ← nuevo recurso

// Estado de la partida
foto.TiempoJuegoSegundos  // int
foto.EnEjecucion           // bool
foto.GanadorNombre         // string (null si la partida sigue)
```

### 2.3 Acciones del jugador → Controlador → Modelo

Todos devuelven `bool`: `true` = acción aceptada, `false` = rechazada (sin recursos, casilla ocupada, etc.).

```csharp
// Mover unidad
ctrl.MoverUnidad(unidad, nuevoX, nuevoY);      // → bool

// Construir edificio (se paga del catálogo y se envía por red)
ctrl.ConstruirEdificio(TipoEdificio.Cuartel, x, y);   // → bool

// Entrenar unidad en un edificio
ctrl.EntrenarUnidad(TipoUnidad.Soldado, TipoEdificio.Cuartel);  // → bool

// Recolección (el Modelo lo hace en segundo plano)
ctrl.IniciarRecoleccion(aldeano, recurso);    // → bool
ctrl.DetenerRecoleccion(aldeano);             // → bool
ctrl.EstaRecolectando(aldeano);               // → bool (para colorear el sprite)

// Atacar
ctrl.Atacar(miUnidad, unidadEnemiga);         // → bool
ctrl.AtacarEdificio(miUnidad, edificioEnemigo); // → bool

// Items
ctrl.ColocarItem(TipoItem.Yogur, x, y, enviarPorRed: false); // → bool (host)
ctrl.RecogerItem(miUnidad, item);             // → bool

// Verificar ganador manualmente (el Modelo lo hace solo; esto es por si acaso)
ctrl.VerificarGanador();
```

### 2.4 Red

```csharp
// Modo host: abrir puerto y esperar rival
ctrl.HospedarRed(puerto: 5505);   // → bool

// Modo cliente: conectar a IP del host
ctrl.ConectarRed("192.168.1.10", puerto: 5505);  // → bool

// Estado de la red (para el HUD)
ctrl.RedPartida?.EstaConectado   // bool
ctrl.NombreRivalRed              // string (nombre del rival cuando ya se conectó)
ctrl.MensajesDescartados         // int (desync si > 0)

// OBLIGATORIO en Update(): procesar mensajes entrantes y drena la cola de salida
ctrl.ProcesarMensajesRedPendientes();  // → int (mensajes procesados ese frame)
```

### 2.5 Modo batalla (demo de concurrencia)

```csharp
// Sembrar N unidades IA por lado y encender el bucle concurrente
int colocadas = ctrl.IniciarBatalla(8);  // 8 unidades por lado

// Detener el bucle
ctrl.DetenerBatalla();

// Leer métricas para el HUD
ctrl.BucleBatallaActivo  // bool
ctrl.TicksSimulados      // int
ctrl.BajasLocal          // int
ctrl.BajasEnemigo        // int
```

### 2.6 Apagar todo

```csharp
// Llamar SIEMPRE al salir de la escena / OnDestroy
ctrl.Detener();
```

---

## 3. Componentes de la Vista — uno por responsabilidad

### 3.1 `HudRecursos.cs` — barra de recursos del jugador

```csharp
// Mostrar: Oro, Madera, Comida, Hierro, Piedra + Tiempo de partida
using UnityEngine;
using UnityEngine.UI;
using Controlador;
using Modelo;

public class HudRecursos : MonoBehaviour
{
    [SerializeField] private Text txtOro, txtMadera, txtComida, txtHierro, txtPiedra, txtTiempo;
    private JuegoControlador ctrl;

    public void Inicializar(JuegoControlador c) => ctrl = c;

    public void Actualizar(InstantaneaJuego foto)
    {
        txtOro.text    = $"Oro: {foto.Oro}";
        txtMadera.text = $"Madera: {foto.Madera}";
        txtComida.text = $"Comida: {foto.Comida}";
        txtHierro.text = $"Hierro: {foto.Hierro}";   // ← nuevo
        txtPiedra.text = $"Piedra: {foto.Piedra}";   // ← nuevo

        int min = foto.TiempoJuegoSegundos / 60;
        int seg = foto.TiempoJuegoSegundos % 60;
        txtTiempo.text = $"{min:00}:{seg:00}";
    }
}
```

### 3.2 `HudRed.cs` — estado de conexión TCP

```csharp
// Mostrar: conectado/desconectado, nombre rival, mensajes descartados (desync)
public class HudRed : MonoBehaviour
{
    [SerializeField] private Text txtEstadoRed, txtRival, txtDesync;
    [SerializeField] private Image iconoRed; // verde = conectado, rojo = desconectado
    [SerializeField] private Color colorConectado = Color.green;
    [SerializeField] private Color colorDesconectado = Color.red;
    private JuegoControlador ctrl;

    public void Inicializar(JuegoControlador c) => ctrl = c;

    public void Actualizar(JuegoControlador c)
    {
        bool conectado = c.RedPartida != null && c.RedPartida.EstaConectado;
        iconoRed.color = conectado ? colorConectado : colorDesconectado;
        txtEstadoRed.text = conectado ? "Red: OK" : "Red: sin conexión";
        txtRival.text = string.IsNullOrEmpty(c.NombreRivalRed)
            ? "Esperando rival..."
            : $"Rival: {c.NombreRivalRed}";
        txtDesync.text = c.MensajesDescartados > 0
            ? $"⚠ Desync: {c.MensajesDescartados}"
            : "";
    }
}
```

### 3.3 `HudBatalla.cs` — métricas del modo concurrencia masiva

```csharp
// Mostrar: ticks, bajas locales/enemigas, unidades vivas
public class HudBatalla : MonoBehaviour
{
    [SerializeField] private GameObject panelBatalla; // activar solo en modo batalla
    [SerializeField] private Text txtTicks, txtBajasL, txtBajasE;
    private JuegoControlador ctrl;

    public void Inicializar(JuegoControlador c) => ctrl = c;

    public void Actualizar(JuegoControlador c)
    {
        panelBatalla.SetActive(c.BucleBatallaActivo);
        if (!c.BucleBatallaActivo) return;

        txtTicks.text  = $"Ticks: {c.TicksSimulados}";
        txtBajasL.text = $"Bajas locales: {c.BajasLocal}";
        txtBajasE.text = $"Bajas enemigas: {c.BajasEnemigo}";
    }
}
```

### 3.4 `VistaTablero.cs` — pintar el mapa 30×30

```csharp
// Pintar unidades, edificios, recursos e items desde la instantánea.
// Usa un pool de GameObjects para no crear/destruir cada frame.
using System.Collections.Generic;
using UnityEngine;
using Modelo;
using Controlador;

public class VistaTablero : MonoBehaviour
{
    // ── Sprites por tipo (asignar en Inspector) ──────────────────
    [SerializeField] private Sprite[] spritesUnidad;   // índice = (int)TipoUnidad
    [SerializeField] private Sprite[] spritesEdificio; // índice = (int)TipoEdificio
    [SerializeField] private Sprite[] spritesRecurso;  // índice = (int)TipoRecurso
    [SerializeField] private Sprite   spriteItem;

    // ── Prefabs para los marcadores ──────────────────────────────
    [SerializeField] private GameObject prefabMarcador;
    [SerializeField] private float tamanoCasilla = 1f;  // unidades de mundo por casilla

    // ── Colores de equipo ────────────────────────────────────────
    [SerializeField] private Color colorLocal   = Color.blue;
    [SerializeField] private Color colorEnemigo = Color.red;

    private JuegoControlador ctrl;
    // Pool sencillo: reusamos los GameObjects que ya existen en escena
    private readonly List<GameObject> _pool = new List<GameObject>();
    private int _usoActual;

    public void Inicializar(JuegoControlador c) => ctrl = c;

    public void Actualizar(InstantaneaJuego foto)
    {
        _usoActual = 0;

        // Unidades locales
        foreach (var u in foto.UnidadesLocal)
            DibujarMarcador(u.PosicionX, u.PosicionY,
                spritesUnidad[(int)u.Tipo], colorLocal,
                BarraVida(u.Vida, u.VidaMaxima));

        // Unidades enemigas
        foreach (var u in foto.UnidadesEnemigo)
            DibujarMarcador(u.PosicionX, u.PosicionY,
                spritesUnidad[(int)u.Tipo], colorEnemigo,
                BarraVida(u.Vida, u.VidaMaxima));

        // Edificios locales
        foreach (var e in foto.EdificiosLocal)
            DibujarMarcador(e.PosicionX, e.PosicionY,
                spritesEdificio[(int)e.Tipo], colorLocal,
                BarraVida(e.Vida, e.VidaMaxima),
                e.Estado == EstadoEdificio.EnConstruccion ? 0.5f : 1f);

        // Edificios enemigos
        foreach (var e in foto.EdificiosEnemigo)
            DibujarMarcador(e.PosicionX, e.PosicionY,
                spritesEdificio[(int)e.Tipo], colorEnemigo,
                BarraVida(e.Vida, e.VidaMaxima),
                e.Estado == EstadoEdificio.EnConstruccion ? 0.5f : 1f);

        // Recursos (yacimientos)
        foreach (var r in foto.Recursos)
            if (!r.EstaAgotado)
                DibujarMarcador(r.PosicionX, r.PosicionY,
                    spritesRecurso[(int)r.Tipo], Color.white);

        // Items del mapa
        foreach (var i in foto.Items)
            DibujarMarcador(i.PosicionX, i.PosicionY, spriteItem, Color.yellow);

        // Desactivar los marcadores sobrantes del pool
        for (int k = _usoActual; k < _pool.Count; k++)
            _pool[k].SetActive(false);
    }

    // ── Helpers ─────────────────────────────────────────────────

    private void DibujarMarcador(int x, int y, Sprite sp, Color color,
                                  float vida = 1f, float alfa = 1f)
    {
        GameObject go = ObtenerDelPool();
        go.transform.position = new Vector3(x * tamanoCasilla, y * tamanoCasilla, 0);

        SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = sp;
        sr.color  = new Color(color.r, color.g, color.b, alfa);

        // Barra de vida (opcional: usar un child con SpriteRenderer o una UI Slider)
        // go.transform.Find("BarraVida")?.localScale = new Vector3(vida, 0.1f, 1);
    }

    private GameObject ObtenerDelPool()
    {
        if (_usoActual < _pool.Count)
        {
            _pool[_usoActual].SetActive(true);
            return _pool[_usoActual++];
        }
        GameObject nuevo = Instantiate(prefabMarcador, transform);
        _pool.Add(nuevo);
        _usoActual++;
        return nuevo;
    }

    private static float BarraVida(int vida, int vidaMax) =>
        vidaMax > 0 ? (float)vida / vidaMax : 0f;
}
```

### 3.5 `ControlInputUsuario.cs` — traducir clics a acciones del Controlador

```csharp
// Adjuntar al mismo GestorJuego o a un GameObject "InputManager".
// Aquí se leen los clics del jugador y se llama al Controlador.
using UnityEngine;
using Controlador;
using Modelo;

public class ControlInputUsuario : MonoBehaviour
{
    private JuegoControlador ctrl;
    private Unidad unidadSeleccionada;
    private Edificio edificioSeleccionado;

    public void Inicializar(JuegoControlador c) => ctrl = c;

    void Update()
    {
        if (ctrl == null) return;

        // ── Selección por clic izquierdo ─────────────────────────────────
        if (Input.GetMouseButtonDown(0))
            ManejarSeleccion();

        // ── Acción por clic derecho (mover / atacar) ─────────────────────
        if (Input.GetMouseButtonDown(1) && unidadSeleccionada != null)
            ManejarAccionDerecha();

        // ── Atajos de teclado (entrenar / construir) ─────────────────────
        if (Input.GetKeyDown(KeyCode.Q)) ctrl.EntrenarUnidad(TipoUnidad.Aldeano,  TipoEdificio.CentroUrbano);
        if (Input.GetKeyDown(KeyCode.W)) ctrl.EntrenarUnidad(TipoUnidad.Soldado,  TipoEdificio.Cuartel);
        if (Input.GetKeyDown(KeyCode.E)) ctrl.EntrenarUnidad(TipoUnidad.Arquero,  TipoEdificio.Cuartel);
        if (Input.GetKeyDown(KeyCode.R)) ctrl.EntrenarUnidad(TipoUnidad.Caballero,TipoEdificio.Cuartel);
        if (Input.GetKeyDown(KeyCode.Alpha1)) ctrl.ConstruirEdificio(TipoEdificio.Casa,       0, 0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) ctrl.ConstruirEdificio(TipoEdificio.Cuartel,    0, 0);
        if (Input.GetKeyDown(KeyCode.Alpha3)) ctrl.ConstruirEdificio(TipoEdificio.Torre,      0, 0);
        if (Input.GetKeyDown(KeyCode.Alpha4)) ctrl.ConstruirEdificio(TipoEdificio.CentroUrbano, 0, 0);

        // ── Red ──────────────────────────────────────────────────────────
        if (Input.GetKeyDown(KeyCode.H)) ctrl.HospedarRed();
        if (Input.GetKeyDown(KeyCode.J)) ctrl.ConectarRed("127.0.0.1");

        // ── Demo batalla masiva ──────────────────────────────────────────
        if (Input.GetKeyDown(KeyCode.B)) ctrl.IniciarBatalla(8);
        if (Input.GetKeyDown(KeyCode.N)) ctrl.DetenerBatalla();
    }

    private void ManejarSeleccion()
    {
        Vector2 punto = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        // Aquí detectas qué unidad/edificio hay en esa casilla usando la foto,
        // o con un Collider2D en cada sprite del pool de VistaTablero.
        // Ejemplo simplificado:
        unidadSeleccionada = null; // reemplazar con lógica de Raycast
    }

    private void ManejarAccionDerecha()
    {
        Vector2 punto = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        int x = Mathf.RoundToInt(punto.x);
        int y = Mathf.RoundToInt(punto.y);
        ctrl.MoverUnidad(unidadSeleccionada, x, y);
    }
}
```

### 3.6 `PanelFinPartida.cs` — pantalla de resultado

```csharp
using UnityEngine;
using UnityEngine.UI;
using Modelo;
using Controlador;

public class PanelFinPartida : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Text txtGanador;
    [SerializeField] private Button btnReintentar;
    private JuegoControlador ctrl;

    public void Inicializar(JuegoControlador c)
    {
        ctrl = c;
        panel.SetActive(false);
        btnReintentar.onClick.AddListener(Reiniciar);
    }

    public void Actualizar(InstantaneaJuego foto)
    {
        if (foto.GanadorNombre == null) return;
        panel.SetActive(true);
        txtGanador.text = $"¡Ganó {foto.GanadorNombre}!";
    }

    private void Reiniciar()
    {
        ctrl?.Detener();
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }
}
```

---

## 4. Costos del catálogo (para tooltips / UI de construcción)

La Vista puede leer `DatosDelJuego` directamente para mostrar los costos antes de construir/entrenar. El Controlador los valida al ejecutar la acción.

### Unidades

| Unidad | Comida | Oro | Madera | Hierro | Piedra | Tiempo |
|--------|--------|-----|--------|--------|--------|--------|
| Aldeano | 50 | — | — | — | — | 10 s |
| Soldado | 60 | 30 | — | 20 | — | 20 s |
| Arquero | 50 | 40 | 20 | 10 | — | 25 s |
| Caballero | 80 | 60 | — | 50 | — | 30 s |

### Edificios

| Edificio | Madera | Oro | Hierro | Piedra | Tiempo |
|----------|--------|-----|--------|--------|--------|
| Casa | 50 | — | — | 20 | 15 s |
| Torre | 150 | 100 | — | 250 | 25 s |
| Cuartel | 200 | 50 | 50 | 100 | 30 s |
| Centro Urbano | 350 | — | — | 200 | 40 s |

```csharp
// Ejemplo: mostrar tooltip de costo antes de construir
using Modelo;

var config = DatosDelJuego.EdificiosBase[TipoEdificio.Cuartel];
string tooltip = $"Cuartel — Madera:{config.CostoMadera} Oro:{config.CostoOro} " +
                 $"Hierro:{config.CostoHierro} Piedra:{config.CostoPiedra} " +
                 $"({config.TiempoConstruccionSegundos}s)";
```

---

## 5. Recursos en el mapa (yacimientos iniciales)

Los recursos vienen en `foto.Recursos`. La Vista los pinta según su tipo y puede mostrar cuánto queda:

```csharp
foreach (var r in foto.Recursos)
{
    if (r.EstaAgotado) continue;   // no pintar yacimientos vacíos
    // r.Tipo         → TipoRecurso (Madera/Oro/Comida/Hierro/Piedra)
    // r.Cantidad     → int (lo que queda)
    // r.PosicionX/Y  → casilla en el mapa
}
```

**Posiciones iniciales en el mapa 30×30:**

| Tipo | Posición | Cantidad inicial |
|------|----------|-----------------|
| Madera | (3,3) | 500 |
| Oro | (11,11) | 500 |
| Comida | (7,7) | 300 |
| Madera | (12,3) | 400 |
| Oro | (3,12) | 400 |
| Comida | (11,6) | 300 |
| Hierro | (2,8) | 300 |
| Hierro | (12,8) | 300 |
| Piedra | (8,2) | 300 |
| Piedra | (8,12) | 300 |

---

## 6. Items del mapa (objetos realistas)

El host los siembra solo (concurrencia). El cliente los ve a través del espejo de red. La Vista solo los pinta desde `foto.Items`.

```csharp
foreach (var item in foto.Items)
{
    // item.Tipo      → TipoItem (Yogur/Casco/Espada/Herramientas)
    // item.PosicionX/Y
    // item.Recogido  → bool (true = ya fue tomado, no pintarlo)
}
```

**Efectos (para tooltips):**

| Item | Efecto |
|------|--------|
| Yogur | +50 vida a todas las tropas propias |
| Casco | +10 defensa por 10 segundos |
| Espada | +5 ataque permanente a la unidad que la lleva |
| Herramientas | +5% de recolección permanente |

Para recoger un item con una unidad (debe estar adyacente):
```csharp
ctrl.RecogerItem(unidadSeleccionada, itemCercano);
```

---

## 7. Protocolo de red (referencia para el HUD y logs)

La Vista puede mostrar los mensajes que fluyen por la red. Están todos en el `log_partida.txt` (escrito automáticamente por `GestorArchivos`).

```
MOVER;xOrigen;yOrigen;xNuevo;yNuevo
ATACAR;xAtacante;yAtacante;xObjetivo;yObjetivo;dano
ATACAR_EDIFICIO;xAtacante;yAtacante;xEdificio;yEdificio;ataque
CONSTRUIR;Tipo;x;y
ENTRENAR;Tipo;x;y
SALUDO;nombre
RECOLECTAR;x;y;1|0
ITEM;TipoItem;x;y
RECOGER_ITEM;TipoItem;x;y
FIN;ganador
```

---

## 8. Escena Unity recomendada

```
Jerarquía de escena sugerida:
├── GestorJuego          ← GestorJuego.cs + ControlInputUsuario.cs
├── Canvas
│   ├── HudRecursos      ← HudRecursos.cs  (panel arriba-izq)
│   ├── HudRed           ← HudRed.cs       (panel arriba-der)
│   ├── HudBatalla       ← HudBatalla.cs   (panel centro, solo en batalla)
│   └── PanelFinPartida  ← PanelFinPartida.cs (modal, oculto por defecto)
├── TableroRoot          ← VistaTablero.cs (prefab marcador como hijo)
│   └── Marcador         ← prefab: SpriteRenderer + opcional BarraVida
└── Camara Principal     ← centrada en (7,7), ortográfica, size ~9
```

**Configuración de Cámara:**
```
Position: (14.5, 14.5, -10)
Projection: Orthographic
Size: 15.5  (mapa 30×30 centrado)
```

---

## 9. Pasos en orden para implementar la Vista

- [ ] **Paso 1** — Crear la escena y el GameObject `GestorJuego` con `GestorJuego.cs`. Verificar que la partida arranca (log en Console).
- [ ] **Paso 2** — `HudRecursos`: mostrar los 5 recursos + tiempo. Verificar que cambian al recolectar.
- [ ] **Paso 3** — `VistaTablero`: pintar unidades y edificios con sprites de colores. Verificar posiciones del mapa.
- [ ] **Paso 4** — `ControlInputUsuario`: mover una unidad con clic izquierdo (la unidad CAMINA hasta el destino; el clic derecho también actúa). Verificar el log `Mover`.
- [ ] **Paso 5** — Entrenar unidades con atajos de teclado (Q/W/E/R). Verificar que aparecen tras el tiempo de espera.
- [ ] **Paso 6** — Construir edificios (1/2/3/4). Verificar barra de progreso de construcción.
- [ ] **Paso 7** — Items: pintarlos en el mapa y permitir recogerlos con clic.
- [ ] **Paso 8** — `HudRed`: botón Hospedar (H) y Conectar (J). Probar con `PruebaEnUnity.cs`.
- [ ] **Paso 9** — `HudBatalla`: botón Batalla (B) y ver el bucle concurrente en pantalla.
- [ ] **Paso 10** — `PanelFinPartida`: cuando `foto.GanadorNombre != null`, mostrar el ganador.

---

## 10. Recordatorio: qué NO debe hacer la Vista

| ❌ NUNCA | ✅ EN SU LUGAR |
|----------|---------------|
| `new Task(...)` / `Thread` / `lock` | Leer `foto` (copia ya segura) |
| Acceder a `ctrl.Motor.*` directamente | Usar `ctrl.Instantanea()` |
| Modificar `Jugador`, `Unidad`, `Edificio` | Llamar al método del `ctrl` |
| Llamar `Simulacion` directamente | Todo pasa por `JuegoControlador` |
| Leer `ctrl.JugadorLocal.Unidades` sin `Instantanea()` | Siempre `foto.UnidadesLocal` |
| Olvidar `ctrl.Detener()` al salir | Siempre en `OnDestroy()` |
