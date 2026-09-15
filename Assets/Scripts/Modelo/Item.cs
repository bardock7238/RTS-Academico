namespace Modelo
{
    // [Concurrencia] Un item es un OBJETO del mundo que nadie mueve: aparece solo
    // (lo siembra el Task del spawner en el host), se recoge bajo lock(_lockJuego)
    // y su efecto puede expirar con otro Task (el Casco). Por eso interactúa a
    // través de listas y candados, igual que un Recurso.
    public class Item
    {
        public TipoItem Tipo { get; set; }
        public int PosicionX { get; set; }
        public int PosicionY { get; set; }
        public bool Recogido { get; set; }

        public Item(TipoItem tipo, int x, int y)
        {
            Tipo = tipo;
            PosicionX = x;
            PosicionY = y;
            Recogido = false;
        }
    }
}