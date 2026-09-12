using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Controlador
{
    // Tablero de comunicación TCP entre dos instancias del juego.
    //
    // Quién hace qué:
    //   - El hilo de escucha SOLO lee del tubo y mete líneas en una cola (no
    //     toca el juego). Así el hilo principal puede bloquearse leyendo la red
    //     sin congelar la partida.
    //   - El hilo principal del juego pregunta HayMensajes / RecibirMensaje y
    //     aplica cada acción. Eso lo hace el Controlador (Vista ya no).
    public class ConectorRed : IDisposable
    {
        public bool EstaConectado { get; private set; }
        public string UltimoError { get; private set; }

        // Cola segura entre hilos: diferencias de punto de la red → hilo de juego.
        private readonly ConcurrentQueue<string> _recibidos = new ConcurrentQueue<string>();

        private TcpListener _servidor;   // Solo si soy el host (espero llamadas).
        private TcpClient _conexion;     // El tubo ya conectado (cliente o aceptado).
        private StreamReader _lector;
        private StreamWriter _escritor;
        private Thread _hiloEscucha;
        private volatile bool _ejecutando;

        // Dos hilos pueden querer escribir a la vez (juego + otro hilo); se serializan aquí.
        private readonly object _lockEnvio = new object();

        public bool HayMensajes => !_recibidos.IsEmpty;

        // ============ MODO SERVIDOR (host): abro la casilla y espero 1 jugador ============

        public bool IniciarHost(int puerto)
        {
            _ejecutando = true;
            try
            {
                _servidor = new TcpListener(IPAddress.Any, puerto);
                _servidor.Start();
            }
            catch (Exception ex)
            {
                UltimoError = ex.Message;
                return false;
            }

            _hiloEscucha = new Thread(AceptarClienteYEscuchar)
            {
                IsBackground = true,
                Name = "HiloRedServidor"
            };
            _hiloEscucha.Start();
            return true;
        }

        private void AceptarClienteYEscuchar()
        {
            try
            {
                TcpClient cliente = _servidor.AcceptTcpClient();
                AbrirConexion(cliente);
                Escuchar();
            }
            catch (Exception ex)
            {
                // Si nos están cerrando (_ejecutando = false), ignorarlo es lo correcto.
                if (_ejecutando) UltimoError = ex.Message;
            }
        }

        // ============ MODO CLIENTE: llamo al servidor ============

        public bool Conectar(string ip, int puerto)
        {
            _ejecutando = true;
            try
            {
                TcpClient cliente = new TcpClient();
                cliente.Connect(ip, puerto);
                AbrirConexion(cliente);
            }
            catch (Exception ex)
            {
                UltimoError = ex.Message;
                return false;
            }

            _hiloEscucha = new Thread(Escuchar)
            {
                IsBackground = true,
                Name = "HiloRedCliente"
            };
            _hiloEscucha.Start();
            return true;
        }

        private void AbrirConexion(TcpClient cliente)
        {
            _conexion = cliente;
            NetworkStream flujo = cliente.GetStream();
            _escritor = new StreamWriter(flujo, Encoding.UTF8) { AutoFlush = true };
            _lector = new StreamReader(flujo, Encoding.UTF8);
            EstaConectado = true;
        }

        // ============ LECTURA EN SEGUNDO PLANO (bloqueante a propósito) ============

        private void Escuchar()
        {
            try
            {
                while (_ejecutando)
                {
                    string linea = _lector.ReadLine();
                    if (linea == null) break; // El otro cerró el tubo.
                    _recibidos.Enqueue(linea);
                }
            }
            catch (IOException)
            {
                // El tubo se cerró: ya no hay más mensajes. No es un error grave.
            }
            catch (Exception ex)
            {
                UltimoError = ex.Message;
            }
            finally
            {
                EstaConectado = false;
            }
        }

        // ============ PARA EL HILO PRINCIPAL DEL JUEGO ============

        public string RecibirMensaje()
        {
            if (_recibidos.TryDequeue(out string mensaje))
                return mensaje;
            return null;
        }

        public bool Enviar(string mensaje)
        {
            lock (_lockEnvio)
            {
                if (!EstaConectado || _escritor == null) return false;
                _escritor.WriteLine(mensaje);
                return true;
            }
        }

        public void Cerrar()
        {
            _ejecutando = false;
            try { _escritor?.Close(); } catch { }
            try { _lector?.Close(); } catch { }
            try { _conexion?.Close(); } catch { }
            try { _servidor?.Stop(); } catch { }

            if (_hiloEscucha != null && _hiloEscucha.ThreadState != ThreadState.Unstarted)
            {
                _hiloEscucha.Join(1000); // Cerramos el tubo → ReadLine sale solo.
            }
            EstaConectado = false;
        }

        public void Dispose()
        {
            Cerrar();
        }

        // La IP que debe compartir el host con su compañero para poder conectarse.
        public static string ObtenerIpLocal()
        {
            try
            {
                foreach (IPAddress ip in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                        return ip.ToString();
                }
            }
            catch { }
            return "127.0.0.1";
        }
    }
}