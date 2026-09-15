using System;
using System.IO;

namespace Controlador
{
    // Maneja configuracion.txt, log_partida.txt y resultado_final.txt.
    // No depende de UnityEngine, así que es fácil de probar fuera de Unity.
    // Tu compañero solo necesita hacer, en algún script de arranque de Unity:
    //   GestorArchivos.CarpetaDestino = Application.persistentDataPath;
    public static class GestorArchivos
    {
        public static string CarpetaDestino = Directory.GetCurrentDirectory();

        // [Concurrencia] Varios hilos de fondo escriben logs a la vez (recolección,
        // entrenamiento, red, reloj...). Este candado serializa las escrituras a disco.
        private static readonly object _lock = new object();

        private const string ArchivoConfiguracion = "configuracion.txt";
        private const string ArchivoLog = "log_partida.txt";
        private const string ArchivoResultado = "resultado_final.txt";

        private static string RutaCompleta(string nombreArchivo) =>
            Path.Combine(CarpetaDestino, nombreArchivo);

        public static void GuardarConfiguracionInicial(string contenido)
        {
            lock (_lock)
            {
                try
                {
                    File.WriteAllText(RutaCompleta(ArchivoConfiguracion), contenido + Environment.NewLine);
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"Error al guardar configuración: {ex.Message}");
                }
            }
        }

        // En RTS en tiempo real no hay turnos: cada entrada registra la hora real
        // y qué jugador hizo la acción, así ningún hilo se pisa al escribir.
        public static void RegistrarAccion(string jugador, string accion, string resultado)
        {
            lock (_lock)
            {
                try
                {
                    string entrada =
                        $"Tiempo: {DateTime.Now:HH:mm:ss}{Environment.NewLine}" +
                        $"Jugador: {jugador}{Environment.NewLine}" +
                        $"Acción: {accion}{Environment.NewLine}" +
                        $"Resultado: {resultado}{Environment.NewLine}{Environment.NewLine}";

                    File.AppendAllText(RutaCompleta(ArchivoLog), entrada);
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"Error al registrar acción: {ex.Message}");
                }
            }
        }

        public static void GuardarResultadoFinal(string contenido)
        {
            lock (_lock)
            {
                try
                {
                    File.WriteAllText(RutaCompleta(ArchivoResultado), contenido + Environment.NewLine);
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"Error al guardar resultado final: {ex.Message}");
                }
            }
        }
    }
}