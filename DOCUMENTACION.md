# DOCUMENTACIÓN TÉCNICA — "Imperios en Guerra" (RTS en Unity)

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

Diagramas de diseño del sistema. Reflejan el estado del Modelo congelado en el tag `modelo-1.0.0` (ramas `modelo` / `main`).

Los bloques `mermaid` se ven en GitHub, en VS Code (extensión Mermaid) o en https://mermaid.live. Los bloques `plantuml` se renderizan en https://www.plantuml.com/plantuml o con la extensión PlantUML.

Idea central de la arquitectura (MVC):

- **Modelo** = toda la simulación y **toda la concurrencia** (hilos, locks, colas). Sin `UnityEngine`, por eso se prueba fuera de Unity.
- **Controlador** = puente delgado: traduce acciones de la Vista → Modelo, traduce la red → Modelo, y anuncia/carga logs. **No crea hilos.**
- **Vista** = solo pinta desde una instantánea y llama al Controlador.

---

## 1. Diagrama de clases

```mermaid
classDiagram
    direction LR

    class TipoUnidad {
        <<enumeration>>
        Aldeano
        Soldado
        Arquero
        Caballero
    }
    class TipoEdificio {
        <<enumeration>>
        CentroUrbano
        Cuartel
        Torre
        Casa
    }
    class TipoRecurso {
        <<enumeration>>
        Madera
        Oro
        Comida
    }
    class TipoItem {
        <<enumeration>>
        Yogur
        Casco
        Espada
        Herramientas
    }
    class EstadoUnidad {
        <<enumeration>>
        Idle
        Moviendo
        Recolectando
        Construyendo
        Atacando
    }
    class EstadoEdificio {
        <<enumeration>>
        EnConstruccion
        Operativo
        Destruido
    }

    class Unidad {
        +TipoUnidad Tipo
        +int Vida
        +int VidaMaxima
        +int Ataque
        +int Defensa
        +int RangoAtaque
        +int PosicionX
        +int PosicionY
        +bool EsRecolector
        +int CapacidadRecoleccion
        +EstadoUnidad Estado
        +Item Equipado
        +bool ControladaPorIA
        +Unidad Objetivo
        +int TiempoEsperaAtaque
        +bool EstaViva
        +bool PuedeAtacar
        +int AtaqueTotal
        +RecibirDano(int)
        +RecibirGolpe(int)
        +Curarse(int)
        +MoverA(int, int)
    }

    class Edificio {
        +TipoEdificio Tipo
        +int Vida
        +int VidaMaxima
        +int PosicionX
        +int PosicionY
        +EstadoEdificio Estado
        +List~TipoUnidad~ UnidadesEntrenables
        +bool EstaViva
        +bool EstaOperativo
        +PuedeEntrenar(TipoUnidad)
        +RecibirDano(int)
        +CompletarConstruccion()
        +Destruir()
    }

    class Recurso {
        +TipoRecurso Tipo
        +int Cantidad
        +int CantidadMaxima
        +int PosicionX
        +int PosicionY
        +bool EstaAgotado
        +float ProgresoDisponible
        +Extraer(int) int
    }

    class Item {
        +TipoItem Tipo
        +int PosicionX
        +int PosicionY
        +bool Recogido
    }

    class Jugador {
        +string Nombre
        +int Oro
        +int Madera
        +int Comida
        +List~Unidad~ Unidades
        +List~Edificio~ Edificios
        +int DefensaBonus
        +double BonusRecoleccion
        +int ItemsRecogidos
        +bool EstaDerrotado
        +bool TieneCentroUrbanoOperativo
        +PuedePagar(int, int, int) bool
        +Gastar(int, int, int) bool
        +Recibir(int, int, int)
        +AgregarUnidad(Unidad)
        +AgregarEdificio(Edificio)
    }

    class Mapa {
        +const int Ancho
        +const int Alto
        +List~Recurso~ RecursosEnMapa
        +EsCoordenadaValida(int, int) bool
        +ObtenerRecursoEn(int, int) Recurso
        +CasillaTieneRecurso(int, int) bool
        +EsCasillaLibre(int, int, List~Unidad~, List~Edificio~) bool
        +EsCasillaLibre(int, int, Jugador, Jugador) bool
        +EsCasillaEdificable(int, int, Jugador, Jugador) bool
        +SeSolapan(Unidad, Edificio) bool
    }

    class Partida {
        +bool EnEjecucion
        +int TiempoJuegoSegundos
        +string GanadorNombre
        +Finalizar(string)
    }

    class DatosDelJuego {
        <<static>>
        +Dictionary~TipoUnidad,UnidadConfig~ UnidadesBase
        +Dictionary~TipoEdificio,EdificioConfig~ EdificiosBase
        +CrearUnidad(TipoUnidad, int, int) Unidad
        +CrearEdificio(TipoEdificio, int, int, bool) Edificio
        +ObtenerTipoEdificio(string) TipoEdificio
        +UnidadesEntrenablesDe(TipoEdificio) List~TipoUnidad~
        +CrearCentroUrbano(int, int) Edificio
        +CrearRecursosIniciales() List~Recurso~
        +NombreDe(TipoItem) string
    }

    class InstantaneaJuego {
        +List~Unidad~ UnidadesLocal
        +List~Unidad~ UnidadesEnemigo
        +List~Edificio~ EdificiosLocal
        +List~Edificio~ EdificiosEnemigo
        +List~Recurso~ Recursos
        +List~Item~ Items
        +int Oro
        +int Madera
        +int Comida
        +int TiempoJuegoSegundos
        +bool EnEjecucion
        +string GanadorNombre
    }

    class Simulacion {
        +object Candado
        +Jugador JugadorLocal
        +Jugador JugadorEnemigo
        +Mapa Tablero
        +Partida EstadoPartida
        +IReadOnlyList~Item~ ItemsVisibles
        +bool HaySalientes
        +SiguienteSaliente() string
        +int CicloRecoleccionMs
        +int RelojTickMs
        +int IntervaloSpawnerMs
        +int DuracionCascoSegundos
        +int TickSimulacionMs
        +int TicksEntreAtaques
        +int TicksSimulados
        +int BajasLocal
        +int BajasEnemigo
        +bool BucleActivo
        +int UnidadesEnBatalla
        +Func EsperarEntrenamiento
        +Func EsperarConstruccion
        +Instantanea() InstantaneaJuego
        +MoverUnidad(Unidad, int, int) bool
        +ConstruirEdificio(TipoEdificio, int, int) bool
        +EntrenarUnidad(TipoUnidad, TipoEdificio) bool
        +IniciarRecoleccion(Unidad, Recurso) bool
        +DetenerRecoleccion(Unidad) bool
        +Atacar(Unidad, Unidad) bool
        +AtacarEdificio(Unidad, Edificio) bool
        +ColocarItem(TipoItem, int, int) bool
        +RecogerItem(Unidad, Item) bool
        +VerificarGanador()
        +IniciarBatalla(int) int
        +DetenerBatalla()
        +Detener()
        +MoverUnidadRival(int, int, int, int) Unidad
        +AplicarAtaqueEnUnidadLocal(int, int, int, int, int) Unidad
        +AplicarAtaqueEnEdificioLocal(int, int, int, int, int) Edificio
        +CrearEdificioRival(TipoEdificio, int, int) bool
        +CrearUnidadRival(TipoUnidad, int, int) bool
        +CambiarEstadoRecoleccionRival(int, int, bool) bool
        +ColocarItemRival(TipoItem, int, int) bool
        +AplicarRecogidaRival(TipoItem, int, int) bool
    }

    class GestorArchivos {
        <<static>>
        +Flush()
        +GuardarConfiguracionInicial(string)
        +RegistrarAccion(string, string, string)
        +GuardarResultadoFinal(string)
    }

    class ConectorRed {
        +bool EstaConectado
        +string UltimoError
        +event AlConectar
        +bool HayMensajes
        +IniciarHost(int) bool
        +Conectar(string, int) bool
        +Enviar(string) bool
        +RecibirMensaje() string
        +CortarConexion()
        +Cerrar()
        +Dispose()
        +ObtenerIpLocal()$ string
    }

    class JuegoControlador {
        +Simulacion Motor
        +Jugador JugadorLocal
        +Jugador JugadorEnemigo
        +Mapa Tablero
        +Partida EstadoPartida
        +ConectorRed RedPartida
        +string NombreRivalRed
        +int MensajesDescartados
        +MoverUnidad(Unidad, int, int) bool
        +ConstruirEdificio(TipoEdificio, int, int) bool
        +EntrenarUnidad(TipoUnidad, TipoEdificio) bool
        +IniciarRecoleccion(Unidad, Recurso) bool
        +DetenerRecoleccion(Unidad) bool
        +Atacar(Unidad, Unidad) bool
        +AtacarEdificio(Unidad, Edificio) bool
        +ColocarItem(TipoItem, int, int, bool) bool
        +RecogerItem(Unidad, Item) bool
        +IniciarBatalla(int) int
        +DetenerBatalla()
        +BucleBatallaActivo
        +Instantanea() InstantaneaJuego
        +HospedarRed(int) bool
        +ConectarRed(string, int) bool
        +EnviarSaludoRed()
        +ProcesarMensajesRedPendientes() int
        +Detener()
    }

    %% Composiciones / agregaciones del Modelo
    Jugador "1" o-- "*" Unidad : unidades
    Jugador "1" o-- "*" Edificio : edificios
    Mapa "1" o-- "*" Recurso : RecursosEnMapa
    Simulacion "1" *-- "*" Item : _itemsGlobales
    Simulacion "1" *-- "1" Mapa : Tablero
    Simulacion "1" *-- "1" Partida : EstadoPartida
    Simulacion "1" *-- "2" Jugador : local / enemigo
    Unidad --> "0..1" Item : Equipado
    Unidad --> "0..1" Unidad : Objetivo

    %% Dependencias
    Simulacion ..> DatosDelJuego : fábricas
    Simulacion ..> GestorArchivos : logs
    Simulacion ..> InstantaneaJuego : crea
    Simulacion ..> ConectorRed : (cola de salida)

    %% Controlador
    JuegoControlador "1" o-- "1" Simulacion : Motor
    JuegoControlador "1" o-- "1" ConectorRed : RedPartida
    JuegoControlador ..> InstantaneaJuego : expone

    %% Enums usados
    Unidad ..> TipoUnidad
    Unidad ..> EstadoUnidad
    Unidad ..> TipoItem
    Edificio ..> TipoEdificio
    Edificio ..> EstadoEdificio
    Recurso ..> TipoRecurso
    Item ..> TipoItem
```

