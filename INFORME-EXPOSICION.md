# INFORME DE EXPOSICIÓN — "Imperios en Guerra" (RTS en Unity)

**Proyecto:** RTS-Academico — Imperios en Guerra
**Curso:** Programación Avanzada
**Docente:** Nancy Yaneth Gélvez García
**Monitor:** Brayan Javier Ramírez Mendoza (20222020139)
**Motor:** Unity 6000.6.0f1 · **Lenguaje:** C# · **Arquitectura:** Modelo-Vista-Controlador

> Este documento es la guía de exposición. Todo lo que está aquí está respaldado
> por el código: si se afirma algo, se puede señalar el archivo y la línea.

---

## 1. Qué es el proyecto

Un **RTS (juego de estrategia en tiempo real)** inspirado en Age of Empires, para
escritorio. Dos jugadores compiten por el control de un mapa de 140×140 casillas:
administran recursos, construyen edificios, entrenan unidades y se enfrentan. Gana
quien destruya el Centro Urbano del rival (regicidio) o le aniquile el ejército.

**Modos de juego:**
- **PVE** — jugador contra la IA (que juega con las *mismas reglas* y el *mismo candado*).
- **PVP** — dos máquinas conectadas por TCP, cada una con su copia del mundo espejada.
- **Exploración** — variante con niebla de guerra: hay que encontrar y destruir la capital sin saber dónde está.

---

## 2. Las tres capas (MVC) y quién hace qué

| Capa | Carpeta | Depende de Unity? | ¿Tiene hilos? | Responsabilidad |
|------|---------|-------------------|---------------|-----------------|
| **Modelo** | `Assets/Scripts/Modelo/` | **No** | **Sí — todos** | Reglas, mundo, concurrencia, red, IA, archivos |
| **Vista** | `Assets/Scripts/Vista/` | Sí | No | Dibujar, leer input, mostrar mensajes |
| **Controlador** | `Assets/Scripts/Controlador/` | No | No | Traducir Vista→Modelo, red→Modelo, anunciar |

**Frase clave para la defensa:**
> "El Modelo es el único que sabe las reglas y el único que crea hilos. La Vista
> solo dibuja. El Controlador solo traduce. Ninguna capa hace el trabajo de otra."

### 2.1 El Modelo (14 clases, POCO puro)

No importa `UnityEngine` en ningún archivo. Eso permite compilarlo y probarlo en
un `.exe` de consola sin abrir Unity — de ahí las suites de pruebas de escritorio.

| Clase | Qué hace |
|-------|----------|
| **`Simulacion`** | **El motor del mundo.** Contiene el `lock(Candado)`, los 10 tipos de Task, las acciones del jugador, la IA, el pathfinding BFS y los métodos "espejo" de la red. Es la clase más grande y la más importante. |
| `Jugador` | 5 recursos, listas de unidades y edificios, mejoras de herrería, condición de derrota. |
| `Unidad` | Vida, ataque, defensa, rango, posición, estado, item equipado, objetivo. |
| `Edificio` | Vida, huella en el mapa (`Lado`×`Lado`), qué unidades entrena, estado. |
| `Mapa` | Rejilla 140×140. Valida coordenadas, casillas libres, transitibilidad y *pathfinding BFS*. |
| `Recurso` | Yacimiento: tipo, cantidad, posición, si se agotó. |
| `Item` | Objeto del mapa (Yogur, Casco, Espada, Herramientas). |
| `Partida` | Estado global: en ejecución, tiempo, ganador, motivo de victoria. |
| **`ConectorRed`** | **Sockets TCP.** Hilo de escucha, cola de recibidos, reconexión, latido. |
| **`GestorArchivos`** | **`configuracion.txt`, `log_partida.txt`, `resultado_final.txt`** con productor-consumidor. |
| **`IAEnemiga`** | La máquina: economy, construcción, militar, reunión. Corre en su propia Task. |
| `DatosDelJuego` | Catálogo central de costos y estadísticas (la "fuente de verdad"). |
| `InstantaneaJuego` | La **foto** que la Vista pinta: copia inmutable del mundo. |
| `Tipos` | Enums: `TipoUnidad`, `TipoEdificio`, `TipoRecurso`, `TipoItem`, estados. |

### 2.2 La Vista (12 MonoBehaviours)

