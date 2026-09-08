# Guía de trabajo en pareja

## Responsabilidades iniciales

### Diseño - compañero

- Define el estilo visual de unidades, edificios, recursos y la interfaz.
- Guarda bocetos y referencias en `Diseno/Bocetos` y `Diseno/Referencias`.
- Conserva los archivos editables originales (por ejemplo, Blender, Krita o Aseprite) en `Diseno/Fuente`.
- Cuando un recurso esté listo para Unity, exporta una copia optimizada a la carpeta correspondiente de `Assets/Arte`.
- Documenta decisiones visuales en `Docs/Diseno`.

### Desarrollo - responsable principal

- Implementa reglas, interacción, datos y comportamiento de juego dentro de `Assets/Scripts`.
- Crea y mantiene las escenas en `Assets/Escenas`.
- Agrega pruebas en `Assets/Pruebas` cuando se implementen sistemas importantes.
- Registra decisiones técnicas en `Docs/Desarrollo`.

### Trabajo compartido

- Acordar qué debe hacer cada unidad, edificio o recurso antes de integrarlo.
- Probar juntos los recursos y la jugabilidad dentro de Unity.
- Hacer cambios pequeños en Git con mensajes claros en español.

## Regla para los recursos

`Diseno` es el espacio de trabajo creativo y puede contener archivos pesados o editables. `Assets/Arte` contiene únicamente las versiones listas para que Unity las importe y use en el juego.

## Primer objetivo de desarrollo

Esperar los requisitos detallados antes de implementar mecánicas. Cuando lleguen, se definirá una primera entrega pequeña y comprobable.
