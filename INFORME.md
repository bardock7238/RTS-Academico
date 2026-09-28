# INFORME TÉCNICO — "Imperios en Guerra"

> **Proyecto:** RTS-Academico — "Imperios en Guerra"
> **Curso:** Programación Avanzada
> **Docente:** Nancy Yaneth Gelvez García
> **Monitor:** Brayan Javier Ramírez Mendoza — Código: 20222020139
> **Motor:** Unity 6000.6.0f1 (Unity 6.6)
> **Lenguaje:** C#
> **Arquitectura:** Modelo-Vista-Controlador (MVC)

---

## 1. Resumen ejecutivo

"Imperios en Guerra" es un videojuego de estrategia en tiempo real (RTS) inspirado en Age of Empires, desarrollado en Unity con C#. El juego permite a dos jugadores competir por el control de un mapa administrando recursos, construyendo edificios y entrenando unidades.

El proyecto implementa:
- **Patrón MVC** con separación estricta de responsabilidades
- **Conurrencia real** mediante Tasks, Threads y locks
- **Comunicación en red** mediante sockets TCP
- **Manejo de archivos** para configuración, logs y resultados
- **Interfaz gráfica** en Unity (build de escritorio)

---

## 2. Arquitectura MVC

### 2.1 Modelo (Assets/Scripts/Modelo/)

El Modelo contiene **toda la lógica de negocio y la concurrencia**. Es 100% POCO (sin dependencias de UnityEngine), lo que permite probarlo fuera de Unity.

| Clase | Responsabilidad |
|-------|-----------------|
| `Simulacion` | Motor del mundo: 8 Tasks concurrentes, lock del mundo, pathfinding, batalla |
| `Jugador` | Datos del jugador: 5 recursos, unidades, edificios, mejoras |
| `Unidad` | Entidad del juego: vida, ataque, defensa, posición, estado |
| `Edificio` | Estructura construible: vida, huella, estado, unidades entrenables |
| `Recurso` | Yacimiento del mapa: tipo, cantidad |
| `Mapa` | Rejilla 140×140, validación, pathfinding BFS |
| `Partida` | Estado de la partida: tiempo, ganador, motivo de victoria |
| `ConectorRed` | Comunicación TCP: host/cliente, reconexión automática |
| `GestorArchivos` | Logs en disco: productor-consumidor con BlockingCollection |
| `IAEnemiga` | IA PVE en Task propia |
| `DatosDelJuego` | Catálogo central de costos y estadísticas |
| `InstantaneaJuego` | Foto inmutable del mundo para la Vista |
| `Tipos` | Enums: TipoUnidad, TipoEdificio, TipoRecurso, etc. |
| `Item` | Objeto sembrado en el mapa |

### 2.2 Vista (Assets/Scripts/Vista/)

La Vista es **"tonta"**: solo lee `InstantaneaJuego` (una copia segura del mundo) y dibuja. Nunca modifica el Modelo directamente.

| Clase | Responsabilidad |
|-------|-----------------|
| `GestorJuego` | Raíz de la Vista: crea el Controlador, auto-bootstrap de UI |
| `VistaTablero` | Pinta el mapa y entidades desde la instantánea |
| `ControlInputUsuario` | Traduce clics/teclas a acciones del Controlador |
| `HudRecursos` | Barra superior: 5 recursos + tiempo + mensajes |
| `MenuInicio` | Menú inicial: PVE/PVP, bases enemigas, ritmo |
| `MenuMercado` | Mercado de trueque (tecla T) |
| `MenuMejoras` | Herrería: 3 ramas × 3 niveles (tecla Y) |
| `Minimap` | Minimapa esquemático con marco de cámara |
| `PanelFinPartida` | Modal de victoria/derrota |
| `SonidoJuego` | Sonido procedural (sin assets) |
| `UiFabrica` | Fábrica compartida de UI 9-slice |
| `ArteRecursos` | Carga automática de sprites |
| `SpriteFactory` | Pixel-art 16×16 generado por código (fallback) |

### 2.3 Controlador (Assets/Scripts/Controlador/)

El Controlador es un **puente delgado sin hilos ni locks**:

```csharp
public bool MoverUnidad(Unidad unidad, int nuevoX, int nuevoY)
{
    if (!Motor.MoverUnidad(unidad, nuevoX, nuevoY)) return false;
    EnviarPorRed($"MOVER;{origenX};{origenY};{nuevoX};{nuevoY}");
    return true;
}
```

**Regla de oro:** El Controlador NUNCA crea hilos ni toma locks. Solo:
1. Traduce acciones de la Vista → métodos del Modelo
2. Traduce mensajes de red → métodos "Espejo" del Modelo
3. Anuncia por red y escribe logs