| Clase | Qué hace |
|-------|----------|
| **`GestorJuego`** | **La raíz.** Dueño único del Controlador. En `Update()` hace las 2 llamadas por frame. |
| `VistaTablero` | Pinta mapa y entidades desde la foto. Pool de sprites (cero `Instantiate` por frame). |
| `ControlInputUsuario` | Clicks, box-select, grupos Ctrl+5-9, cámara RTS, modos construir/recoger. |
| `HudRecursos` | Barra superior: 5 recursos, tiempo, mensajes. |
| `PanelFinPartida` | Modal de victoria/derrota con estadísticas. |
| `MenuInicio` / `MenuRed` | Elegir modo y escenario; hospedar/conectar. |
| `MenuMercado` / `MenuMejoras` | Trueque de recursos; herrería. |
| `Minimap` | Minimapa con niebla y marco de cámara. |
| `UiFabrica`, `ArteRecursos`, `SpriteFactory`, `SonidoJuego` | Fabricas de UI, carga de sprites, pixel-art por código, sonido procedural. |

### 2.3 El Controlador (1 clase)

`JuegoControlador` — el puente. **Cero `Task`, cero `Thread`, cero `lock`.**
Su trabajo se reduce a 4 verbos: *exponer*, *traducir*, *anunciar*, *escribir logs*.

---

## 3. Los 7 puntos de intercambio (lo que más se pregunta)

Estos son los **únicos** lugares por los que la información cruza de una capa a otra.
Están marcados como comentarios en el propio código.

### (1) VISTA → CONTROLADOR — el arranque
**Dónde:** `GestorJuego.Awake()`
```
Controlador = new JuegoControlador(nombreJugador, localArriba);
```
El Controlador, en su constructor, instancia el Modelo:
```
Motor = new Simulacion(...);
```
Y el Modelo **arma el mundo entero y arranca sus 4 Tasks fijas** (reloj, bucle de
simulación, fauna, economía; más el spawner de items si esta máquina es el host).
A partir de ese momento **el mundo avanza solo**, sin que nadie lo pida.

### (2) VISTA → CONTROLADOR — las 2 llamadas de cada frame
**Dónde:** `GestorJuego.Update()`, en este orden exacto:
```
Controlador.ProcesarMensajesRedPendientes();   // red -> modelo
UltimaFoto = Controlador.Instantanea();         // modelo -> vista
```
> **¿Por qué la red antes que la foto?** Porque si se invirtiera, un ataque que
> llega del rival se vería **un frame tarde**: la foto se sacaría antes de aplicar
> el movimiento del espejo.

### (3) VISTA → MODELO — las acciones del usuario
**Dónde:** `ControlInputUsuario`, `MenuMercado`, `MenuMejoras`

Toda acción del juego tiene la **misma forma de 3 pasos**:
```csharp
public bool MoverUnidad(Unidad unidad, int nuevoX, int nuevoY)
{
    int origenX = unidad?.PosicionX ?? 0;      // 1. guardar el origen
    if (!Motor.MoverUnidad(unidad, nuevoX, nuevoY)) return false;  // 2. el Modelo valida y muta
    EnviarPorRed($"MOVER;{origenX};{origenY};{nuevoX};{nuevoY}"); // 3. encolar para el rival
    return true;
}
```
- Paso 2: el Modelo toma `lock(Candado)`, valida (recursos, rango, coordenadas) y muta. Si devuelve `false`, la acción es inválida.
- Paso 3: `EnviarPorRed` **no escribe al socket**, solo encola (ver punto 6).

**Por qué la Vista no valida nada:** si la Vista opinara ("tienes madera"), la regla
existiría en dos sitios y se desincronizarían. Como el Modelo es el que **cobra**,
es el único que sabe los costos.

### (4) CONTROLADOR → MODELO — la red reflejada
**Dónde:** `ProcesarMensajesRedPendientes()` → `ProcesarMensajeRed(mensaje)`

Cada mensaje de la cola se traduce a un **método "espejo"** del Modelo:
```csharp
case "ATACAR":
    Unidad objetivo = Motor.AplicarAtaqueRivalAUnidad(ax, ay, bx, by, dano);
```
Es decir: el mensaje nunca muta el mundo directamente, siempre pasa por un método
del Modelo que vuelve a tomar `lock(Candado)`.

### (5) MODELO → VISTA — la foto (el único camino de regreso)
**Dónde:** `Simulacion.Instantanea()`

