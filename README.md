# RTS Académico

Juego de estrategia en tiempo real (RTS) 1v1 por red, desarrollado en Unity con
arquitectura **MVC** y **concurrencia real** (tareas/hilos con `lock`,
`CancellationToken` y colas concurrentes). Es un proyecto académico.

## Requisitos

- **Unity 6000.6.0f1** (Unity 6.6). Ver `ProjectSettings/ProjectVersion.txt`.
- Para jugar en red, un build de Windows en cada equipo (o dos instancias del Editor).

## Arquitectura (MVC)

- **Modelo** (`Assets/Scripts/Modelo`): estado y reglas del juego, y **toda la
  concurrencia** (reloj de simulación, entrenamiento, construcción, recolección,
  spawner de items y batalla con IA; candado `lock(Candado)` sobre el mundo). No
  depende de `UnityEngine`, por lo que se puede probar fuera del Editor.
- **Controlador** (`Assets/Scripts/Controlador/JuegoControlador.cs`): capa
  delgada. Traduce las acciones del usuario y los mensajes de red a métodos del
  Modelo, y escribe los archivos de log. **No crea hilos.**
- **Vista** (`Assets/Scripts/Vista`, a implementar): dibuja la fotografía del
  mundo (`InstantaneaJuego`) y maneja la entrada del usuario. **No crea hilos**;
  solo lee la instantánea una vez por frame.

Regla de oro: el Controlador y la Vista no crean hilos; la concurrencia vive en
el Modelo.

## Estructura de carpetas

- `Assets/Scripts/Modelo` — lógica y concurrencia.
- `Assets/Scripts/Controlador` — puente entre Vista, Modelo y red.
- `Assets/Scripts/Vista` — vista Unity (escena, sprites e interfaz).
- `Assets/Arte` — `Animaciones`, `Audio`, `Interfaz`, `Materiales`, `Modelos`,
  `Prefabs` y `Texturas`.
- `Assets/Datos` — datos de apoyo.
- `Assets/Escenas` — escenas del juego.
- `Assets/Pruebas` — pruebas (por ejemplo, `Juego/PruebaEnUnity.cs`).

## Cómo ejecutar (2 jugadores)

1. Abrir el proyecto en Unity 6000.6.0f1.
2. Abrir la escena de juego desde `Assets/Escenas`. (Pendiente de crear.)
3. En un equipo elegir **Host** (por defecto puerto `5505`); en el otro elegir
   **Cliente** e ingresar la IP del host.
4. Los archivos `configuracion.txt`, `log_partida.txt` y `resultado_final.txt` se
   escriben en `Application.persistentDataPath` (`%USERPROFILE%\AppData\LocalLow\...`).

## Documentación

- [`AVANCE_PROYECTO.md`](AVANCE_PROYECTO.md) — estado, decisiones y limitaciones.
- [`DOCUMENTACION.md`](DOCUMENTACION.md) — UML, casos de uso, diagramas de
  secuencia y mapa de concurrencia.

## Protocolo de red

TCP con mensajes separados por `;`. Comandos: `SALUDO`, `MOVER`, `ATACAR`,
`ATACAR_EDIFICIO`, `CONSTRUIR`, `ENTRENAR`, `RECOLECTAR`, `ITEM`, `RECOGER_ITEM`
y `FIN`. Detalle exacto en `Assets/Scripts/Controlador/JuegoControlador.cs`.

## Estado

- Modelo congelado: tag `modelo-1.0.0`.
- Ramas `main` y `modelo` sincronizadas.
