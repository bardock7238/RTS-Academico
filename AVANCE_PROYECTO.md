# AVANCE DEL PROYECTO — "Imperios en Guerra" (RTS en Unity)

---

## Datos clave

- **Proyecto:** RTS académico "Imperios en Guerra" (inspirado en Age of Empires).
- **Motor:** Unity 6.0.6f1. **Lenguaje:** C#. **Rama git:** `modelo` (y `main`, en el mismo commit).
- **Repositorio:** `https://github.com/bardock7238/RTS-Academico.git`.
- **Fecha límite (avance):** 22 de septiembre.
- **Patrón exigido por la guía:** MVC, concurrencia con hilos, red, archivos de log.

## Reparto del equipo

- **Johan:** capa **Modelo** + **Controlador**.
- **Compañero:** capa **Vista** en Unity (sprites, escenas, prefabs) — *aún no la ha empezado*.

---

## Estado actual (última sesión: 19-sep)

### ⭐ REVISIÓN EXTERNA + ARREGLOS DE ROBUSTEZ (19-sep) — Modelo FROZEN

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

### ⭐ REFACTOR DE ARQUITECTURA (16-sep): TODA la concurrencia vive en el MODELO

**Pedido de la profesora: los hilos/threads NO deben estar en el Controlador.** Refactor completo y VERDE (compile-check `PRUEBA OK` + red-test **71 OK, 0 FALLO**).

- **`Assets/Scripts/Modelo/Simulacion.cs`** — el motor del mundo. Concentra TODOS los mecanismos concurrentes:
  - `Candado` (el lock compartido del mundo; toda mutación pasa por `lock(Candado)`).
  - Reloj RTS en tiempo real (`IniciarRelojAsync`), entrenamiento (`EntrenamientoTaskAsync`), construcción (`ConstruccionTaskAsync`), recolección (`RecoleccionTaskAsync`), spawner de items (`IniciarSpawnerItemsAsync`), expiración del Casco (`ExpiracionCascoAsync`) y el bucle de batalla masiva (`IniciarBucleSimulacionAsync`).
  - Métodos **espejo para la red** (`MoverUnidadRival`, `AplicarAtaqueEnUnidadLocal`, `AplicarAtaqueEnEdificioLocal`, `CrearEdificioRival`, `CrearUnidadRival`, `CambiarEstadoRecoleccionRival`, `ColocarItemRival`, `AplicarRecogidaRival`, `EstablecerNombreRival`).
  - Config de tiempos como **propiedades del Motor** (`CicloRecoleccionMs`, `RelojTickMs`, `IntervaloSpawnerMs`, `DuracionCascoSegundos`, `TickSimulacionMs`, `TicksEntreAtaques`) y esperas como delegados (`EsperarEntrenamiento`, `EsperarConstruccion`) → las pruebas ajustan el Motor, NO el Controlador.
  - **Cola de salida** `ConcurrentQueue` + `Transmitir(...)`: cuando el motor produce algo que hay que anunciar por red (un ataque con su daño calculado, una unidad que terminó de entrenar, un item que apareció solo, un aldeano que terminó solo) lo ENCOLA; el Controlador lo reenvía por TCP desde el hilo principal.
- **`Controlador/JuegoControlador.cs` = puente delgado: CERO Task, CERO Thread, CERO lock.** Solo: expone el mundo (los objetos vienen del `Motor`), traduce acciones de la Vista → métodos del Motor, traduce mensajes de red → métodos espejo del Motor, y anuncia por red + escribe logs.
- **`ConectorRed.cs` y `GestorArchivos.cs` viven en `Assets/Scripts/Modelo/`** (`namespace Modelo`): TODOS los archivos con hilos/candados están en el Modelo. El hilo de escucha TCP es el único hilo "de infraestructura" y quedó en el Modelo.
- Para defensa oral: "Modelo = la simulación completa (estado + concurrencia); Controlador = orquesta que pide al Modelo y avisa al rival; Vista = solo pinta y llama al Controlador".

### ⭐ CONCURRENCIA VISIBLE (16-sep): Instantánea, Detener y MODO BATALLA (Nivel 1)

- **`Modelo/InstantaneaJuego.cs` + `Simulacion.Instantanea()`** — foto segura del mundo: copia las LISTAS bajo `lock(Candado)` (unidades local/enemigo, edificios, recursos, items + oro/madera/comida/tiempo/ganador). La Vista la llama 1 vez por frame y dibuja desde la copia. Fachada: `JuegoControlador.Instantanea()`.
- **`Simulacion.Detener()` + `JuegoControlador.Detener()`** — apaga el motor al salir de la escena/Play (cancela reloj, spawner, entrenamientos, recolecciones, bucle de batalla; drena la salida y cierra la red; evita la "partida fantasma" y el puerto ocupado en el siguiente Play).
- **MODO BATALLA (concurrencia masiva, NIVEL 1)** — `Simulacion.IniciarBatalla(unidadesPorLado)`: siembra N unidades por lado con `ControladaPorIA=true` y enciende `BucleActivo`. Un **bucle de simulación** (cada `TickSimulacionMs`, 100 ms) recorre TODAS las unidades dentro de `lock(Candado)`, cada una: busca el rival más cercano, se acerca 1 casilla o golpea con enfriamiento `TicksEntreAtaques`. Los golpes del latido se aplican al final (`AplicarPendientes`) y luego se retiran los muertos y se llama `VerificarGanador()`.
  - **Por qué Nivel 1 (no paralelo):** un solo hilo dentro del candado → sin condiciones de carrera y fácil de razonar. El **Nivel 2** (lotes en `Parallel.For` por workers, sin candado y daños aplicados al final) queda como **optimización futura** para el informe.
  - Métricas para el HUD/informe: `BucleActivo`, `TicksSimulados`, `BajasLocal`, `BajasEnemigo`, `UnidadesEnBatalla`. Fachada del Controlador: `IniciarBatalla(n)`, `DetenerBatalla()`, `BucleBatallaActivo`, `TicksSimulados`, `BajasLocal`, `BajasEnemigo`.
  - **Limitación conocida:** el modo batalla NO se replica por red (es una demo local de concurrencia masiva). Presentarlo como tal en la defensa.