```csharp
lock (Candado)
{
    return new InstantaneaJuego(
        new List<Unidad>(JugadorLocal.Unidades),   // copia
        new List<Unidad>(JugadorEnemigo.Unidades),
        ...);
}
```

> **Esta es la pieza que hace posible que 10 Tasks modifiquen el mundo mientras la
> Vista dibuja, sin que pase nada.** Si la Vista recorriera las listas vivas,
> Unity lanzaría `Collection was modified while enumerating` y la partida se caería.
> Con la copia, si una unidad muere a media partida, en la Vista simplemente
> aparece en el frame siguiente. **Cero excepciones.**

Se copian las **listas**, no los objetos: es rápido (son referencias).

### (6) MODELO → RED — encolar, nunca enviar
**Dónde:** `Simulacion.Transmitir()`

```csharp
private void Transmitir(string mensaje) => _salientes.Enqueue(mensaje);
```

Esto ocurre **adentro de `lock(Candado)` y desde cualquier Task**. El Modelo
**nunca toca el socket**. El que envía es el Controlador, en el hilo principal:
```
Modelo:   Transmitir("MOVER;...")  ->  cola _salientes   (no bloquea)
Controlador: ProcesarMensajes...  ->  Enviar(...)         ->  hilo de red
```

> **¿Por qué?** Si el rival tiene la red lenta y el Modelo escribiera al socket
> dentro del candado, **congelaría el mundo entero**. Con la cola, el Modelo
> nunca se bloquea, y si el rival está caído el mensaje sale al reconectar.

### (7) VISTA → CONTROLADOR — el apagado
**Dónde:** `GestorJuego.OnDestroy()` → `Detener()`
1. `Motor.Detener()`: cancela los 6 `CancellationTokenSource` y los trabajos en curso.
2. Drena la cola saliente (el último `FIN` alcanza a salir).
3. Cierra el socket.
4. `GestorArchivos.Flush()`: garantiza que los `.txt` quedaron en disco.

---

## 4. Concurrencia: qué corre y dónde

### 4.1 Los 10 tipos de Task (todos en el Modelo)

| # | Task | Qué hace | Cadencia |
|---|------|----------|----------|
| 1 | `IniciarRelojAsync` | `TiempoJuegoSegundos++` | 1 s |
| 2 | `IniciarBucleSimulacionAsync` | Avanza destinos + resuelve combate | 100 ms |
| 3 | `IniciarSpawnerItemsAsync` | Siembra items (**solo host**) | 15 s |
| 4 | `VagarFaunaAsync` | Los ciervos dan pasos al azar | ~1.8–2.7 s |
| 5 | `EconomiaPasivaAsync` | Casas→comida, Centros→oro | 4 s |
| 6 | `EntrenamientoTaskAsync` | 1 por unidad entrenándose | según catálogo |
| 7 | `ConstruccionTaskAsync` | 1 por obra en curso | según catálogo |
| 8 | `RecoleccionTaskAsync` | 1 por aldeano trabajando | 1 s por ciclo |
| 9 | `ExpiracionCascoAsync` | Quita el +defensa del Casco | 10 s |
| 10 | `IAEnemiga.BucleDecisionAsync` | Decisiones de la máquina (PVE) | 2 s |

### 4.2 Los 2 hilos dedicados (`Thread`)

| Hilo | Dónde | Qué hace |
|------|-------|----------|
| `HiloRedServidor` / `HiloRedCliente` | `ConectorRed` | Lectura **bloqueante** del socket. Solo mete líneas en la cola. **No toca el juego.** |
| `HiloEscritorLogs` | `GestorArchivos` | Único que escribe a disco. |

> **¿Por qué un hilo para leer el socket?** Porque `ReadLine()` se bloquea. Si lo
> hiciera el hilo principal de Unity, la ventana se congelaría. Al vivir en un
> hilo aparte, puede quedarse esperando datos indefinidamente sin afectar la partida.

### 4.3 Los 3 candados

| Candado | Dónde | Qué protege |
|---------|-------|-------------|
| `Simulacion.Candado` | Modelo | **El mundo entero**: unidades, edificios, recursos, items |
| `ConectorRed._lockEnvio` | Modelo | El socket (que dos envíos no se pisen) |
| (interno del `BlockingCollection`) | `GestorArchivos` | La cola de escritura |

### 4.4 Las 3 colas thread-safe

`_salientes` (Modelo→red), `_recibidos` (red→Modelo), cola de logs.

