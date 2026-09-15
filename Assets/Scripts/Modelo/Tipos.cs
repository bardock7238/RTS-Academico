namespace Modelo
{
    // Tipos de unidades que existen en el juego.
    public enum TipoUnidad
    {
        Aldeano,
        Soldado,
        Arquero,
        Caballero
    }

    // Tipos de edificios construibles.
    public enum TipoEdificio
    {
        CentroUrbano,
        Cuartel,
        Torre,
        Casa
    }

    // Tipos de recursos presentes en el mapa.
    public enum TipoRecurso
    {
        Madera,
        Oro,
        Comida
    }

    // Tipos de items que el host siembra en el mapa (uno por categoría, todos
    // objetos realistas — nada de fantasía): Yogur = CONSUMIBLE (cura), Casco =
    // TEMPORAL (defensa), Espada = EQUIPABLE (ataque), Herramientas = PASIVO
    // (más producción). El yogur es un guiño del equipo: solo "tropas griegas".
    public enum TipoItem
    {
        Yogur,          // Cura (consumible). Por ahora todas las tropas son el
                        // ejército griego, así que aplica a todas. Si se añaden
                        // civilizaciones, habrá que filtrar por civilización.
        Casco,          // +Defensa por X segundos (temporal).
        Espada,         // +Ataque mientras la lleve una unidad (equipable).
        Herramientas    // +Producción de recursos permanente (pasivo).
    }

    // Estado en el que puede encontrarse una unidad.
    public enum EstadoUnidad
    {
        Idle,
        Moviendo,
        Recolectando,
        Construyendo,
        Atacando
    }

    // Estado en el que puede encontrarse un edificio.
    public enum EstadoEdificio
    {
        EnConstruccion,
        Operativo,
        Destruido
    }
}