### Hecho y verificado (compila y pasa pruebas de escritorio)

- **Modelo POCO** completo en `Assets/Scripts/Modelo/`:
  - `Tipos.cs` — enums: `TipoUnidad`, `TipoEdificio`, `TipoRecurso`, `TipoItem`, `EstadoUnidad`, `EstadoEdificio`.
  - `DatosDelJuego.cs` — catálogo central de costos/estadísticas + fábricas (`CrearUnidad`, `CrearEdificio`, `CrearRecursosIniciales`, `CrearCentroUrbano`); constantes de items (Yogur/Casco/Espada/Herramientas).
  - `Unidad.cs`, `Edificio.cs`, `Recurso.cs`, `Item.cs`, `Jugador.cs`, `Mapa.cs` (15x15, validación de casillas), `Partida.cs` (tiempo real y ganador, **SIN turnos**).
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

### Pruebas (concepto a repetir)

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

1. Inicio: jugador con 100 de cada recurso; Mapa 15x15 con 6 yacimientos.
2. Construir: valida coordenada, casilla libre (ambos jugadores), sin yacimiento, y costo del catálogo.
3. Entrenar: edificio debe estar `Operativo` y poder entrenar ese tipo → paga → espera (Task) → unidad aparece junto al edificio.
4. Recolectar: aldeano adyacente al yacimiento → suma por ciclo en segundo plano.
5. Mover: destino dentro del mapa y casilla libre.
6. Atacar: distancia ≤ `RangoAtaque`; daño = max(0, ataque - defensa).
7. Victoria: jugador derrotado si no tiene Centro Urbano con vida **o** no tiene unidades.

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
| M15 | P3 | `AplicarAtaque*Local` confían en el rival (no revalidan atacante/rango). | "Asumimos cliente confiable; la seguridad no es requisito". |
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

0. **Modelo congelado (FROZEN, tag `modelo-1.0.0`)** — no se toca más su lógica salvo necesidad crítica de última hora.
1. **Vista (compañero)**: escena Unity con mapa/sprrites por enum, barras de vida/progreso, HUD de recursos y red (`HospedarRed`/`ConectarRed`), HUD de batalla, panel de fin de partida. El Modelo ya expone todo (enums, `Instantanea()`, `Detener()`, `ProcesarMensajesRedPendientes()`, `MensajesDescartados`). Recomendado: `ArranqueJuego` único dueño del `JuegoControlador`; `Instantanea()` llamada UNA vez por frame; `Detener()` en `OnDestroy`/`OnApplicationQuit`.
2. **Documentación**: `AVANCE_PROYECTO.md` (este archivo), diagrama de clases UML, casos de uso, diagramas de secuencia (mover+espejo, ataque sincronizado, entrenamiento por red, spawn/pickup de item, fin de partida), mapa de concurrencia (7 Tasks + 1 hilo + 3 candados). Todo en Mermaid/PlantUML dentro de un `.md`.
3. **Pruebas EditMode de Unity** (3 casos pedidos): concurrencia, red y victoria del lado del editor.
4. **Evidencia de ejecución**: capturas de `configuracion.txt`, `log_partida.txt`, `resultado_final.txt` + log de una partida con dos jugadores.
5. **README** actualizado con instrucciones de ejecución en dos máquinas.
6. **Guion de la demo**: orden exacto de clics (evitar detener+reiniciar recolección sobre el MISMO aldeano en el mismo instante, etc.).
7. **Opcional:** Nivel 2 de batalla (`Parallel.For`) como optimización para el informe.

## Instrucciones para la siguiente sesión

- Leer los scripts `Assets/Scripts/Modelo/*.cs` y `Assets/Scripts/Controlador/*.cs` antes de tocar.
- **JEFATURA ROSA (requisito de la profesora): NUNCA crear hilos/tasks/locks en el Controlador.** Toda la concurrencia se agrega en `Modelo/Simulacion.cs` (motor).
- Mantener el Modelo **sin UnityEngine** (POCO) para poder probarlo fuera de Unity.
- Antes de hacer push: `git fetch` + `git status`, y resolver conflictos a mano integrando lo mejor de cada versión.
- Para validar la lógica: correr la suite de escritorio (proyectos temporales locales, fuera del repo) → `PRUEBA OK`.
- Para validar la red: correr la suite de escritorio de red → debe dar **71 OK, 0 fallos**.
- Para la demo en 2 PCs reales: consultar la sección "Cómo probar la red en 2 máquinas reales" (arriba). Recordar `netsh` en el host.