### 4.5 Las 3 reglas que no se rompen

> **R1.** Toda mutación pasa por `lock(Candado)`. Ninguna Task ve el mundo a medio escribir.

> **R2.** **Nunca** se escribe a un socket ni a disco dentro de `lock(Candado)`.
> Se encola, y otro lo escribe.

> **R3.** La Vista nunca lee listas vivas. Pide `Instantanea()`.

### 4.6 `CancellationTokenSource`: cómo se apanda todo

Cada Task de fondo tiene su CTS. `Detener()` cancela los 6 fijos y luego fotografía
los trabajos en curso **bajo candado** y los cancela **fuera** (porque `Cancel()`
dispara callbacks síncronos y nunca debe correr con el candado tomado).

---

## 5. Comunicación en red

### 5.1 Flujo completo de un mensaje

```
EMISOR                                      RECEPTOR
──────                                      ───────
Modelo: Transmitir("MOVER;1;2;3;4")
  └─> cola _salientes        (no bloquea)
        └─> Controlador: ProcesarMensajesRedPendientes()  (hilo principal)
              └─> ConectorRed.Enviar()     (lock _lockEnvio)
                    └─> [TCP] ─────────>  HiloRedEscucha: ReadLine() bloqueante
                                            └─> cola _recibidos   (no toca el juego)
                                                  └─> Controlador: RecibirMensaje()
                                                        └─> ProcesarMensajeRed()
                                                              └─> Modelo: MoverUnidadRival()
                                                                    (lock Candado)
```

### 5.2 El protocolo (separador `;`)

```
SALUDO;<nombre>              MOVER;<ox>;<oy>;<x>;<y>
ATACAR;<ax>;<ay>;<bx>;<by>;<dano>
ATACAR_EDIFICIO;<ax>;<ay>;<ex>;<ey>;<ataque>
CONSTRUIR;<Tipo>;<x>;<y>     ENTRENAR;<Tipo>;<x>;<y>
RECOLECTAR;<x>;<y>;<1|0>     ITEM;<TipoItem>;<x>;<y>
RECOGER_ITEM;<TipoItem>;<x>;<y>          FIN;<ganador>
PING / PONG                  (latido silencioso)
```

**El diccionario `Aridad`** valida cuántos campos trae cada comando. Un mensaje
truncado (por una reconexión a medias) se registra y se descarta, en vez de
reventar el `Update()` del juego.

### 5.3 Convergencia: por qué no se envía el estado completo

> Cuando el atacante golpea, el Modelo **calcula el daño una vez** y lo envía ya
> resuelto. El rival aplica **ese mismo número**, sin recalcular.

```csharp
// en el atacante (Simulacion.Atacar)
int danoReal = Math.Max(0, atacante.AtaqueTotal + BonoAtaque - (enemigo.Defensa + bonus));
enemigo.RecibirGolpe(danoReal);
Transmitir($"ATACAR;...;{danoReal}");

// en el rival (AplicarAtaqueRivalAUnidad)
objetivo.RecibirGolpe(dano);   // daño plano, ya calculado
```
Ambas copias convergen al mismo resultado sin enviar el estado del mundo entero.

### 5.4 Manejo de errores de comunicación

| Problema | Solución |
|----------|----------|
| Rival no está (aún) | El cliente reintenta cada 1 s en su propio hilo |
| Rival se cae | El hilo vuelve al bucle y reconecta solo; `AlConectar` reenvía el `SALUDO` |
| **Tubo "muerto"** (WiFi a medias) | `PING` cada 3 s sin enviar; si pasan **15 s sin recibir nada**, se corta y el hilo reconecta |
| Mensaje malformado | Se registra y se descarta (try/catch + `Aridad`) |
| Mensaje que no pudo salir | `MensajesDescartados++` y se escribe en el log (el desync no pasa en silencio) |
| Caída real del rival | Tras 5 s sin tubo, la Vista cierra y vuelve al menú |

---

## 6. Verificación de ganador

**Dónde:** `Simulacion.VerificarGanador()` (con `/// <summary>` documentado).

| Jugador | Causa de derrota |
|---------|------------------|
| **Enemigo** | Su capital (primer Centro) destruida, o su Centro Urbano ya no existe |
| **Jugador local** | Pierde su Centro Urbano, o se queda sin unidades (aniquilado) |

