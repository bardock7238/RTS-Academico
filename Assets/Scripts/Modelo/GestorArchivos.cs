using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace Modelo
{
    // Maneja configuracion.txt, log_partida.txt y resultado_final.txt.
    // No depende de UnityEngine, así que es fácil de probar fuera de Unity.
    // Desde la Vista (en algún script de arranque de Unity):
    //   GestorArchivos.CarpetaDestino = Application.persistentDataPath;
    //
    // [Concurrencia] Productor-consumidor: muchos hilos de fondo escriben logs a la
    // vez (recolección, entrenamiento, red, reloj...). En lugar de tocar el disco en
    // el hilo que llama (que muchas veces está dentro de lock(Candado)), cada llamada
    // solo ENCOLA el texto (no bloqueante) y UN único hilo escritor hace la I/O a
    // disco. Así la escritura de archivos nunca frena la simulación.
    public static class GestorArchivos
    {
        public static string CarpetaDestino = Directory.GetCurrentDirectory();

        private const string ArchivoConfiguracion = "configuracion.txt";
        private const string ArchivoLog = "log_partida.txt";
        private const string ArchivoResultado = "resultado_final.txt";

        // Identifica a qué archivo va cada entrada de la cola.
        private const int DestinoConfiguracion = 0;
        private const int DestinoLog = 1;
        private const int DestinoResultado = 2;

        private struct Entrada
        {
            public int Destino;
            public string Contenido;         // null = centinela de sincronización (Flush)
            public ManualResetEventSlim Aviso; // para Flush: se señaliza al escribirse
        }

        // [Concurrencia] La cola bloqueante entre los productores (cualquier hilo del
        // juego) y el único consumidor (el hilo escritor de abajo).
        private static readonly BlockingCollection<Entrada> _cola = new BlockingCollection<Entrada>();

        static GestorArchivos()
        {
            Thread escritor = new Thread(EscribirEnSegundoPlano)
            {
                IsBackground = true,
                Name = "HiloEscritorLogs"
            };
            escritor.Start();
        }

        // El único lugar del proceso que toca el disco: vacía la cola y escribe cada
        // archivo. Corre en un solo hilo → no hace falta candado en las escrituras.
        private static void EscribirEnSegundoPlano()
        {
            foreach (Entrada entrada in _cola.GetConsumingEnumerable())
            {
                if (entrada.Contenido == null)
                {
                    entrada.Aviso?.Set();
                    continue;
                }
                try
                {
                    string ruta = Path.Combine(CarpetaDestino, NombreArchivo(entrada.Destino));
                    if (entrada.Destino == DestinoLog)
                        File.AppendAllText(ruta, entrada.Contenido);
                    else
                        File.WriteAllText(ruta, entrada.Contenido);
                }
                catch (Exception ex)
                {
                    // Sin permisos, carpeta que no existe, archivo ocupado... se anota
                    // en consola y se sigue; el juego no debe caerse por un log.
                    try { Console.WriteLine($"Error al escribir {NombreArchivo(entrada.Destino)}: {ex.Message}"); }
                    catch { }
                }
            }
        }

        private static string NombreArchivo(int destino)
        {
            switch (destino)
            {
                case DestinoConfiguracion: return ArchivoConfiguracion;
                case DestinoResultado: return ArchivoResultado;
                default: return ArchivoLog;
            }
        }

        // Espera a que todo lo encolado hasta ahora quede escrito en disco (útil para
        // pruebas que leen los archivos o antes de cerrar el proceso).
        public static void Flush()
        {
            ManualResetEventSlim aviso = new ManualResetEventSlim(false);
            _cola.Add(new Entrada { Destino = 0, Contenido = null, Aviso = aviso });
            aviso.Wait(1000);
        }

        public static void GuardarConfiguracionInicial(string contenido)
        {
            _cola.Add(new Entrada
            {
                Destino = DestinoConfiguracion,
                Contenido = contenido + Environment.NewLine
            });
        }

        // En RTS en tiempo real no hay turnos: cada entrada registra la hora real
        // y qué jugador hizo la acción, así ningún hilo se pisa al escribir.
        public static void RegistrarAccion(string jugador, string accion, string resultado)
        {
            string entrada =
                $"Tiempo: {DateTime.Now:HH:mm:ss}{Environment.NewLine}" +
                $"Jugador: {jugador}{Environment.NewLine}" +
                $"Acción: {accion}{Environment.NewLine}" +
                $"Resultado: {resultado}{Environment.NewLine}{Environment.NewLine}";

            _cola.Add(new Entrada { Destino = DestinoLog, Contenido = entrada });
        }

        public static void GuardarResultadoFinal(string contenido)
        {
            _cola.Add(new Entrada
            {
                Destino = DestinoResultado,
                Contenido = contenido + Environment.NewLine
            });
            // El resultado se lee justo al terminar (sin Flush intermedio):
            // se espera al escritor para que quede en disco al retornar.
            // El disco lo sigue tocando SOLO el hilo escritor; aquí solo se
            // espera su centinela (nunca se toma lock(Candado) en ese hilo,
            // así que no hay interbloqueo). Una vez por partida: sin costo.
            Flush();
        }
    }
}