# AVANCE DEL PROYECTO — "Imperios en Guerra" (RTS en Unity)

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
    .mermaid, svg { max-width: 100% !important; height: auto; page-break-inside: avoid; margin: 6px 0 !important; }
  }
</style>

---

## Datos clave

- **Proyecto:** RTS académico "Imperios en Guerra" (inspirado en Age of Empires).
- **Motor:** Unity 6000.6.0f1. **Lenguaje:** C#. **Rama git:** `modelo` (y `main`, en el mismo commit).
- **Repositorio:** `https://github.com/bardock7238/RTS-Academico.git`.
- **Fecha límite (avance):** 22 de septiembre.
- **Patrón exigido por la guía:** MVC, concurrencia con hilos, red, archivos de log.

## Reparto del trabajo

- **Modelo + Controlador:** `Assets/Scripts/Modelo/` y `Assets/Scripts/Controlador/`.
- **Vista:** escena Unity (sprites, escenas, prefabs).

---

## Estado actual (última sesión: 21-sep)

### MODO PVE + VISTA MÍNIMA JUGABLE (21-sep)

Sesión completa de PVE + Vista mínima (decisión del usuario: PVE + red opcional,
IA económica+militar, Controlador mantiene API, arte a cargo del compañero):

- **Modelo (descongelado solo para PVE)**:
  - `Simulacion.cs` generalizado a ambos jugadores: `MoverUnidadPara`/`ConstruirEdificioPara`/`EntrenarUnidadPara`/`IniciarRecoleccionPara` + variantes `*IA`; dict de entrenamientos rekeyed `(Jugador, TipoUnidad)`; `EntregarRecurso(Jugador, …)`; recolección por dueño.
  - **`Modelo/IAEnemiga.cs`** (nuevo): Task de decisión cada `IntervaloDecisionMs` (2000 ms) — economiza (madera si le falta para el Cuartel), construye Cuartel cerca del Centro Urbano, entrena Soldados hasta `PresionMilitarObjetivo=4`. Todo muta vía `*IA` (bajo `lock(Candado)`).
  - API PVE: `IniciarIA()`, `IAActiva`, `IntervaloIaMs`; `_ia?.Detener()` en `Detener()`.