Se llama **automáticamente** tras cada ataque con muerte, cada edificio demolido y
en cada latido de batalla. El candado evita que dos latidos declaren ganador a la vez.

`FinalizarPartida()` escribe en `resultado_final.txt` y anuncia el `FIN` por red
**una sola vez** (para que ambas máquinas escriban el mismo resultado).

---

## 7. Manejo de archivos

| Archivo | Contenido |
|---------|-----------|
| `configuracion.txt` | Configuración inicial del mapa |
| `log_partida.txt` | Cada acción, con el formato de la guía |
| `resultado_final.txt` | Ganador y motivo |

**Formato del log (exacto de la guía):**
```
Turno: Jugador 1
Acción: Ataque
Resultado: Impacto - Unidad enemiga destruida
Hora: 14:32:15
```

**Productor-consumidor:** cualquier hilo llama `RegistrarAccion()`, que **solo
encola** (no bloqueante). Un único `HiloEscritorLogs` es el que toca el disco.
Así, escribir un log nunca frena la simulación. `Flush()` usa un centinela para
esperar a que todo quede escrito antes de cerrar el proceso.

---

## 8. Pruebas de escritorio

`Assets/Pruebas/Editor/PruebasBatch.cs` — corre **sin abrir el Editor**:

```bash
Unity.exe -batchmode -nographics -projectPath <repo> -executeMethod PruebasBatch.Todo -logFile - -quit
```

| Prueba | Qué verifica |
|--------|--------------|
| `ProbarApiObligatoria` | Sin Controlador no hay juego; con él hay mundo |
| `ProbarPveSinRed` | La IA + reloj/ticks avanzan sin red |
| `ProbarPvpHostCliente` | Host + cliente se conectan e intercambian `SALUDO` |
| `ProbarArteDescargado` | Los sprites cargan como `Sprite` |
| `ProbarAtaqueYVictoria` | Ataque entre unidades + aniquilación + destruction de Centro |
| `ProbarConcurrencia` | Recolección y movimiento simultáneos sin condiciones de carrera |

---

## 9. Guion de preguntas y respuestas

**P: ¿Por qué el Controlador no valida las reglas?**
R: Para que exista una sola fuente de verdad. Si el Controlador validara, la regla
estaría en dos sitios y se desincronizarían. Además, el Controlador es quien
anuncia por red: si negara una acción que en realidad es válida, el rival
vería una partida imposible.

**P: ¿Por qué copiar las listas y no mandar el estado por red?**
R: Copiarlas es barato (son referencias, no se clonan objetos) y elimina de raíz
la clase entera de errores de concurrencia en la Vista.

**P: ¿Qué pasa si dos Tasks tocan la misma unidad a la vez?**
R: No puede pasar: ambas toman `lock(Candado)`, que las serializa. El que entra
primero muta; el otro espera y ve el estado ya actualizado.

**P: ¿Y si el rival tiene la red lentísima?**
R: El Modelo no se bloquea nunca: `Transmitir()` solo encola. La escritura al
socket ocurre en el hilo principal, con un tope de 500 mensajes por frame, así
que una ráfaga se drena en varios frames en vez de congelar uno.

**P: ¿Por qué no usaste `Parallel.For` para la batalla?**
R: Porque el nivel actual ya cumple y `Parallel.For` sobre el mismo candado no
aporta paralelismo real (se serializarían igual). Está documentado como posible
mejora futura, no como requisito perdido.

**P: ¿Qué pasa con los datos del juego guardados?**
R: El archivo de configuración, el log y el resultado se escriben en
`Application.persistentDataPath`, y `Detener()` hace `Flush()` para que nada
quede en memoria al cerrar.

---

## 10. Conclusión

El proyecto cumple los tres ejes de la asignatura:

- **MVC** con separación estricta: el Modelo no depende de Unity, el Controlador
  no tiene hilos, la Vista no valida reglas.
- **Concurrencia real**: 10 tipos de Task, 2 hilos dedicados, 3 candados y
  3 colas thread-safe, gobernados por 3 reglas que se pueden enunciar y defender.
- **Red real**: sockets TCP con protocolo definido, espejo del mundo, manejo de
  reconexión, detección de tubo muerto y manejo de mensajes corruptos.

La idea que resume todo el diseño:

> **El Modelo es el único dueño de la verdad y del tiempo. La Vista solo mira
> fotos. El Controlador solo traduce. Y nadie escribe a un socket ni a un disco
> con el mundo bloqueado.**
