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