---

## 3. Concurrencia

### 3.1 Tasks del Modelo (8 Tasks)

| Task | Propósito | Frecuencia |
|------|-----------|------------|
| **Reloj** | Suma 1 segundo al tiempo de juego | Cada 1000 ms |
| **Entrenamiento** | Completa el entrenamiento de una unidad | Por unidad |
| **Construcción** | Completa la construcción de un edificio | Por edificio |
| **Recolección** | Recolecta recursos automáticamente | Por aldeano |
| **Spawner de items** | Siembra items en el mapa (solo host) | Cada N ms |
| **Expiración del Casco** | Quita la defensa bonus temporal | Por item |
| **Bucle de batalla** | Procesa ataques de unidades en rango | Continuo |
| **IA Enemiga** | Decisiones de la IA (PVE) | Cada 2000 ms |

### 3.2 Hilos (2 Threads)

| Hilo | Propósito | Namespace |
|------|-----------|-----------|
| **HiloRedServidor / HiloRedCliente** | Escucha TCP bloqueante | `System.Net.Sockets` |
| **HiloEscritorLogs** | Escribe logs en disco | `System.IO` |

### 3.3 Sincronización (3 Locks)

| Candado | Ubicación | Protege |
|---------|-----------|---------|
| `Simulacion.Candado` | Modelo | Unidades, edificios, recursos, items |
| `ConectorRed._lockEnvio` | Modelo | Escritura al socket |
| `GestorArchivos` (interno) | Modelo | Cola de escritura |

### 3.4 Colas thread-safe (3 Colas)

| Cola | Tipo | Uso |
|------|------|-----|
| `_salientes` | `ConcurrentQueue<string>` | Comandos para enviar por red |
| `_recibidos` | `ConcurrentQueue<string>` | Mensajes recibidos de red |
| Cola de logs | `BlockingCollection<Entrada>` | Escritura asíncrona a disco |

### 3.5 Regla fundamental

> **NUNCA se escribe a un socket ni a disco dentro de `lock(Candado)`.**

Las Tasks solo **encolan** (`_salientes`); el Controlador **drena** desde el hilo principal. Los logs se encolan y los escribe un único hilo.

### 3.6 UnityMainThreadDispatcher

La Vista nunca toca las listas vivas. En su lugar:
1. Llama `Instantanea()` una vez por frame (copia bajo candado)
2. Dibuja sobre esa copia
3. Llama al Controlador para acciones

---

## 4. Comunicación en red

### 4.1 Tecnología

**Sockets TCP** con `System.Net.Sockets`:
- `TcpListener` (host) / `TcpClient` (cliente)
- Puerto 5505 (juego) / 5517 (pruebas)
- Reconexión automática cada 1 segundo

### 4.2 Protocolo

Mensajes de texto separados por `;`:

```
SALUDO;NombreJugador
CONSTRUIR;TipoEdificio;x;y
ENTRENAR;TipoUnidad;x;y
MOVER;origenX;origenY;destinoX;destinoY
ATACAR;atacanteX;atacanteY;objetivoX;objetivoY;daño
RECOLECTAR;unidadX;unidadY;activar
ITEM;TipoItem;x;y
RECOGER_ITEM;TipoItem;x;y
FIN;ganador
```

### 4.3 Convergencia

Ambos jugadores ejecutan la **MISMA fórmula de daño**:
```csharp
daño = max(0, AtaqueTotal + BonoAtaque - (Defensa + BonusDefensa))
```

El atacante calcula el daño una vez y lo envía. El rival aplica el mismo daño. Así se evita enviar el estado completo.

### 4.4 Manejo de desconexión

- **Detección:** `MensajesDescartados` en el HUD
- **Reconexión:** Los bucles `CicloServidor` / `CicloCliente` reintentan cada 1s
- **Sincronización:** Al reconectar, se reenvía el `SALUDO`

---

## 5. Manejo de archivos

### 5.1 Archivos generados

| Archivo | Contenido | Formato |
|---------|-----------|---------|
| `configuracion.txt` | Configuración inicial del mapa | Texto plano |
| `log_partida.txt` | Registro de cada acción | `Turno / Acción / Resultado / Hora` |
| `resultado_final.txt` | Ganador y estado final | Texto plano |

### 5.2 Formato del log (según la guía)

```
Turno: Jugador 1
Acción: Ataque
Resultado: Impacto - Unidad enemiga destruida
Hora: 14:32:15

Turno: Jugador 1
Acción: Construcción
Resultado: Cuartel construido
Hora: 14:32:20
```

### 5.3 Escritura asíncrona