**Nota sobre `JuegoControlador.Motor`:** es `public` a propósito. Las pruebas de escritorio (fuera de Unity) necesitan ajustar los tiempos del motor (`CicloRecoleccionMs`, `EsperarEntrenamiento`, etc.) y consultar contadores de batalla. Envolverlo fue una mejora evaluada y descartada por romper esas suites; queda documentado como decisión.

---

## 2. Diagrama de casos de uso

Nota: los casos de uso no son un tipo nativo de Mermaid. Van dos versiones: la de Mermaid (renderiza en GitHub/VS Code) y la de PlantUML (notación UML formal; se pega en https://www.plantuml.com/plantuml).

### 2.1 Versión Mermaid (renderiza en GitHub)

```mermaid
flowchart LR
    J(["Jugador"])
    R(["Jugador Rival - segunda instancia"])

    subgraph SIS["Imperios en Guerra (RTS)"]
        direction TB
        UC02(["Construir edificio"])
        UC03(["Entrenar unidad"])
        UC05(["Mover unidad"])
        UC06(["Atacar unidad enemiga"])
        UC07(["Atacar edificio enemigo"])
        UC08(["Recolectar recursos"])
        UC09(["Recoger item"])
        UC04(["Sincronizar acciones por red TCP"])
        UC11(["Ver mapa e instantanea del juego"])
        UC12(["Ver estado de partida / ganador"])
        UC19(["Declarar ganador - FIN compartido"])
        UC17(["Reconectar si el rival cae"])
        UC18(["Modo Batalla - concurrencia masiva"])
    end

    J --> UC02
    J --> UC03
    J --> UC05
    J --> UC06
    J --> UC07
    J --> UC08
    J --> UC09
    J --> UC11
    J --> UC12
    J --> UC18
    J --> UC04
    R --> UC04

    UC04 -.->|extend| UC17
    UC03 -.->|include - verifica derrota| UC12
    UC06 -.->|include| UC19
    UC07 -.->|include| UC19
```

### 2.2 Versión formal PlantUML (texto para pegar en plantuml.com)

```text
@startuml
left to right direction
skinparam packageStyle rectangle

actor "Jugador" as J
actor "Jugador Rival\n(segunda instancia)" as R

rectangle "Imperios en Guerra (RTS)" {
  usecase "Construir edificio" as UC02
  usecase "Entrenar unidad" as UC03
  usecase "Mover unidad" as UC05
  usecase "Atacar unidad enemiga" as UC06
  usecase "Atacar edificio enemigo" as UC07
  usecase "Recolectar recursos" as UC08
  usecase "Recoger item" as UC09
  usecase "Sincronizar acciones\npor red (TCP)" as UC04
  usecase "Ver mapa e\ninstantánea del juego" as UC11
  usecase "Ver estado de\npartida / ganador" as UC12
  usecase "Declarar ganador\n(FIN compartido)" as UC19
  usecase "Expulsar/Reconectar\nsi el rival cae" as UC17
  usecase "Modo Batalla\n(concurrencia masiva)" as UC18
}

J --> UC02
J --> UC03
J --> UC05
J --> UC06
J --> UC07
J --> UC08
J --> UC09
J --> UC11
J --> UC12
J --> UC18
J --> UC04
R --> UC04
UC04 ..> UC17 : <<extend>>
UC03 ..> UC12 : <<include>> (verifica derrota)
UC06 ..> UC19 : <<include>>
UC07 ..> UC19 : <<include>>
@enduml
```

---

## 3. Diagramas de secuencia

Formato del protocolo: para que rendericen en cualquier visor, en los diagramas el comando se representa como `NOMBRE (campos)`. En el protocolo real (ver `ConectorRed.cs`) los campos van separados por `;`: `MOVER;<ox>;<oy>;<x>;<y>`, `ATACAR;<ax>;<ay>;<bx>;<by>;<dano>`, `ATACAR_EDIFICIO;<ax>;<ay>;<ex>;<ey>;<ataque>`, `CONSTRUIR;<Tipo>;<x>;<y>`, `ENTRENAR;<Tipo>;<x>;<y>`, `RECOLECTAR;<x>;<y>;<1|0>`, `ITEM;<TipoItem>;<x>;<y>`, `RECOGER_ITEM;<TipoItem>;<x>;<y>`, `FIN;<ganador>`.

### 3.1 Mover una unidad y espejarlo en el rival

```mermaid
sequenceDiagram
    participant V as Vista
    participant C as JuegoControlador
    participant S as Simulacion
    participant CR as ConectorRed

    V->>C: MoverUnidad(u, x, y)
    C->>S: MoverUnidad(u, x, y)
    S->>S: lock(Candado) — valida mapa/casilla y mueve
    S-->>C: true
    alt hay conexión
        C->>CR: Enviar comando MOVER (ox, oy, x, y)
        CR-->>C: ok
    else sin conexión
        C->>C: MensajesDescartados++ + log "NO ENVIADO"
    end
    Note over C: Registra la acción en GestorArchivos

    CR->>CR: Rival recibe la línea (hilo de escucha → cola)
    V->>C: ProcesarMensajesRedPendientes() (Update)
    C->>CR: RecibirMensaje() → comando MOVER (ox, oy, x, y)
    C->>S: MoverUnidadRival(ox, oy, x, y)
    S->>S: lock(Candado) — espeja (sin validar ocupación)
```

### 3.2 Atacar una unidad: daño calculado una vez, restado en ambas copias

```mermaid
sequenceDiagram
    participant C as JuegoControlador atacante
    participant S as Simulacion
    participant CR as ConectorRed
    participant S2 as Simulacion rival

    C->>S: Atacar(atacante, enemigo)
    S->>S: lock(Candado) — valida rango
    S->>S: dano = max(0, AtaqueTotal - Defensa)
    S->>S: target.RecibirGolpe(dano) — resta lo MISMO del cálculo
    S-->>C: true + Transmitir comando ATACAR (ax, ay, bx, by, dano)
    C->>CR: Enviar(...) (drena cola, fuera del lock)
    S->>S: Si murió → elimina + VerificarGanador()

    CR->>S2: comando ATACAR (ax, ay, bx, by, dano)
    S2->>S2: lock(Candado) — AplicarAtaqueEnUnidadLocal(...)
    S2->>S2: RecibirGolpe(dano) — resta el MISMO daño
    S2->>S2: Si murió → elimina + VerificarGanador()
    Note over S,S2: Las dos copias convergen (mismo daño, mismo resultado)
```

### 3.3 Entrenar una unidad en el rival (evento del Modelo)

```mermaid
sequenceDiagram
    participant C as JuegoControlador local
    participant S as Simulacion
    participant E as Edificio
    participant CR as ConectorRed
    participant S2 as Simulacion rival

    C->>S: EntrenarUnidad(TipoUnidad, edificio)
    S->>E: PuedeEntrenar(tipo)? y paga costo
    S->>S: Lanza EntrenamientoTaskAsync (Task + CancellationToken)
    Note over S: La unidad aparece DESPUÉS (async)
    S->>S: ... EsperarEntrenamiento(...) ...
    S->>S: lock(Candado) — crea la unidad junto al edificio
    S->>S: Transmitir comando ENTRENAR (Tipo, x, y) → cola de salida
    C->>CR: Enviar comando ENTRENAR (Tipo, x, y) (hilo principal)
    CR->>S2: comando ENTRENAR (Tipo, x, y)
    S2->>S2: CrearUnidadRival(tipo, x, y) bajo lock
```

### 3.4 Sembrar/recoger un item (Casco: efecto temporal con expiración)

```mermaid
sequenceDiagram
    participant Sp as SpawnerTask del Modelo
    participant S as Simulacion
    participant C as JuegoControlador
    participant CR as ConectorRed
    participant S2 as Simulacion rival
    participant Ex as ExpiracionCascoTask

    Sp->>S: ColocarItem(TipoItem, x, y) cada IntervaloSpawnerMs
    S->>S: lock(Candado) — crea Item en el mapa
    S->>S: Transmitir comando ITEM (Tipo, x, y)
    C->>CR: Enviar comando ITEM (Tipo, x, y)
    CR->>S2: comando ITEM (Tipo, x, y) → ColocarItemRival(...) bajo lock

    C->>S: RecogerItem(unidad, item)
    S->>S: lock(Candado) — aplica efecto (cura/defensa/ataque/bonus)
    S->>S: Transmitir comando RECOGER_ITEM (Tipo, x, y)
    C->>CR: Enviar comando RECOGER_ITEM (Tipo, x, y)
    CR->>S2: comando RECOGER_ITEM (Tipo, x, y) → AplicarRecogidaRival(...)
    Note over S2: El rival replica el +10 del Casco para que<br/>los cálculos de daño sigan coincidiendo

    Ex->>S: Al pasar DuracionCascoSegundos
    S->>S: lock(Candado) — quita DefensaBonus
    Note over Ex: Si el nombre del rival también tiene el Casco,<br/>lo expira en su copia (identificación por nombre)
```

### 3.5 Fin de partida sincronizado (implementado)

```mermaid
sequenceDiagram
    participant S as Simulacion detecta
    participant C as JuegoControlador
    participant CR as ConectorRed
    participant S2 as Simulacion rival
    participant C2 as JuegoControlador rival

    S->>S: VerificarGanador() → Finalizar(ganador)
    S-->>C: (polling) EstadoPartida.GanadorNombre != null
    C->>C: guard _ganadorAnunciado (una sola vez)
    C->>CR: Enviar comando FIN (ganador)
    C->>C: GuardarResultadoFinal(...) + GestorArchivos.Flush()

    CR->>C2: comando FIN (ganador) (drenaje en Update)
    C2->>S2: Finalizar(ganador) — mismo ganador
    C2->>C2: _ganadorAnunciado = true
    C2->>C2: GuardarResultadoFinal(...) — MISMO contenido
    Note over C,C2: Ambas máquinas terminan con el mismo<br/>resultado_final.txt (antes divergían)
```

---

## 4. Mapa de concurrencia

Todos los hilos, locks y colas viven en el **Modelo** (regla de la materia). El Controlador solo consume en el hilo principal.

```mermaid
flowchart TB
    subgraph HILO_PRINCIPAL["Hilo principal (Unity) — Update()"]
        V["Vista.refrescar()"]
        PC["JuegoControlador.ProcesarMensajesRedPendientes()"]
        EN["JuegoControlador.EnviarPorRed() (drena cola)"]
    end

    subgraph MODELO["Modelo — Simulacion"]
        LOCK{{"lock(Candado)\nLock del mundo"}}
        T1["Task: Reloj (IniciarRelojAsync)"]
        T2["Task: Entrenamiento (por unidad)"]
        T3["Task: Construccion (por edificio)"]
        T4["Task: Recoleccion (por aldeano)"]
        T5["Task: Spawner de items (host)"]
        T6["Task: Expiracion del Casco"]
        T7["Task: Bucle de simulacion (Batalla Nivel 1)"]
        COLA_OUT["ConcurrentQueue _salientes\n(productor-consumidor)"]
    end

    subgraph ARCHIVOS["Modelo — GestorArchivos"]
        HILO_LOG["Thread 'HiloEscritorLogs' (IsBackground)"]
        COLA_LOG["BlockingCollection (cola de escritura)"]
    end

    subgraph RED["Modelo — ConectorRed"]
        HILO_RED["Thread 'HiloRedServidor' / 'HiloRedCliente' (IsBackground)"]
        COLA_IN["ConcurrentQueue _recibidos"]
        LOCK_ENV{{"lock(_lockEnvio)"}}
    end

    T1 & T2 & T3 & T4 & T5 & T6 & T7 -->|"mutan estado"| LOCK
    T1 & T2 & T3 & T4 & T5 & T6 & T7 -.->|"Transmitir()"| COLA_OUT
    COLA_OUT -->|"SiguienteSaliente()"| EN
    EN -->|"Enviar()"| LOCK_ENV
    HILO_RED -->|"ReadLine() bloqueante"| COLA_IN
    HILO_RED -.->|"Enviar()"| LOCK_ENV
    COLA_IN -->|"RecibirMensaje()"| PC
    PC -->|"metodos espejo *Rival"| LOCK
    V -->|"Instantanea()"| LOCK
    T1 & T2 & T3 & T4 & T5 & T6 & T7 -.->|"RegistrarAccion()"| COLA_LOG
    COLA_LOG --> HILO_LOG -->|"escribe a disco"| DISCO[(configuracion / log / resultado)]

    style LOCK fill:#ffe6e6,stroke:#cc0000
    style LOCK_ENV fill:#ffe6e6,stroke:#cc0000
    style COLA_OUT fill:#e6f2ff
    style COLA_IN fill:#e6f2ff
    style COLA_LOG fill:#e6f2ff
```

**Resumen numérico**

| Elemento | Cantidad | Detalle |
|---|---|---|
| Tasks del Modelo | 7 | reloj, entrenamiento, construcción, recolección, spawner, expiración Casco, bucle de batalla |
| Hilos (`Thread`) | 2 | escucha TCP (`ConectorRed`) + escritor de logs (`GestorArchivos`) |
| Candados (`lock`) | 3 | `Simulacion.Candado`, `ConectorRed._lockEnvio`, cola interna de `GestorArchivos` |
| Colas seguras | 3 | `_salientes`, `_recibidos`, cola de escritura de logs |

**Puntos clave para la defensa**

1. **Nadie escribe a un socket ni a disco dentro de `lock(Candado)`.** El Modelo **encola** (`_salientes`) y el Controlador envía desde el hilo principal; los logs se encolan y los escribe un único hilo.
2. **La Vista nunca toca las listas vivas:** pide `Instantanea()` una vez por frame (copia bajo candado) y dibuja sobre copias.
3. **Reconexión:** los bucles `CicloServidor`/`CicloCliente` vuelven a aceptar/reintentar; `AlConectar` reenvía el `SALUDO`.
4. **Batalla Nivel 1:** un solo hilo dentro del candado (sin carreras). El Nivel 2 (`Parallel.For`) queda como optimización futura.

---

## 5. Anexo — Arquitectura de la Vista (para el compañero)

La Vista no calcula reglas; solo dibuja y delega:

```mermaid
flowchart LR
    AR["ArranqueJuego\n(único dueño del JuegoControlador)"] --> JC["JuegoControlador"]
    subgraph VISTA["Vista (Unity) — MonoBehaviour"]
        HUD["HUD (oro, madera, comida, tiempo)"]
        TILES["MapaVista\n(dibuja 15x15 y entidades)"]
        UI["PanelAcciones\n(botones → Controlador)"]
        REDUI["PanelRed\n(Hospedar / Conectar)"]
        FIN["PanelFinPartida"]
    end
    AR --> HUD & TILES & UI & REDUI & FIN
    HUD & TILES & FIN -->|"Instantanea() 1 vez/frame"| JC
    UI & REDUI -->|"Mover / Construir / Entrenar / Atacar / Recoger... "| JC
    TILES -->|"Detener() en OnDestroy"| JC
```

**Reglas para la Vista:**
- Llamar `Instantanea()` **una sola vez por `Update()`** y dibujar desde esa copia.
- Llamar `ProcesarMensajesRedPendientes()` en `Update()` (drena la red).
- Llamar `Detener()` en `OnDestroy` / `OnApplicationQuit` (apaga el motor, cierra la red y hace `Flush` de logs).
- Mostrar `MensajesDescartados` en el HUD: si crece, la conexión se cayó (aviso temprano de desync).
- Dibujar los sprites mapeando los `enum` (`TipoUnidad`, `TipoEdificio`, `TipoRecurso`, `TipoItem`) a imágenes.
