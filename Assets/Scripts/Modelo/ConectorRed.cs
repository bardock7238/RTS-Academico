using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Modelo
{
    // Tablero de comunicación TCP entre dos instancias del juego.
    //
    // Quién hace qué:
    //   - El hilo de escucha SOLO lee del tubo y mete líneas en una cola (no
    //     toca el juego). Así el hilo principal puede bloquearse leyendo la red
    //     sin congelar la partida.
    //   - El hilo principal del juego pregunta HayMensajes / RecibirMensaje y
    //     aplica cada acción. Eso lo hace el Controlador (Vista ya no).
    //
    // Protocolo (separador ';'): MOVER;x1;y1;x2;y2 | ATACAR;xa;ya;xb;yb;dano |
    // ATACAR_EDIFICIO;xa;ya;xe;ye;ataque | CONSTRUIR;Tipo;x;y | ENTRENAR;Tipo;x;y |
    // SALUDO;nombre | RECOLECTAR;x;y;1|0 | ITEM;TipoItem;x;y | RECOGER_ITEM;TipoItem;x;y
    public class ConectorRed : IDisposable
    {
        public bool EstaConectado { get; private set; }
        public string UltimoError { get; private set; }

        // Se dispara CADA vez que se consigue una conexión (la inicial y cada reconexión).
        public event Action AlConectar;

        // [Concurrencia] Cola segura entre hilos: la llena el hilo de escucha de la
        // red; la vacía el hilo del juego. Nunca se disputan la misma escritura.
        private readonly ConcurrentQueue<string> _recibidos = new ConcurrentQueue<string>();

        private TcpListener _servidor;   // Solo si soy el host (espero llamadas).
        private TcpClient _conexion;     // El tubo ya conectado (cliente o aceptado).
        private StreamReader _lector;
        private StreamWriter _escritor;
        private Thread _hiloEscucha;
        private volatile bool _ejecutando;

        // [Concurrencia] Dos hilos pueden querer escribir a la vez (juego + otro hilo);
        // este candado serializa los envíos por el mismo tubo TCP.
        private readonly object _lockEnvio = new object();

        public bool HayMensajes => !_recibidos.IsEmpty;

        // MODO SERVIDOR (host): abro la casilla y espero 1 jugador

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

            _hiloEscucha = new Thread(CicloServidor)
            {
                IsBackground = true,
                Name = "HiloRedServidor"
            };
            _hiloEscucha.Start();
            return true;
        }

        // [Concurrencia] Hilo servidor: accepta, lee (bloqueante), y si el rival cae,
        // vuelve en bucle a la casilla — reconexión sin morir el hilo.
        private void CicloServidor()
        {
            while (_ejecutando)
            {
                try
                {
                    TcpClient cliente = _servidor.AcceptTcpClient(); // bloquea hasta que alguien llame
                    AbrirConexion(cliente);
                    Escuchar();            // bloquea hasta que el rival cague o cierre
                    CerrarConexionActual(); // listo: volvemos a esperar al siguiente
                }
                catch (Exception ex)
                {
                    if (_ejecutando) UltimoError = ex.Message;
                }
            }
        }

        // MODO CLIENTE: llamo al servidor

        public bool Conectar(string ip, int puerto)
        {
            _ejecutando = true;
            _hiloEscucha = new Thread(() => CicloCliente(ip, puerto))
            {
                IsBackground = true,
                Name = "HiloRedCliente"
            };
            _hiloEscucha.Start();
            return true;
        }

        // [Concurrencia] Hilo cliente: conecta, lee (bloqueante), y si el host se cae,
        // duerme 1 s y reintenta — reconexión automática en segundo plano.
        private void CicloCliente(string ip, int puerto)
        {
            while (_ejecutando)
            {
                try
                {
                    TcpClient cliente = new TcpClient();
                    cliente.Connect(ip, puerto);
                    AbrirConexion(cliente);
                    Escuchar();            // bloquea hasta que el host se caiga
                    CerrarConexionActual(); // volvemos a intentar en 1 segundo
                }
                catch (Exception)
                {
                    // No hubo servidor (o se cayó): dormimos 1s y reintentamos.
                    if (!_ejecutando) break;
                    Thread.Sleep(1000);
                }
            }
        }

        private void AbrirConexion(TcpClient cliente)
        {
            _conexion = cliente;
            NetworkStream flujo = cliente.GetStream();
            _escritor = new StreamWriter(flujo, Encoding.UTF8) { AutoFlush = true };
            _lector = new StreamReader(flujo, Encoding.UTF8);
            EstaConectado = true;   // primero el flag (para que Enviar no falle)
            AlConectar?.Invoke();   // avisar al Controlador (reenviará el SALUDO)
        }

        // LECTURA EN SEGUNDO PLANO (bloqueante a propósito)

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

        // PARA EL HILO PRINCIPAL DEL JUEGO

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
                try
                {
                    _escritor.WriteLine(mensaje);
                    return true;
                }
                catch (IOException ex)
                {
                    // El tubo murió (rival caído, red cortada): se marca desconectado
                    // y no se propaga la excepción (el hilo que llamó no debe morir).
                    EstaConectado = false;
                    GestorArchivos.RegistrarAccion("Sistema", "Red",
                        "Fallo al enviar (tubo roto): " + ex.Message);
                    return false;
                }
                catch (SocketException ex)
                {
                    EstaConectado = false;
                    GestorArchivos.RegistrarAccion("Sistema", "Red",
                        "Fallo al enviar (socket): " + ex.Message);
                    return false;
                }
                catch (ObjectDisposedException ex)
                {
                    EstaConectado = false;
                    GestorArchivos.RegistrarAccion("Sistema", "Red",
                        "Tubo cerrado al enviar: " + ex.Message);
                    return false;
                }
            }
        }

        // Cierra SOLO el tubo actual (sin matar los ciclos de reconexión).
        // Útil en el juego (¿reset?) y en las pruebas para simular una caída.
        public void CortarConexion()
        {
            CerrarConexionActual();
        }

        private void CerrarConexionActual()
        {
            lock (_lockEnvio)
            {
                try { _escritor?.Close(); } catch { }
                try { _lector?.Close(); } catch { }
                try { _conexion?.Close(); } catch { }
                _escritor = null;
                _lector = null;
                _conexion = null;
            }
            EstaConectado = false;
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