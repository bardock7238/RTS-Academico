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

        // Turno: Jugador 1
        // Acción: Ataque
        // Resultado: Impacto - Unidad enemiga destruida
        public static void RegistrarAccion(string jugador, string accion, string resultado)
        {
            lock (_lock)
            {
                try
                {
                    string entrada =
                        $"Turno: {jugador}{Environment.NewLine}" +
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