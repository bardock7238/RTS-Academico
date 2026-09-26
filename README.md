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
  `VistaTablero`, `HudRecursos`, `ControlInputUsuario`, `PanelFinPartida`,
  `MenuInicio`, `MenuRed`, `Minimap` y `UiFabrica` (fábrica de botones/textos).
  Dibuja la fotografía del mundo (`InstantaneaJuego`) y maneja la entrada
  (cámara con zoom/paneo, selección por arrastre, fantasma de construcción).
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

Al abrir la escena sale el **menú inicial**: configura tu escenario y pulsa
**¡JUGAR!**. Opciones: **bases enemigas (1-5)** repartidas por los bordes del
mapa (tú eres los **Griegos**; ellos: Romanos, Persas, Egipcios, Cartagineses
y Babilonios), **enemigo/yo avanzado** (Cuartel + soldados desde el minuto 0).
También hay `COMO JUGAR` con los controles y `SALIR` para cerrar el juego.

### Modo PVE (1 jugador vs IA)

1. Abrir el proyecto en Unity 6000.6.0f1.
2. Abrir la escena `Assets/Escenas/Juego.unity` y pulsar Play.
3. En el menú inicial, pulsar **VS MAQUINA (PVE)**: ahí arranca el
   Controlador y la IA enemiga (economía: recolección; militar: Cuartel +
   Soldados). Sin elegir modo la partida no empieza.

### Modo red (2 jugadores)

1. Abrir el proyecto en Unity 6000.6.0f1.
2. Abrir la escena de juego desde `Assets/Escenas` y pulsar Play.
3. Abrir el **menú provisional de red** con `M` (o el botón **Red [M]** del panel lateral):
   - **Host · puerto 5505** (o tecla `H`) en un equipo;
   - **Cliente** con la IP del host (o tecla `J` para `127.0.0.1`) en el otro.
4. Los archivos `configuracion.txt`, `log_partida.txt` y `resultado_final.txt` se
   escriben en `Application.persistentDataPath` (`%USERPROFILE%\AppData\LocalLow\...`).

## Controles

### Ratón

| Acción | Clic | Qué hace |
|--------|------|----------|
| Seleccionar | Izq. en unidad | Selecciona esa unidad (o todas las apiladas en la casilla) |
| Selección múltiple | **Arrastrar** con izq. | Selecciona todas tus unidades vivas dentro del rectángulo |
| Añadir al grupo | Der. en unidad | Añade/quita la unidad del grupo seleccionado |
| Orden contextual | Izq. con unidad seleccionada | **Item** → va a recogerlo · **Yacimiento** (con aldeano) → recolecta · **Enemigo/ciervo** → ataca (si está lejos, camina hasta el rango y sigue pegando) · **Vacío** → caminar hasta ahí |
| Deseleccionar | `Esc` | Limpia la selección (y cancela viajes en curso) |

Las unidades se **apilan** en la misma casilla (solo los edificios bloquean el paso).

### Selección por tipo

| Tecla | Selecciona |
|-------|------------|
| `Alt+Q` | Todas las unidades vivas: **Aldeanos** |
| `Alt+W` | Todas las unidades vivas: **Soldados** |
| `Alt+E` | Todas las unidades vivas: **Arqueros** |
| `Alt+R` | Todas las unidades vivas: **Caballeros** |

### Entrenar unidades

| Tecla | Entrena | Desde |
|-------|---------|-------|
| `Q` | Aldeano | Centro Urbano |
| `W` | Soldado | Cuartel |
| `E` | Arquero | Cuartel |
| `R` | Caballero | Cuartel |

### Construir edificios

Después de la tecla, **clic en el mapa** para colocar el edificio.

| Tecla | Construye |
|-------|-----------|
| `1` | Casa |
| `2` | Cuartel |
| `3` | Torre |
| `4` | Centro Urbano |

### Recolección e ítems

