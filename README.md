# RTS Académico

Juego de estrategia en tiempo real (RTS) 1v1 por red o **PVE** (jugador vs IA),
desarrollado en Unity con arquitectura **MVC** y **concurrencia real**
(tareas/hilos con `lock`, `CancellationToken` y colas concurrentes). Proyecto
académico.

## Requisitos

- **Unity 6000.6.0f1** (Unity 6.6). Ver `ProjectSettings/ProjectVersion.txt`.
- Para jugar en red, un build de Windows en cada equipo (o dos instancias del Editor).

## Arquitectura (MVC)

- **Modelo** (`Assets/Scripts/Modelo`): estado y reglas del juego, y **toda la
  concurrencia** (reloj de simulación, entrenamiento, construcción, recolección,
  spawner de items, batalla con IA e **IA enemiga PVE**; candado `lock(Candado)`
  sobre el mundo). No depende de `UnityEngine`, por lo que se puede probar fuera
  del Editor.
- **Controlador** (`Assets/Scripts/Controlador/JuegoControlador.cs`): capa
  delgada. Traduce las acciones del usuario y los mensajes de red a métodos del
  Modelo, y escribe los archivos de log. **No crea hilos.** Expone
  `IniciarIA()` / `IAActiva` para el modo PVE.
- **Vista** (`Assets/Scripts/Vista`): `GestorJuego` (raíz + UI por código),
  `VistaTablero`, `HudRecursos`, `ControlInputUsuario`, `PanelFinPartida`.
  Dibuja la fotografía del mundo (`InstantaneaJuego`) y maneja la entrada.
  **No crea hilos**; solo lee la instantánea una vez por frame.

Regla de oro: el Controlador y la Vista no crean hilos; la concurrencia vive en
el Modelo.

## Estructura de carpetas

- `Assets/Scripts/Modelo` — lógica, concurrencia e IA enemiga.
- `Assets/Scripts/Controlador` — puente entre Vista, Modelo y red.
- `Assets/Scripts/Vista` — vista Unity (escena, sprites e interfaz).
- `Assets/Arte` — `Animaciones`, `Audio`, `Interfaz`, `Materiales`, `Modelos`,
  `Prefabs` y `Texturas`.
- `Assets/Datos` — datos de apoyo.
- `Assets/Escenas` — escenas del juego (`Juego.unity`).
- `Assets/Pruebas` — pruebas (por ejemplo, `Juego/PruebaEnUnity.cs`).

## Cómo ejecutar

### Modo PVE (1 jugador vs IA)

1. Abrir el proyecto en Unity 6000.6.0f1.
2. Abrir la escena `Assets/Escenas/Juego.unity` y pulsar Play.
3. `GestorJuego` arranca el Controlador e inicia la IA enemiga
   (economía: recolección; militar: Cuartel + Soldados).

### Modo red (2 jugadores)

1. Abrir el proyecto en Unity 6000.6.0f1.
2. Abrir la escena de juego desde `Assets/Escenas`.
3. En un equipo elegir **Host** (por defecto puerto `5505`); en el otro elegir
   **Cliente** e ingresar la IP del host.
4. Los archivos `configuracion.txt`, `log_partida.txt` y `resultado_final.txt` se
   escriben en `Application.persistentDataPath` (`%USERPROFILE%\AppData\LocalLow\...`).

## Controles (Vista mínima)

- Clic izq: seleccionar unidad · clic con unidad: contextual (item → va a
  recogerlo, yacimiento con aldeano → recolecta, enemigo → atacar, vacío → caminar).
- `QWER`: entrenar (Aldeano, Soldado, Arquero, Caballero).
- `1-4`: construir (Casa, Cuartel, Torre, Centro Urbano).
- `C`: modo item — clic en el item y la unidad **caminará sola** a recogerlo
  (segunda `C` = item más cercano). Sin selección, autoselecciona la unidad viva más cercana.
- `I`: ir directo al item más cercano · `Esc`: cancelar viaje / selección.

## Documentación

- [`AVANCE_PROYECTO.md`](AVANCE_PROYECTO.md) — estado, decisiones y limitaciones.
- [`DOCUMENTACION.md`](DOCUMENTACION.md) — UML, casos de uso, diagramas de
  secuencia (incluye PVE/IA) y mapa de concurrencia.
- [`GUIA_VISTA.md`](GUIA_VISTA.md) — guía de implementación de la Vista.

## Protocolo de red

TCP con mensajes separados por `;`. Comandos: `SALUDO`, `MOVER`, `ATACAR`,
`ATACAR_EDIFICIO`, `CONSTRUIR`, `ENTRENAR`, `RECOLECTAR`, `ITEM`, `RECOGER_ITEM`
y `FIN`. Detalle exacto en `Assets/Scripts/Controlador/JuegoControlador.cs`.

## Estado

- Modelo congelado: tag `modelo-1.1.0`.
- Vista mínima jugable + modo PVE implementados (issues #4–#9 de arte pendientes).
- Ramas `main` y `modelo` sincronizadas.
