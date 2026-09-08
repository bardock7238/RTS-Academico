# RTS Académico

Proyecto en Unity para un juego de estrategia en tiempo real, inspirado en los principios generales de *Age of Empires*. El equipo está formado por dos personas: una responsable principalmente del diseño y otra del desarrollo.

## Abrir el proyecto

En Unity Hub, selecciona **Add** y elige esta carpeta. El proyecto usa Unity 6.0.6f1.

## Organización inicial

- `Assets/Arte`: recursos que Unity usa directamente: modelos, texturas, animaciones, audio, interfaz y prefabs.
- `Assets/Escenas`: escenas del juego.
- `Assets/Datos`: configuraciones del juego que se crearán con ScriptableObjects.
- `Assets/Scripts`: código separado entre reglas del juego (`Dominio`), sistemas (`Sistemas`) y elementos visibles de Unity (`Presentacion`).
- `Assets/Pruebas`: futuras pruebas de edición y de juego.
- `Diseno`: bocetos, referencias y archivos fuente de diseño. Unity no los importa automáticamente.
- `Docs`: decisiones de diseño, desarrollo y contexto técnico.

La distribución del trabajo inicial está en [Docs/Desarrollo/GuiaDeTrabajoEnPareja.md](Docs/Desarrollo/GuiaDeTrabajoEnPareja.md).