| Tecla | Qué hace |
|-------|----------|
| `C` | **Modo recoger**: después, clic en un yacimiento o item → la unidad camina sola (autoselecciona un aldeano si hace falta). Segunda `C` = va a lo más cercano |
| `I` | Ir directo al **item más cercano** |
| `Esc` | Cancelar viaje en curso y/o limpiar selección |

También hay botones en el panel lateral derecho con los mismos atajos (`Recoger [C]`, `Item [I]`, etc.).

### Escenario y facciones

Desde el menú inicial eliges **bases enemigas (1–5)** repartidas por los
bordes, y si arrancan **avanzadas** (con Cuartel y soldados) o básicas —
igual para ti. Tú eres los **Griegos**; ellos: Romanos, Persas, Egipcios,
Cartagineses y Babilonios, cada uno con su color. La IA enemiga **no ataca
hasta el minuto 3** (se prepara); tú puedes atacar desde el segundo 0.

### Cámara

| Tecla | Qué hace |
|-------|----------|
| `Flechas` | Mover la cámara por el mapa (también sobre los paneles con la rueda) |
| `Rueda del ratón` | Zoom 6–55 (acercar/alejar, funciona incluso sobre la UI) |
| `Botón central` (arrastrar) | Mover la cámara |

Abajo a la izquierda hay un **minimapa**: puntos azules (tus tropas),
rojos (enemigo), amarillos (recursos) y el marco blanco de tu cámara.
**Clic en el minimapa** para saltar ahí.

El mapa (100×100) tiene **biomas**: pradera, arena y bosque (solo visual,
no cambian las reglas). En el bosque hay **arboledas grandes**, **rocas** y
**ciervos**: ordena a tus tropas atacarlos para cazarlos (**+100 de comida**
cada uno). En la arena hay **menas grandes de oro y hierro**.
Los yacimientos dentro de un bioma rinden **+50% por ciclo**.

### Economía avanzada

- Las **Casas** dan +1 de comida y los **Centros Urbanos** +1 de oro cada
  4 segundos (ambos bandos).
- **Mercado [T]**: vende 100 de un recurso por 60 de oro, o compra 100 por
  75 de oro.
- **Herrería [Y]**: mejora ataque (+1/tropa), defensa (+1) y recolección
  (+10%) hasta nivel 3, con costos crecientes.
- Los **aldeanos se reponen solos**: al morir uno, el Centro entrena otro
  automáticamente (si hay hueco y fondos, hasta 4).
- **Ritmo de partida** (menú): Rápida (60 s de paz, IA al 85%),
  Normal (180 s, 60%) o Larga (7 min de paz, IA al 45%).
- **Inicio rico** (menú): +250 madera/comida, +150 oro, +50 hierro/piedra,
  2 aldeanos extra y una Casa operativa desde el minuto 0.
- **Regicidio**: ganas al destruir la capital enemiga (su primer Centro,
  marcada con un anillo dorado), aunque le queden tropas; pierdes si cae
  tu Centro o tu ejército. La IA también **asedia**: sin tropas tuyas cerca,
  marcha sobre tu Centro tras la gracia.
- **Sonido procedural** (sin assets): clic, error, golpes, construir,
  entrenar, fanfarrias y música ambiental en bucle. Tecla **S** o botón
  `SONIDO` del HUD para silenciar.
- **Grupos de control**: `Ctrl+5..9` guarda la selección, `5..9` la recupera
  (las bajas se podan solas).
- **Demoler**: clic en un edificio enemigo y la tropa camina sola hasta su
  huella y demuele sin soltarlo (solo otra orden lo interrumpe); vale clicar
  cualquier casilla del edificio. El objetivo fijado no se pierde solo.
- Cada aldea enemiga arranca con su aldeano (la avanzada suma Cuartel + tropas).
- La barra muestra la **facción** de lo seleccionado y de lo atacado.

### Panel izquierdo TROPAS

Botones de selección rápida (lo mismo que `Alt+Q/W/E/R`): Aldeanos,
Soldados, Arqueros, Caballeros y Todos (todas tus unidades vivas).

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