- **Controlador**: fachada `IniciarIA()` / `IAActiva`; `EnviarPorRed` sale temprano si `RedPartida == null` (drena cola sin red; evita fuga).
- **Vista (5 scripts, MVC estricto)**:
  - `GestorJuego`: raíz con `RuntimeInitializeOnLoadMethod`, `ConstruirUiSiFalta` (cámara → EventSystem → tablero → canvas/HUD → input → panel fin), expone `VistaTablero`.
  - `VistaTablero`: pool de sprites, rejilla 30×30 redibujada cada frame (bug de slots corregido), campos `[SerializeField] Sprite[]` para el arte del compañero (issues #4–#9).
  - `HudRecursos`: HUD de 5 recursos + tiempo + mensajes; fallback de Canvas corregido.
  - `ControlInputUsuario`: clics y teclas QWER (entrenar) / **Alt+QWER** (seleccionar todas las vivas de ese tipo) / 1-4 (construir) / **C** (modo recoger: clic en yacimiento o item → la unidad camina sola; autoselecciona aldeano; 2.ª C = lo más cercano) / **I** (`RecogerItemCercano`) / Esc → API del Controlador.
  - `PanelFinPartida`: modal de fin + Reintentar (`LoadScene`; `Detener()` solo en `OnDestroy` del Gestor).
- **Escena**: `Assets/Escenas/Juego.unity` (cámara ortográfica centrada en el mapa, size 15.5 + placeholder); registrada en `ProjectSettings/EditorBuildSettings.asset`.
- **Verificación**: Unity batch sin `error CS` (`Exiting batchmode successfully`); suites de escritorio **rts-pve-test 18 OK** y **rts-red-test 8 OK, 0 fallos**.
- **Issues para el compañero**: **#4–#9** (label `vista`).
- **Docs/diagramas**: `DOCUMENTACION.md` sincronizado (clase `IAEnemiga`, API PVE, clases Vista, secuencia 3.6, concurrencia 8 Tasks, casos de uso PVE); `README.md` actualizado (PVE + controles).

### MOVIMIENTO CAMINANDO + SPRITES + PULIDO (21-sep, tarde — sesión de juego)

Feedback del usuario jugando en el Editor (nada de teletransportar, mapa grande,
todo con clic izquierdo, ítems con clic) + permiso total para pulir Modelo/API
y añadir arte temporal:

- **MOVIMIENTO CON DESTINO (nadie se teletransporta)**:
  - `Unidad`: campos `DestinoX/DestinoY`, `ItemAlLlegar`, `RecursoAlLlegar`, `TicksBloqueoDestino` + métodos `FijarDestino`/`LimpiarDestino`.
  - `MoverUnidadPara` fija el destino (valida coordenada + casilla libre) y la unidad **CAMINA 1 casilla por latido** (100 ms) vía `AvanzarDestinos()` dentro del bucle de simulación — corre SIEMPRE que la partida esté viva, con o sin batalla.
  - **Pathfinding BFS 4 direcciones** (`SiguientePasoBFS`): esquiva unidades y edificios (p. ej. rodear el Centro Urbano en la fila 1); sin camino transitable → contador de bloqueo y cancelación del viaje a los ~50 latidos.
  - **Viajes con acción al llegar**: `MoverARecogerItem` (recoge el ítem solo al llegar) y `MoverARecolectar`/`MoverARecolectarIA` (lanza la recolección al llegar a una casilla libre junto al yacimiento). `LlegarADestino` ejecuta el pendiente y anuncia por red (`RECOGER_ITEM;…` / `RECOLECTAR;…;1`).
  - `CancelarDestino` (API del Modelo + Controlador; 1.ª tecla Escape corta el viaje y mantiene la selección). Las órdenes de ataque/caminar cortan viajes y recolecciones en curso (anunciando `RECOLECTAR;…;0` si hace falta).
  - **Espejo de red**: `MoverUnidadRival` también fija destino → AMBAS copias caminan al mismo ritmo; los viajes a ítem/yacimiento anuncian `MOVER` al salir para que el rival camine su copia.
  - **IA puleada**: `MoverARecolectarIA` — el aldeano enemigo viaja SOLO hasta el yacimiento (antes daba 1 paso cada 2 s en mapas grandes).
  - **Empate técnico** (`VerificarFinBatalla`): si el modo batalla se queda sin NINGÚN combatiente vivo en ambos bandos (p. ej. 6v6 simétricos que se exterminan y solo quedan aldeanos), el bucle se apaga en vez de latir para siempre; con IA activa no se apaga (la máquina repone).
- **API NUEVA (Controlador)**: `MoverARecogerItem`, `MoverARecolectar`, `CancelarDestino`.
- **VISTA (solo lo autorizado: sprites + fluidez)**:
  - **`Vista/SpriteFactory.cs` (nuevo)**: pixel-art 16×16 **generado por código** — 4 unidades (aldeano/soldado/arquero/caballero), 4 edificios, 5 recursos, 4 ítems y tile — como arte TEMPORAL hasta que el compañero aporte el definitivo (issues #4–#9); cualquier sprite enlazado en el Inspector se respeta.
  - `VistaTablero`: carga automática del arte si los slots están vacíos; ítems con **sprite por tipo** (tinte blanco para no destiñir el arte); **suavizado de movimiento** (interpolación `MoveTowards` a 10 celdas/s entre latidos → el sprite se desliza, no salta) + poda de unidades ya desaparecidas.
  - Cámara: ortográfica size **15.5** centrada en el mapa 30×30.
- **Mapa 30×30 + ~17 yacimientos** (4 cuadrantes + central), spawner de ítems cada 15 s con `MaxItemsEnMapa = 8`, controles unificados a **clic izquierdo** (el derecho también actúa), botones `Recoger [C]` y `Item [I]` en la barra (1040 px); C = modo dual yacimiento/item con autoselección de aldeano; clic directo en yacimiento con aldeano también recolecta (sin modo previo).
- **Verificación**: compilación COMPLETA (Modelo+Controlador+Vista) contra las DLLs de Unity 6000.6.0f1 → **0 errores, 0 avisos**; suites de escritorio **rts-pve-test 23 OK, 0 fallos** (incluye: caminar rodeando obstáculos, no teletransporta, cancelar viaje, recoger ítem al llegar, recolección por viaje, empate de batalla) y **rts-red-test 8 OK, 0 fallos**.

### COLISIONES APILABLES + ATAQUE CON APROXIMACIÓN (21-sep, noche)

Feedback del usuario jugando: "cuando me atacaban no me pegaban" y "los aldeanos se
bloquean entre sí; quiero que 10 aldeanos se apilen y se vean épicos":

- **Modelo / `Mapa.EsTransitable`**: nueva regla de camino — solo los **edificios**
  bloquean; las **unidades no** (pueden compartir casilla). Se usa en:
  `IntentarPasoA`, `SiguientePasoBFS`, `IntentarPaso` (IA), `MoverUnidadPara`
  (destino), `CasillaLibreJuntoA`, `ObtenerPosicionDeSalida`, espejo `MoverUnidadRival`
  y spawn de batalla. `EsCasillaLibre` se mantiene para **construir/colocar items**
  (ahí sí importa quién pisa la casilla).
- **Efecto**: multitudes de aldeanos ya no se tapan el paso; todos llegan al mismo
  yacimiento y quedan **apilados**; las tropas avanzan sin estancarse en corredores.
- **Combate del jugador (`MoverAAtacar`)**: clic en enemigo → si está en rango pega
  (con `TiempoEsperaAtaque`); si no, fija `Objetivo` + destino a la casilla del rival
  y **camina solo** hasta estar a golpe (`ProcesarObjetivoDe` en el latido de
  movimiento, con o sin `BucleActivo`). Así "clic = atacar" siempre termina en golpes.
- **IA**: `ProcesarUnidadTactica` ya no exige solo `ControladaPorIA` para el
  enfriamiento; las unidades del jugador solo pelean si el jugador fijó objetivo.
- **Vista (`DibujarUnidad`)**: cuenta unidades por casilla y las dibuja en un
  **círculo con offset** (radio ~0.28–0.34, escala ligeramente menor si hay pila)
  → 10 aldeanos en la misma casilla se ven todos, "épicos", no uno tapando al otro.
- **Controlador**: API `MoverAAtacar(Unidad, Unidad)`; input usa ese flujo en vez
  de `Atacar` directo (que solo servía si ya estabas en rango).
- **Enfriamiento unificado**: `Atacar`/`AtacarEdificio` ponen `TiempoEsperaAtaque`;
  baja en `AvanzarDestinos` y en `ProcesarUnidadTactica`.
- **Verificación**: suites de escritorio (ver más abajo).

Se hizo una revisión a fondo de arquitectura y se aplicaron los arreglos de red/concurrencia. **El Modelo queda CONGELADO** (no se le cambia más la lógica; priorizamos explicar en la defensa sobre seguir tocándolo).

Arreglos aplicados y verificados (Unity compila en batch; validación de escritorio **71 OK, 0 fallos**):

1. **Cola de salida en el Modelo**: `Simulacion.cs` ya no dispara un evento síncrono (`ParaTransmitir` se eliminó). Lo que produce (ataques con su daño, entrenamiento terminado, item sembrado, recolección que terminó sola) se **ENCOLA** en una `ConcurrentQueue`; el Controlador drena desde el hilo principal (`ProcesarMensajesRedPendientes`) y solo si hay conexión. Resultado: **una escritura TCP bloqueante nunca corre estando tomado el `lock(Candado)`**.
2. **`GestorArchivos` async (productor-consumidor)**: las llamadas a `RegistrarAccion`/`GuardarResultadoFinal` solo ENCOLAN; un único hilo escritor hace la I/O a disco. La escritura de archivos ya no frena la simulación ni ocurre dentro de un candado.
3. **Parser robusto**: antes del `switch` se valida la **aridad** por comando (diccionario `Aridad`); un mensaje truncado ya no explota con `IndexOutOfRange`, se registra y se descarta.
4. **`ConectorRed.Enviar` con `try/catch`**: un tubo muerto (socket cortado) ya no mata a los Tasks del Modelo: marca desconectado, registra el fallo y sigue.
5. **Espejo de red "obedece, no opina"**: `MoverUnidadRival`, `CrearEdificioRival`, `CrearUnidadRival` y `ColocarItemRival` ya NO validan ocupación de casilla (solo coordenadas y duplicidad): la copia rival replica lo que el rival hizo, y los desyncs por validaciones asimétricas desaparecieron.
6. **Controlador blindado**:
   - `CONSTRUIR` parsea el tipo con `Enum.TryParse` (un typo ya no regala un Centro Urbano gratis al rival).
   - `PrepararConector()` libera el `ConectorRed` anterior antes de abrir otro (reintentar tras un error ya no falla con el puerto 5505 ocupado).
   - `ProcesarMensajesRedPendientes` envuelve cada mensaje en `try/catch` (un mensaje malo no tumbar el `Update`) y anuncia **`FIN;ganador`** una sola vez para que ambas máquinas escriban el MISMO `resultado_final.txt`.
   - `EnviarPorRed` cuenta `MensajesDescartados` (para el HUD) y registra "NO ENVIADO" si el tubo está caído (ahí nace el desync; no pasa en silencio).
   - `Detener()` drena la cola de salida, desuscribe el evento de red y hace `GestorArchivos.Flush()` antes de salir.
7. **`DetenerRecoleccion` (M3)**: la entrada se quita del diccionario **al instante** (con guardia por token para que la Task vieja no pise un ciclo nuevo). Reiniciar recolección del mismo aldeano ya no falla en la ventana de ~1 s.

### REFACTOR DE ARQUITECTURA (16-sep): TODA la concurrencia vive en el MODELO

**Pedido de la profesora: los hilos/threads NO deben estar en el Controlador.** Refactor completo y VERDE (compile-check `PRUEBA OK` + red-test **71 OK, 0 FALLO**).

- **`Assets/Scripts/Modelo/Simulacion.cs`** — el motor del mundo. Concentra TODOS los mecanismos concurrentes:
  - `Candado` (el lock compartido del mundo; toda mutación pasa por `lock(Candado)`).
  - Reloj RTS en tiempo real (`IniciarRelojAsync`), entrenamiento (`EntrenamientoTaskAsync`), construcción (`ConstruccionTaskAsync`), recolección (`RecoleccionTaskAsync`), spawner de items (`IniciarSpawnerItemsAsync`), expiración del Casco (`ExpiracionCascoAsync`) y el bucle de batalla masiva (`IniciarBucleSimulacionAsync`).
  - Métodos **espejo para la red** (`MoverUnidadRival`, `AplicarAtaqueRivalAUnidad`, `AplicarAtaqueRivalAEdificio`, `CrearEdificioRival`, `CrearUnidadRival`, `CambiarEstadoRecoleccionRival`, `ColocarItemRival`, `AplicarRecogidaRival`, `EstablecerNombreRival`).
  - Config de tiempos como **propiedades del Motor** (`CicloRecoleccionMs`, `RelojTickMs`, `IntervaloSpawnerMs`, `DuracionCascoSegundos`, `TickSimulacionMs`, `TicksEntreAtaques`) y esperas como delegados (`EsperarEntrenamiento`, `EsperarConstruccion`) → las pruebas ajustan el Motor, NO el Controlador.
  - **Cola de salida** `ConcurrentQueue` + `Transmitir(...)`: cuando el motor produce algo que hay que anunciar por red (un ataque con su daño calculado, una unidad que terminó de entrenar, un item que apareció solo, un aldeano que terminó solo) lo ENCOLA; el Controlador lo reenvía por TCP desde el hilo principal.
- **`Controlador/JuegoControlador.cs` = puente delgado: CERO Task, CERO Thread, CERO lock.** Solo: expone el mundo (los objetos vienen del `Motor`), traduce acciones de la Vista → métodos del Motor, traduce mensajes de red → métodos espejo del Motor, y anuncia por red + escribe logs.
- **`ConectorRed.cs` y `GestorArchivos.cs` viven en `Assets/Scripts/Modelo/`** (`namespace Modelo`): TODOS los archivos con hilos/candados están en el Modelo. El hilo de escucha TCP es el único hilo "de infraestructura" y quedó en el Modelo.
- Para defensa oral: "Modelo = la simulación completa (estado + concurrencia); Controlador = orquesta que pide al Modelo y avisa al rival; Vista = solo pinta y llama al Controlador".

### CONCURRENCIA VISIBLE (16-sep): Instantánea, Detener y MODO BATALLA (Nivel 1)

- **`Modelo/InstantaneaJuego.cs` + `Simulacion.Instantanea()`** — foto segura del mundo: copia las LISTAS bajo `lock(Candado)` (unidades local/enemigo, edificios, recursos, items + oro/madera/comida/hierro/piedra/tiempo/ganador). La Vista la llama 1 vez por frame y dibuja desde la copia. Fachada: `JuegoControlador.Instantanea()`.
- **`Simulacion.Detener()` + `JuegoControlador.Detener()`** — apaga el motor al salir de la escena/Play (cancela reloj, spawner, entrenamientos, recolecciones, bucle de batalla; drena la salida y cierra la red; evita la "partida fantasma" y el puerto ocupado en el siguiente Play).
- **MODO BATALLA (concurrencia masiva, NIVEL 1)** — `Simulacion.IniciarBatalla(unidadesPorLado)`: siembra N unidades por lado con `ControladaPorIA=true` y enciende `BucleActivo`. Un **bucle de simulación** (cada `TickSimulacionMs`, 100 ms) recorre TODAS las unidades dentro de `lock(Candado)`, cada una: busca el rival más cercano, se acerca 1 casilla o golpea con enfriamiento `TicksEntreAtaques`. Los golpes del latido se aplican al final (`AplicarPendientes`) y luego se retiran los muertos y se llama `VerificarGanador()`.
  - **Por qué Nivel 1 (no paralelo):** un solo hilo dentro del candado → sin condiciones de carrera y fácil de razonar. El **Nivel 2** (lotes en `Parallel.For` por workers, sin candado y daños aplicados al final) queda como **optimización futura** para el informe.
  - Métricas para el HUD/informe: `BucleActivo`, `TicksSimulados`, `BajasLocal`, `BajasEnemigo`, `UnidadesEnBatalla`. Fachada del Controlador: `IniciarBatalla(n)`, `DetenerBatalla()`, `BucleBatallaActivo`, `TicksSimulados`, `BajasLocal`, `BajasEnemigo`.
  - **Limitación conocida:** el modo batalla NO se replica por red (es una demo local de concurrencia masiva). Presentarlo como tal en la defensa.

### Hecho y verificado (compila y pasa pruebas de escritorio)

- **Modelo POCO** completo en `Assets/Scripts/Modelo/`:
  - `Tipos.cs` — enums: `TipoUnidad`, `TipoEdificio`, `TipoRecurso`, `TipoItem`, `EstadoUnidad`, `EstadoEdificio`.
  - `DatosDelJuego.cs` — catálogo central de costos/estadísticas + fábricas (`CrearUnidad`, `CrearEdificio`, `CrearRecursosIniciales`, `CrearCentroUrbano`); constantes de items (Yogur/Casco/Espada/Herramientas).
  - `Unidad.cs`, `Edificio.cs`, `Recurso.cs`, `Item.cs`, `Jugador.cs`, `Mapa.cs` (30x30, validación de casillas), `Partida.cs` (tiempo real y ganador, **SIN turnos**).
- **Controlador** en `Assets/Scripts/Controlador/`:
  - `JuegoControlador.cs` — constructor **con parámetro `bool localArriba = true`**: el host vive arriba (centro (7,1), aldeano (6,1)) y el cliente (`localArriba:false`) vive abajo (centro (7,13), aldeano (6,13)). Así **las copias de ambos mundos concuerdan** y la red espeja por casilla.
  - Ataque valida **rango** y usa la defensa del modelo; construcción con costos del catálogo.
- **ENTRENAMIENTO Y RECOLECCIÓN CON CONCURRENCIA** (requisito de la guía): `Task` por trabajo + `CancellationToken` + `lock(Candado)`; la obra/entrenamiento termina solo; recolección suma por ciclo en segundo plano.
- **RTS EN TIEMPO REAL (SIN TURNOS)**: eliminados `NumeroTurno`/`JugadorActivo`/`AvanzarTurno`; el reloj (`IniciarRelojAsync`) suma 1 s por segundo real mientras la partida corre. El log registra la **hora real** y el jugador (no "turnos").
- **ITEMS DEL EQUIPO (objetos realistas, todo concurrente)**: el host corre un **spawner de fondo** (`IniciarSpawnerItemsAsync`, cada `IntervaloSpawnerMs`) que siembra un item y lo anuncia por red (`ITEM;Tipo;x;y`); al recoger con unidad adyacente (`RecogerItem`, mensaje `RECOGER_ITEM;Tipo;x;y`) aplica su efecto:
  - **Yogur** (consumible): cura +50 a todas las tropas.
  - **Casco** (temporal): +10 defensa X s; lo apaga `ExpiracionCascoAsync`. El espejo replica el +10 en la copia del rival (afecta los cálculos de daño compartidos).
  - **Espada** (equipable): +5 ataque (la suma `AtaqueTotal` en ambas copias).
  - **Herramientas** (pasivo): +5% de recolección permanente (`BonusRecoleccion`).
- **RED (sockets TCP) — módulo verificado**:
  - TCP: `HospedarRed(puerto)` = servidor (`TcpListener`), `ConectarRed(ip, puerto)` = cliente. Cada instancia corre SU simulación completa y **solo se envían las acciones**.
  - Un **hilo de escucha en segundo plano** lee del tubo (`ReadLine` bloqueante) y mete líneas en una `ConcurrentQueue`; el hilo del juego consume en `ProcesarMensajesRedPendientes()` (la Vista lo llama en `Update`).
  - Protocolo de texto separado por `;` (sin JSON): `MOVER;xOrigen;yOrigen;xNuevo;yNuevo`, `ATACAR;xa;ya;xb;yb;dano`, `ATACAR_EDIFICIO;xa;ya;xe;ye;ataque`, `CONSTRUIR;Tipo;x;y`, `ENTRENAR;Tipo;x;y`, `SALUDO;nombre`, `RECOLECTAR;x;y;1|0`, `ITEM;TipoItem;x;y`, `RECOGER_ITEM;TipoItem;x;y`, `FIN;ganador`.
  - **Espejo simétrico**: en mover/construir/entrenar el rival refleja la acción en su copia; en atacar se replica el **daño ya calculado** (una vez calculado, ambas copias restan lo mismo → convergencia); si muere la unidad/se destruye el edificio se elimina y se `VerificarGanador` en AMBOS lados.
  - **Reconexión automática**: los hilos son ciclos (`CicloServidor`/`CicloCliente`): el host vuelve a `AcceptTcpClient` y el cliente reintenta cada 1 s; el evento `AlConectar` reenvía el `SALUDO` en cada (re)conexión.
  - **FIN de partida sincronizado**: quien detecte la victoria envía `FIN;ganador` una sola vez; el rival cierra con el MISMO ganador y el mismo `resultado_final.txt`.
  - **Limitación conocida:** las CANTIDADES de recursos no se sincronizan por red, solo los estados (anotar en el informe).
- **Archivos:** `GestorArchivos.cs` (configuracion.txt, log_partida.txt, resultado_final.txt), escritos por un único hilo (async).

### Git (rama `modelo` = `main`)

- Commit actual: `997d2c9` "fix(controlador): blindar la frontera de red y sincronizar el ganador".
- `main` y `modelo` están en el MISMO commit (todo pusheado y sincronizado con `origin`).
- Tags: `modelo-0.1.0` (estado previo al congelamiento) y **`modelo-1.0.0`** (congelamiento del Modelo tras la revisión).
- `.gitignore` ignora `configuracion.txt`, `log_partida.txt`, `resultado_final.txt` (y ahora `AVANCE_PROYECTO.md` está VERSIONADO).
- Regla del repo: nombres de commits neutrales (tipos/spans convencionales, sin menciones de ninguna herramienta).

### Pruebas

Se validó fuera de Unity (los scripts no usan `UnityEngine`), en proyectos temporales de escritorio (fuera del repo):

- **Suite de lógica** (`compile-check`): `ControladorRapido` (tiempos a cero) — entrenar, recolección con hilo, construcción, mover, atacar, items, victoria, `Instantanea()` (copia independiente), `Detener()` sin errores, **Batalla Masiva Nivel 1** (20 vs 20: late, combate, declara ganador y apaga el bucle). Termina con `PRUEBA OK`.
- **Suite de red** (`red-test`): 2 instancias en localhost (host + cliente) — **71 OK, 0 fallos**:
  - Conexión TCP y `SALUDO` mutuo.
  - MOVER, RECOLECTAR (espejo y reinicio inmediato — M3), CONSTRUIR, ENTRENAR, ITEM/RECOGER_ITEM en espejo.
  - ATACAR_EDIFICIO: la Casa rival se destruye en ambas copias sin desync.
  - ATACAR: 4 ataques → el objetivo muere EN LA COPIA DEL CLIENTE; ambos lados declaran el mismo ganador.
  - **RECONEXIÓN AUTOMÁTICA**: se corta el tubo → ambos se reconectan solos y el saludo vuelve a fluir.
- **Demo interactiva** (`red-real`): 2 procesos reales (uno por PC), comandos por teclado; validado con 2 instancias localhost.

### Cómo probar la red en 2 máquinas reales

1. Ambas PCs en la **misma red local**.
2. En la **PC del host** (PowerShell como administrador) abrir el puerto:
   ```
   netsh advfirewall firewall add rule name="RTS-Academico" dir=in action=allow protocol=TCP localport=5510
   ```
3. En la PC host: ejecutar la demo con argumento `host`. Anotar la IP que aparece.
4. En la PC retador: ejecutar `cliente <ip-del-host>`. Si el host aún no abrió, el cliente reintenta cada 1 s.
5. Escribir `estado` en cada PC → ver el mundo local y el espejo del rival. Para salir: `salir`.
6. Para **cortar el firewall** después: `netsh advfirewall firewall delete rule name="RTS-Academico"`.

---

## Reglas del juego implementadas (resumen)

1. Inicio: jugador con 100 de cada recurso; Mapa 30x30 con ~17 yacimientos repartidos en 4 cuadrantes + zona central.
2. Construir: valida coordenada, casilla libre (ambos jugadores), sin yacimiento, y costo del catálogo.
3. Entrenar: edificio debe estar `Operativo` y poder entrenar ese tipo → paga → espera (Task) → unidad aparece junto al edificio.
4. Recolectar: aldeano adyacente al yacimiento → suma por ciclo en segundo plano.
5. Mover: destino dentro del mapa y casilla libre; la unidad **camina celda a celda** (BFS esquiva obstáculos) hasta llegar — nada de teletransporte.
6. Atacar: distancia ≤ `RangoAtaque`; daño = max(0, ataque - defensa).
7. Victoria (regicidio asimétrico): el enemigo cae al perder su capital (primer
   Centro Urbano), aunque le queden tropas o bases; el jugador cae si pierde su
   Centro o se queda sin unidades (la IA ahora también asedia).

---

## Limitaciones conocidas (para el informe / defensa)

Modelo congelado: estas se documentan y se explican, no se arreglan ("deuda técnica documentable").

| # | Severidad | Descripción | Manejo en defensa |
|---|-----------|-------------|-------------------|
| M1 | P1 | `EsperarConstruccion` sin `CancellationToken`; la espera de cancelación queda como tarea huérfana por cada obra. | Se explica: el `WhenAny` ya captura la cancelación; es deuda de testabilidad. |
| M2 | P1 | Ningún `CancellationTokenSource` recibe `Dispose()` (varios CTS de campo + por trabajo). | Se asume: en una partida corta no hay acumulación crítica. |
| M3 | P1 | ~~DetenerRecoleccion dejaba la entrada hasta que la Task terminaba~~ **ARREGLADO** (guardia por token). | Ya no aplica. |
| M4 | P1 | `TicksSimulados`/`Bajas*` se leen sin candado (escrituras atómicas de int). | Las lecturas son atómicas; se explica la tolerancia. |
| M5 | P1 | El modo batalla no se replica por red (demo local de concurrencia). | Demo en UNA máquina, presentado explícitamente como concurrencia local. |
| M6 | P1 | ~~resultado_final.txt divergía entre máquinas~~ **RESUELTO** con `FIN;ganador` (C7, en el Controlador). | Ya no aplica. |
| M7 | P1 | `Mapa.EsCasillaLibre` (sobrecarga con jugadores) arma listas con `Concat().ToList()` en cada llamada (perf con cientos de unidades). | Conteo de instancias por segundo en el informe; optimizable con caché. |
| M8 | P1 | Identidad por COORDENADAS sin resincronización: si un mensaje se pierde, la búsqueda del espejo falla para siempre. | Guion de demo corta y limpia (mover, construir, entrenar, atacar). |
| M9 | P2 | `ExpiracionCascoAsync` identifica al jugador por NOMBRE (falla si ambos se llaman igual). | Se evita en la demo (nombres distintos). |
| M10 | P2 | Dos definiciones de derrota: `JugadorDerrotado` usa `Vida > 0`; `Jugador.EstaDerrotado` usa `EstaOperativo`. | Regla de victoria oficial = la de `VerificarGanador`. |
| M11 | P2 | Solo un entrenamiento activo por TIPO (no por cuartel). | Regla de juego declarada; se puede justificar como diseño. |
| M12 | P2 | `ObtenerPosicionDeSalida` recorre O(radio²) y su fallback devuelve la casilla del propio edificio. | Raro en la práctica; se menciona como mejora futura. |
| M13 | P2 | `configuracion.txt` solo se escribe, nunca se lee. | Pendiente si el requisito pide leer configuración del archivo. |
| M14 | P3 | Ramas muertas (ternarios que siempre toman una rama). | Cosmético; flujo ya validado arriba. |
| M15 | P3 | `AplicarAtaqueRivalA*` confían en el rival (no revalidan atacante/rango). | "Asumimos cliente confiable; la seguridad no es requisito". |
| M16 | P3 | Fórmulas asimétricas: a unidades se envía daño calculado; a edificios, ataque bruto (+ fórmula por lado). | Defendible: los edificios no tienen defensa variable. |

**Decisión de diseño (mirror):** la copia rival "obedece, no opina" (prioriza la convergencia sobre la validez geométrica: dos entidades pueden quedar en la misma casilla en el espejo). Es una frase lista para la defensa.

---

## Conceptos clave aprendidos (para defensa oral / informe)

- **MVC**: Modelo POCO (sin UnityEngine) / Controlador orquesta / Vista solo lee.
- **Concurrencia**: `Task` (no bloquea UI), `lock` (evita condición de carrera), `CancellationToken` (cancelación limpia), productor-consumidor (colas) para I/O.
- **Regla de Unity**: la API de UnityEngine solo se toca desde el hilo principal; los hilos calculan y la Vista refresca desde `Update()`.
- **Red (sockets)**: TCP = llamada garantizada; dirección = **IP + PUERTO**; SERVIDOR escucha y CLIENTE llama; por el cable viajan BYTES → protocolo de texto con `;`.
- **Red + concurrencia**: leer de red es **bloqueante** (`ReadLine`) → va en hilo aparte que SOLO llena una cola; el hilo del juego consume las acciones → nadie se congela.
- **Reconexión (bucle + eventos)**: ciclos `CicloServidor`/`CicloCliente` + evento `AlConectar` para reenviar el `SALUDO` sin intervención humana.
- **RTS en tiempo real = reloj concurrente**: cada instancia tiene SU propio reloj y su copia de simulación; solo se intercambian las ACCIONES (patrón tipo lockstep).
- **Cola de salida (I/O fuera del candado)**: el Modelo ENCOLA lo que debe anunciar y el Controlador hace el socket desde el hilo principal → nunca se escribe a red dentro de `lock(Candado)`.

---

## PENDIENTE (siguientes pasos)

0. **Modelo congelado (FROZEN, tag `modelo-1.1.0`)** — Se descongeló para Hierro/Piedra y para el PVE (`IAEnemiga`).
1. [x] **Diagnóstico y Plan de Trabajo:** Revisión exhaustiva completada. El código compila correctamente (PRUEBA OK y 71 OK).
2. [x] **Revisión de la API:** `AplicarAtaqueRivalAUnidad` / `AplicarAtaqueRivalAEdificio`.
3. [x] **Nuevos Recursos (Hierro y Piedra):** `Tipos.cs`, `Jugador.cs`, `InstantaneaJuego.cs`, `DatosDelJuego.cs`, `Simulacion.cs`.
4. [x] **Vista**: escena `Juego.unity` + 5 scripts (GestorJuego, VistaTablero, HudRecursos, ControlInputUsuario, PanelFinPartida). Arte/sprites pendientes del compañero (issues #4–#9, label `vista`).
5. [x] **Documentación**: diagramas actualizados con PVE/IA + Vista (`DOCUMENTACION.md` §1, §2.1, §3.6, §4, §5).
6. [ ] **Pruebas EditMode de Unity** (3 casos pedidos): concurrencia, red y victoria del lado del editor.
7. [ ] **Evidencia de ejecución**: capturas de archivos txt.
8. [x] **README** actualizado (PVE, controles, estructura).
9. [ ] **Guion de la demo** (PVE + red).

## Instrucciones para la siguiente sesión

- **Al cambiar algo del código** (agregar o renombrar una clase, un método, un comando del protocolo, una regla, un item o un costo), **actualizar también los diagramas y la documentación** que lo reflejan: los diagramas Mermaid/PlantUML y el resto de `DOCUMENTACION.md`, más este `AVANCE_PROYECTO.md` y el `README.md`. El código y los diagramas van siempre en sync.
- Leer los scripts `Assets/Scripts/Modelo/*.cs` y `Assets/Scripts/Controlador/*.cs` antes de tocar.
- **JEFATURA ROSA (requisito de la profesora): NUNCA crear hilos/tasks/locks en el Controlador.** Toda la concurrencia se agrega en `Modelo/Simulacion.cs` (motor).
- Mantener el Modelo **sin UnityEngine** (POCO) para poder probarlo fuera de Unity.
- Antes de hacer push: `git fetch` + `git status`, y resolver conflictos a mano integrando lo mejor de cada versión.
- Para validar la lógica: correr la suite de escritorio (proyectos temporales locales, fuera del repo) → `PRUEBA OK`.
- Para validar la red: correr la suite de escritorio de red → debe dar **71 OK, 0 fallos**.
- Para la demo en 2 PCs reales: consultar la sección "Cómo probar la red en 2 máquinas reales" (arriba). Recordar `netsh` en el host.