`GestorArchivos` usa el patrón **productor-consumidor**:
- Cualquier hilo llama `RegistrarAccion()` → solo encola (no bloqueante)
- Un único hilo escritor hace la I/O a disco
- `Flush()` espera a que todo quede escrito

---

## 6. Verificación de ganador

### 6.1 Reglas de derrota

| Jugador | Causa de derrota |
|---------|------------------|
| **Enemigo (IA)** | Capital destruida O Centro Urbano destruido |
| **Jugador local** | Centro Urbano destruido O Ejército aniquilado |

### 6.2 Método `VerificarGanador()`

```csharp
public void VerificarGanador()
{
    lock (Candado)
    {
        if (!EstadoPartida.EnEjecucion) return;
        
        string causaEnemigo = CausaDerrota(JugadorEnemigo, esEnemigo: true);
        if (causaEnemigo != null)
            FinalizarPartida(JugadorLocal, causaEnemigo);
        else
        {
            string causaLocal = CausaDerrota(JugadorLocal, esEnemigo: false);
            if (causaLocal != null)
                FinalizarPartida(JugadorEnemigo, causaLocal);
        }
    }
}
```

Se llama automáticamente después de:
- Destruir una unidad enemiga
- Destruir un edificio enemigo
- Cualquier ataque que pueda cambiar el resultado

---

## 7. Pruebas

### 7.1 Pruebas de escritorio (PruebasBatch.cs)

| Prueba | Descripción | Resultado |
|--------|-------------|-----------|
| `ProbarApiObligatoria` | Sin Controlador no hay juego | ✅ OK |
| `ProbarPveSinRed` | IA + reloj/ticks avanzan sin red | ✅ OK |
| `ProbarPvpHostCliente` | Host + cliente se conectan | ✅ OK |
| `ProbarArteDescargado` | Sprites cargan correctamente | ✅ OK |
| `ProbarAtaqueYVictoria` | Ataque entre unidades y condición de victoria | ✅ OK |
| `ProbarConcurrencia` | Múltiples Tasks sin condiciones de carrera | ✅ OK |

### 7.2 Ejecución

```bash
Unity.exe -batchmode -projectPath <repo> -executeMethod PruebasBatch.Todo -logFile - -quit
```

---

## 8. Estructura de carpetas

```
RTS-Academico/
├── Assets/
│   ├── Arte/          → Sprites (Kenney CC0, Toen Medieval CC-BY, PixelUI CC0)
│   ├── Escenas/       → Juego.unity
│   ├── Resources/     → Sprites del juego
│   ├── Scripts/
│   │   ├── Modelo/    → 14 clases POCO (sin UnityEngine)
│   │   ├── Controlador/ → 1 clase puente
│   │   └── Vista/      → 13 MonoBehaviours
│   └── Pruebas/       → 3 archivos de pruebas
├── Doc/               → Diagramas UML
├── Packages/          → manifest.json
├── ProjectSettings/   → Configuración Unity
├── DOCUMENTACION.md   → Documentación técnica completa
├── INFORME.md         → Este informe
├── AVANCE_PROYECTO.md → Estado detallado del proyecto
├── GUIA_VISTA.md      → Guía de implementación de la Vista
├── EXPOSICION.md      → Guía para la exposición oral
└── README.md          → Guía principal
```

---

## 9. Conceptos aplicados

| Concepto | Implementación |
|----------|----------------|
| **MVC** | Carpetas `Modelo/`, `Vista/`, `Controlador/` separadas |
| **POCO** | Modelo sin `UnityEngine` |
| **Tasks** | 8 Tasks en `Simulacion` |
| **Threads** | 2 Threads (red + logs) |
| **Locks** | 3 candados (`Simulacion`, `ConectorRed`, `GestorArchivos`) |
| **Colecciones concurrentes** | `ConcurrentQueue`, `BlockingCollection` |
| **Sockets TCP** | `System.Net.Sockets` |
| **Serialización** | Protocolo de texto con `;` |
| **Manejo de archivos** | `System.IO` con productor-consumidor |
| **Validación** | Todas las acciones validan recursos, rango, posición |
| **Manejo de excepciones** | try-catch en red, archivos, simulación |

---

## 10. Conclusiones

El proyecto cumple con todos los requisitos de la guía:

1. ✅ **MVC** con separación estricta de responsabilidades
2. ✅ **Conurrencia** real mediante Tasks, Threads y locks
3. ✅ **Comunicación en red** mediante sockets TCP
4. ✅ **Manejo de archivos** para configuración, logs y resultados
5. ✅ **Interfaz gráfica** en Unity (build de escritorio)
6. ✅ **Pruebas** de escritorio que verifican concurrencia, red y victoria
7. ✅ **Documentación** formal con diagramas UML y de flujo

---

**Fin del informe.**
