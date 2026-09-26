using System.Collections.Generic;
using UnityEngine;
using Modelo;

namespace Vista
{
    // Pinta el mapa y las entidades desde InstantaneaJuego (copia segura).
    // Pool de sprites: cero Instantiate/Destroy por frame. Sin lógica de negocio.
    public class VistaTablero : MonoBehaviour
    {
        [Header("Arte (opcional — issue #5 del repo)")]
        [SerializeField] private Sprite[] spritesUnidad;    // indice = (int)TipoUnidad
        [SerializeField] private Sprite[] spritesEdificio;  // indice = (int)TipoEdificio
        [SerializeField] private Sprite[] spritesRecurso;   // indice = (int)TipoRecurso
        [SerializeField] private Sprite[] spritesItem;      // indice = (int)TipoItem
        [SerializeField] private Sprite spriteItem;         // fallback generico
        [SerializeField] private Sprite spriteTile;
        [SerializeField] private Sprite spriteTileAlt;    // moteado (pradera y bosque)
        [SerializeField] private Sprite spriteAgua;       // río decorativo
        [SerializeField] private Sprite spriteCiervo;     // caza mayor (Comida 600+)
        [SerializeField] private Color colorArena = new Color(1f, 0.87f, 0.55f);
        [SerializeField] private Color colorBosque = new Color(0.5f, 0.66f, 0.46f);

        [Header("Layout")]
        [SerializeField] private float tamanoCasilla = 1f;
        public float TamanoCasilla => tamanoCasilla;
        [SerializeField] private Color colorLocal = new Color(0.25f, 0.55f, 1f);
        [SerializeField] private Color colorEnemigo = new Color(1f, 0.35f, 0.3f);
        [SerializeField] private Color colorTileClaro = new Color(0.95f, 0.96f, 0.92f);

        private readonly List<SpriteRenderer> _pool = new List<SpriteRenderer>();
        private int _uso;
        // Rejilla estática: se construye UNA vez (no se reescribe cada frame).
        private readonly List<SpriteRenderer> _rejilla = new List<SpriteRenderer>();
        private GestorJuego _gestor;
        private readonly List<(int X, int Y)> _seleccion = new List<(int, int)>();

        // [Fluidez] Ultima posicion pintada de cada unidad: el Modelo late cada
        // 100 ms (1 celda/s) y aqui se interpola hacia la casilla destino para
        // que el sprite DESLICE en vez de saltar de cuadro en cuadro.
        private readonly Dictionary<Unidad, Vector3> _posSuave = new Dictionary<Unidad, Vector3>();
        private int _podaCada;
        // Posición suavizada de los ciervos (vagan por el bosque).
        private readonly Dictionary<Unidad, Vector3> _posCiervo = new Dictionary<Unidad, Vector3>();
        private int _podaCiervos;

        // Fantasma de construcción: huella que sigue al ratón en modo
        // construcción (verde = se puede, rojo = no). Lo pone/quita
        // ControlInputUsuario; aquí solo se dibuja.
        private TipoEdificio? _fantasmaTipo;
        private int _fantasmaX, _fantasmaY;
        private bool _fantasmaValido;

        public void MostrarFantasma(TipoEdificio tipo, int x, int y, bool valido)
        {
            _fantasmaTipo = tipo;
            _fantasmaX = x;
            _fantasmaY = y;
            _fantasmaValido = valido;
        }

        public void OcultarFantasma() => _fantasmaTipo = null;

        private static readonly Color[] ColoresUnidad =
        {
            new Color(0.95f, 0.85f, 0.55f), // Aldeano
            new Color(0.75f, 0.75f, 0.8f),  // Soldado
            new Color(0.55f, 0.75f, 0.45f), // Arquero
            new Color(0.85f, 0.55f, 0.35f), // Caballero
            new Color(0.6f, 0.45f, 0.3f),   // Ciervo
        };

        private static readonly Color[] ColoresEdificio =
        {
            new Color(0.55f, 0.45f, 0.35f), // Centro Urbano
            new Color(0.5f, 0.6f, 0.75f),   // Cuartel
            new Color(0.65f, 0.65f, 0.7f),  // Torre
            new Color(0.7f, 0.6f, 0.45f),   // Casa
        };

        // [Arte] Escala VISUAL por tipo (indice = (int)TipoEdificio). Casa y
        // Torre son 1x1 lógicos; Cuartel 2x2 y Centro 3x3 también en lógica.
        private static readonly float[] EscalasEdificio =
        {
            3.5f, // Centro Urbano (3x3)
            2.5f, // Cuartel (2x2)
            2f,   // Torre
            1.5f  // Casa
        };

        private static readonly Color[] ColoresRecurso =
        {
            new Color(0.4f, 0.75f, 0.3f),   // Madera
            new Color(1f, 0.84f, 0.2f),     // Oro
            new Color(0.95f, 0.55f, 0.35f), // Comida
            new Color(0.6f, 0.65f, 0.75f),  // Hierro
            new Color(0.75f, 0.75f, 0.78f), // Piedra
        };

        public void Inicializar(GestorJuego gestor)
        {
            _gestor = gestor;
            CargarArtePorCodigo();
        }

        // Sin sprites enlazados en el Inspector (issue #5): primero se intentan
        // los descargados (Assets/Resources/Sprites) y si faltan, el
        // SpriteFactory genera pixel-art temporal en memoria.
        // Lo que este enlazado se respeta.
        private void CargarArtePorCodigo()
        {
            if (ArteVacio(spritesUnidad)) spritesUnidad = SpriteFactory.Unidades();
            if (ArteVacio(spritesEdificio))
                spritesEdificio = ArteRecursos.CargarEdificios() ?? SpriteFactory.Edificios();
            if (ArteVacio(spritesRecurso))
                spritesRecurso = ArteRecursos.CargarRecursos() ?? SpriteFactory.Recursos();
            if (ArteVacio(spritesItem)) spritesItem = SpriteFactory.Items();
            if (spriteTile == null) spriteTile = ArteRecursos.CargarTile() ?? SpriteFactory.Tile();
            if (spriteTileAlt == null) spriteTileAlt = ArteRecursos.CargarTileAlt(); // null = todo base
            if (spriteAgua == null) spriteAgua = ArteRecursos.CargarSpriteAgua(); // null = base
            if (spriteCiervo == null) spriteCiervo = ArteRecursos.CargarSpriteCiervo(); // null = comida normal
        }

        private static bool ArteVacio(Sprite[] arr)
        {
            if (arr == null || arr.Length == 0) return true;
            foreach (Sprite s in arr)
                if (s != null) return false;
            return true;
        }

        public void MarcarSeleccion(int x, int y)
        {
            _seleccion.Clear();
            _seleccion.Add((x, y));
        }

        // Selección múltiple: resalta todas las casillas de la tropa elegida.
        public void MarcarSelecciones(IList<(int X, int Y)> celdas)
        {
            _seleccion.Clear();
            if (celdas == null) return;
            for (int i = 0; i < celdas.Count; i++)
                _seleccion.Add(celdas[i]);
        }

        public void LimpiarSeleccion()
        {
            _seleccion.Clear();
        }

        public void Actualizar(InstantaneaJuego foto)
        {
            if (foto == null) return;
            _uso = 0;

            // Rejilla estática (construida una sola vez).
            if (_rejilla.Count == 0) ConstruirRejilla();

            // Fantasma de construcción (si hay modo construcción activo).
            DibujarFantasma();

            // Recursos (debajo de unidades/edificios). Los árboles de bosque,
            // bien grandes, forman la masa forestal.
            foreach (Recurso r in foto.Recursos)
            {
                if (r.EstaAgotado) continue;
                Color c = ColorPorRecurso(r.Tipo);
                float escala = 0.85f;
                if (r.Tipo == TipoRecurso.Madera || r.Tipo == TipoRecurso.Piedra || r.Tipo == TipoRecurso.Hierro
                    || (r.Tipo == TipoRecurso.Oro))
                {
                    ArteRecursos.PesoBiomas(r.PosicionX, r.PosicionY, out float ar, out float bq);
                    if (r.Tipo == TipoRecurso.Madera && bq > 0.5f) escala = 2.5f;
                    else if ((r.Tipo == TipoRecurso.Piedra || r.Tipo == TipoRecurso.Hierro) && bq > 0.5f) escala = 1.5f;
                    else if ((r.Tipo == TipoRecurso.Oro || r.Tipo == TipoRecurso.Hierro) && ar > 0.5f) escala = 1.5f;
                }
                Dibujar(r.PosicionX, r.PosicionY, SpriteDe(spritesRecurso, (int)r.Tipo, spriteItem), c, escala, 0.9f);
            }

            // Fauna neutral (ciervos): se dibujan aparte y se deslizan al correr.
            if (foto.Animales != null)
            {
                foreach (Unidad c in foto.Animales)
                {
                    if (c == null || !c.EstaViva) continue;
                    Vector3 metaCiervo = PosMundo(c.PosicionX, c.PosicionY);
                    Vector3 pc;
                    if (!_posCiervo.TryGetValue(c, out pc)) pc = metaCiervo;
                    else
                    {
                        if ((pc - metaCiervo).sqrMagnitude > 9f) pc = metaCiervo;
                        pc = Vector3.MoveTowards(pc, metaCiervo, 3f * tamanoCasilla * Time.deltaTime);
                    }
                    _posCiervo[c] = pc;
                    if (spriteCiervo != null) Dibujar(pc, spriteCiervo, Color.white, 1.4f, 1f);
                    else Dibujar(pc, SpriteDe(spritesUnidad, (int)c.Tipo, spriteItem), Color.white, 1f, 1f);
                }
            }

            // Items (sprite propio por tipo; tinte blanco para no destiñir el arte).
            foreach (Item i in foto.Items)
            {
                Sprite sp = SpriteDe(spritesItem, (int)i.Tipo, null);
                if (sp != null) Dibujar(i.PosicionX, i.PosicionY, sp, Color.white, 0.7f, 1f);
                else Dibujar(i.PosicionX, i.PosicionY, spriteItem, Color.yellow, 0.7f, 1f);
            }

            // Edificios enemigos y locales (cada facción con su color).
            foreach (Edificio e in foto.EdificiosEnemigo)
                DibujarEdificio(e, e != null && !string.IsNullOrEmpty(e.Faccion)
                    ? ArteRecursos.ColorFaccion(e.Faccion) : colorEnemigo);
            foreach (Edificio e in foto.EdificiosLocal)
                DibujarEdificio(e, colorLocal);

            // Unidades: primero cuenta cuántas hay en cada casilla para poder
            // "apilarlas" con un offset (10 aldeanos juntos se ven épicos, no
            // uno encima del otro invisible).
            var porCasilla = new Dictionary<(int, int), int>();
            var todas = new List<Unidad>();
            foreach (Unidad u in foto.UnidadesEnemigo) todas.Add(u);
            foreach (Unidad u in foto.UnidadesLocal) todas.Add(u);
            foreach (Unidad u in todas)
            {
                if (u == null || !u.EstaViva) continue;
                var clave = (u.PosicionX, u.PosicionY);
                porCasilla.TryGetValue(clave, out int n);
                porCasilla[clave] = n + 1;
            }
            var vistos = new Dictionary<(int, int), int>();
            foreach (Unidad u in todas)
            {
                if (u == null) continue;
                Color equipo;
                if (foto.UnidadesLocal.Contains(u)) equipo = colorLocal;
                else
                {
                    // Tropa enemiga: color de su facción (base más cercana).
                    string f = ArteRecursos.FaccionDeTropa(u, foto.EdificiosEnemigo);
                    equipo = f != null ? ArteRecursos.ColorFaccion(f) : colorEnemigo;
                }
                var clave = (u.PosicionX, u.PosicionY);
                vistos.TryGetValue(clave, out int idx);
                vistos[clave] = idx + 1;
                porCasilla.TryGetValue(clave, out int n);
                DibujarUnidad(u, equipo, idx, n);
            }

            // Resaltado de selección (anillo sobre cada entidad elegida).
            for (int s = 0; s < _seleccion.Count; s++)
            {
                var fondo = Obtener();
                fondo.transform.position = PosMundo(_seleccion[s].X, _seleccion[s].Y) + new Vector3(0, 0, 0.1f);
                var sr = fondo.GetComponent<SpriteRenderer>();
                sr.sprite = spriteTile != null ? spriteTile : ObtenerSpriteBlanco();
                sr.color = new Color(1f, 1f, 0.3f, 0.35f);
                sr.sortingOrder = 16;
                sr.transform.localScale = Vector3.one * (tamanoCasilla * 0.95f);
                sr.gameObject.SetActive(true);
                _uso++;
            }

            // Capital enemiga (objetivo del regicidio): anillo dorado pulsante
            // sobre su huella para que se vea qué hay que tumbar.
            Edificio capital = _gestor != null && _gestor.Controlador != null
                ? _gestor.Controlador.CapitalEnemiga : null;
            if (capital != null && capital.EstaViva)
            {
                float pulso = 0.30f + 0.15f * Mathf.Sin(Time.time * 3f);
                for (int dx = 0; dx < capital.Lado; dx++)
                    for (int dy = 0; dy < capital.Lado; dy++)
                    {
                        var fondoCap = Obtener();
                        fondoCap.transform.position = PosMundo(capital.PosicionX + dx, capital.PosicionY + dy) + new Vector3(0, 0, 0.1f);
                        var srCap = fondoCap.GetComponent<SpriteRenderer>();
                        srCap.sprite = spriteTile != null ? spriteTile : ObtenerSpriteBlanco();
                        srCap.color = new Color(1f, 0.75f, 0.2f, pulso);
                        srCap.sortingOrder = 16;
                        srCap.transform.localScale = Vector3.one * (tamanoCasilla * 0.95f);
                        srCap.gameObject.SetActive(true);
                        _uso++;
                    }
            }

            // Ocultar sobrantes del pool de entidades.
            for (int k = _uso; k < _pool.Count; k++)
                _pool[k].gameObject.SetActive(false);

            // [Fluidez] Poda periodica de unidades que ya no existen.
            if (_posSuave.Count > 0 && ++_podaCada >= 30)
            {
                _podaCada = 0;
                var vivas = new HashSet<Unidad>();
                foreach (Unidad u in foto.UnidadesLocal) vivas.Add(u);
                foreach (Unidad u in foto.UnidadesEnemigo) vivas.Add(u);
                List<Unidad> fuera = null;
                foreach (Unidad u in _posSuave.Keys)
                    if (!vivas.Contains(u)) { if (fuera == null) fuera = new List<Unidad>(); fuera.Add(u); }
                if (fuera != null)
                    foreach (Unidad u in fuera) _posSuave.Remove(u);
            }

            // Poda de ciervos cazados (misma cadencia).
            if (_posCiervo.Count > 0 && ++_podaCiervos >= 30)
            {
                _podaCiervos = 0;
                var vivosC = new HashSet<Unidad>();
                if (foto.Animales != null) foreach (Unidad c in foto.Animales) vivosC.Add(c);
                List<Unidad> fueraC = null;
                foreach (Unidad c in _posCiervo.Keys)
                    if (!vivosC.Contains(c)) { if (fueraC == null) fueraC = new List<Unidad>(); fueraC.Add(c); }
                if (fueraC != null)
                    foreach (Unidad c in fueraC) _posCiervo.Remove(c);
            }
        }

        // La rejilla no cambia: se pinta una vez al arrancar (900 escrituras
        // menos por frame). Las entidades siguen usando el pool dinámico.
        // El tile y el tinte salen del BIOMA de cada casilla (pradera/arena/bosque).
        private void ConstruirRejilla()
        {
            for (int y = 0; y < Mapa.Alto; y++)
            {
                for (int x = 0; x < Mapa.Ancho; x++)
                {
                    TilePara(x, y, out Sprite t, out Color c);
                    var go = new GameObject($"Tile_{x}_{y}", typeof(SpriteRenderer));
                    go.transform.SetParent(transform, false);
                    go.transform.position = PosMundo(x, y);
                    var sr = go.GetComponent<SpriteRenderer>();
                    sr.sprite = t;
                    sr.color = c;
                    sr.sortingOrder = 0;
                    sr.transform.localScale = Vector3.one * tamanoCasilla;
                    _rejilla.Add(sr);
                }
            }
        }

        private void TilePara(int x, int y, out Sprite t, out Color c)
        {
            // Río decorativo (no bloquea): agua + orilla arenosa.
            if (ArteRecursos.EsRio(x, y))
            {
                t = spriteAgua != null ? spriteAgua : spriteTile;
                c = Color.white;
                if (t == null) t = ObtenerSpriteBlanco();
                return;
            }
            bool orilla = ArteRecursos.EsRio(x - 1, y) || ArteRecursos.EsRio(x + 1, y)
                       || ArteRecursos.EsRio(x, y - 1) || ArteRecursos.EsRio(x, y + 1);
            ArteRecursos.PesoBiomas(x, y, out float arena, out float bosque);
            bool moteado = ((x * 7 + y * 13) % 6 == 0)
                || (bosque > 0.4f && ((x * 3 + y * 5) % 4 == 0));
            t = (spriteTileAlt != null && moteado) ? spriteTileAlt : spriteTile;
            if (t == null) t = ObtenerSpriteBlanco();
            c = colorTileClaro;
            if (orilla) c = Color.Lerp(c, new Color(0.9f, 0.82f, 0.6f), 0.7f);
            if (arena > 0f) c = Color.Lerp(c, colorArena, arena);
            if (bosque > 0f) c = Color.Lerp(c, colorBosque, bosque);
        }

        private void DibujarEdificio(Edificio e, Color equipo)
        {
            float alfa = e.Estado == EstadoEdificio.EnConstruccion ? 0.55f : 1f;
            int i = (int)e.Tipo;
            float escala = (i >= 0 && i < EscalasEdificio.Length) ? EscalasEdificio[i] : 2f;
            // El sprite se centra en el CENTRO de la huella (no en el ancla):
            // si no, los edificios 2x2/3x3 se ven "corridos" de sus casillas.
            float c = (e.Lado - 1) / 2f;
            var centro = new Vector3((e.PosicionX + c) * tamanoCasilla, (e.PosicionY + c) * tamanoCasilla, 0f);
            Dibujar(centro,
                SpriteDe(spritesEdificio, i, spriteItem),
                Color.Lerp(equipo, ColoresEdificio[i], 0.45f), escala, alfa);
            DibujarBarraVida(centro, escala, e.Vida, e.VidaMaxima, escala * 0.55f);
        }

        // Delineado de la huella en modo construcción: casillas verdes si se
        // puede construir ahí, rojas si no, + silueta del edificio al 50%.
        private void DibujarFantasma()
        {
            if (!_fantasmaTipo.HasValue) return;
            TipoEdificio tipo = _fantasmaTipo.Value;
            int lado = DatosDelJuego.LadoSegunTipo(tipo);
            Color c = _fantasmaValido
                ? new Color(0.2f, 1f, 0.2f, 0.45f)
                : new Color(1f, 0.25f, 0.25f, 0.45f);
            for (int dx = 0; dx < lado; dx++)
                for (int dy = 0; dy < lado; dy++)
                {
                    int cx = _fantasmaX + dx, cy = _fantasmaY + dy;
                    if (cx < 0 || cy < 0 || cx >= Mapa.Ancho || cy >= Mapa.Alto) continue;
                    var sr = Obtener();
                    sr.transform.position = PosMundo(cx, cy);
                    sr.sprite = spriteTile != null ? spriteTile : ObtenerSpriteBlanco();
                    sr.color = c;
                    sr.sortingOrder = 2;
                    sr.transform.localScale = Vector3.one * tamanoCasilla;
                    sr.gameObject.SetActive(true);
                    _uso++;
                }
            Sprite sp = SpriteDe(spritesEdificio, (int)tipo, null);
            if (sp != null)
            {
                int i = (int)tipo;
                float escala = (i >= 0 && i < EscalasEdificio.Length) ? EscalasEdificio[i] : 2f;
                // Igual que el edificio real: silueta centrada en la huella.
                float centro = (DatosDelJuego.LadoSegunTipo(tipo) - 1) / 2f;
                var sr2 = Obtener();
                sr2.transform.position = new Vector3(
                    (_fantasmaX + centro) * tamanoCasilla, (_fantasmaY + centro) * tamanoCasilla, -0.05f);
                sr2.sprite = sp;
                sr2.color = new Color(1f, 1f, 1f, 0.5f);
                sr2.sortingOrder = 3;
                sr2.transform.localScale = Vector3.one * (tamanoCasilla * escala);
                sr2.gameObject.SetActive(true);
                _uso++;
            }
        }

        private void DibujarUnidad(Unidad u, Color equipo, int indiceEnCasilla, int totalEnCasilla)
        {
            Color baseC = ColoresUnidad[(int)u.Tipo];
            Color c = Color.Lerp(baseC, equipo, 0.35f);

            // [Fluidez] Interpola entre latidos del Modelo (1 celda/100 ms = 10
            // celdas/s): el sprite se desliza en vez de teletransportarse.
            Vector3 meta = PosMundo(u.PosicionX, u.PosicionY);

            // [Apilamiento] Varias unidades en la misma casilla: las reparte en
            // un círculo pequeño para que se vean TODAS (pila épica, no un pixel).
            if (totalEnCasilla > 1)
            {
                float angulo = (Mathf.PI * 2f * indiceEnCasilla) / totalEnCasilla;
                float radio = 0.28f * tamanoCasilla * Mathf.Sqrt(totalEnCasilla);
                if (totalEnCasilla > 6) radio = 0.34f * tamanoCasilla;
                meta += new Vector3(Mathf.Cos(angulo) * radio, Mathf.Sin(angulo) * radio, 0f);
            }

            Vector3 pos;
            if (!_posSuave.TryGetValue(u, out pos))
            {
                pos = meta; // unidad nueva: aparece ya en su casilla
            }
            else
            {
                if ((pos - meta).sqrMagnitude > 9f) pos = meta; // resincroniza saltos raros
                pos = Vector3.MoveTowards(pos, meta, 10f * tamanoCasilla * Time.deltaTime);
            }
            _posSuave[u] = pos;

            // Ligeramente más chica si hay pila, para que no se tapen del todo.
            float escala = totalEnCasilla > 4 ? 1.0f : 1.15f;
            if (totalEnCasilla > 1) escala = Mathf.Lerp(1.15f, 0.95f, Mathf.Clamp01((totalEnCasilla - 1) / 8f));
            Dibujar(pos, SpriteDe(spritesUnidad, (int)u.Tipo, spriteItem), c, escala, u.EstaViva ? 1f : 0.3f);
            DibujarBarraVida(pos, escala, u.Vida, u.VidaMaxima, 0.62f * escala);
        }

        // Barra de vida flotante (solo si está dañado): fondo oscuro + frente
        // de rojo a verde. Solo lee la foto; no toca el Modelo.
        private void DibujarBarraVida(Vector3 centro, float escala, int vida, int vidaMaxima, float altura)
        {
            if (vidaMaxima <= 0) return;
            float f = Mathf.Clamp01((float)vida / vidaMaxima);
            if (f >= 0.999f) return;
            float ancho = 0.9f * escala;
            var fondo = Obtener();
            fondo.transform.position = centro + new Vector3(0f, altura, -0.01f);
            fondo.sprite = ObtenerSpriteBlanco();
            fondo.color = new Color(0f, 0f, 0f, 0.6f);
            fondo.sortingOrder = 11;
            fondo.transform.localScale = new Vector3(ancho, 0.14f, 1f);
            fondo.gameObject.SetActive(true);
            _uso++;
            float w = Mathf.Max(ancho * f, 0.001f);
            var frente = Obtener();
            frente.transform.position = centro + new Vector3(-(ancho - w) / 2f, altura, -0.02f);
            frente.sprite = ObtenerSpriteBlanco();
            frente.color = Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.3f, 0.9f, 0.3f), f);
            frente.sortingOrder = 12;
            frente.transform.localScale = new Vector3(w, 0.1f, 1f);
            frente.gameObject.SetActive(true);
            _uso++;
        }

        private void Dibujar(int x, int y, Sprite sp, Color color, float escala, float alfa) =>
            Dibujar(PosMundo(x, y), sp, color, escala, alfa);

        private void Dibujar(Vector3 pos, Sprite sp, Color color, float escala, float alfa)
        {
            var sr = Obtener();
            sr.transform.position = pos + new Vector3(0, 0, -0.05f);
            sr.sprite = sp != null ? sp : ObtenerSpriteBlanco();
            sr.color = new Color(color.r, color.g, color.b, alfa);
            sr.sortingOrder = 10;
            sr.transform.localScale = Vector3.one * (tamanoCasilla * escala);
            sr.gameObject.SetActive(true);
            _uso++;
        }

        private Vector3 PosMundo(int x, int y) => new Vector3(x * tamanoCasilla, y * tamanoCasilla, 0f);

        private SpriteRenderer Obtener()
        {
            if (_uso < _pool.Count)
            {
                var existente = _pool[_uso];
                existente.gameObject.SetActive(true);
                return existente;
            }
            var go = new GameObject($"Marcador_{_pool.Count}", typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            var sr = go.GetComponent<SpriteRenderer>();
            _pool.Add(sr);
            return sr;
        }

        private static Sprite SpriteDe(Sprite[] arr, int indice, Sprite fallback)
        {
            if (arr != null && indice >= 0 && indice < arr.Length && arr[indice] != null)
                return arr[indice];
            return fallback;
        }

        private static Color ColorPorRecurso(TipoRecurso t) =>
            ColoresRecurso[(int)t];

        private static Sprite _blanco;
        private static Sprite ObtenerSpriteBlanco()
        {
            if (_blanco != null) return _blanco;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    tex.SetPixel(x, y, Color.white);
            tex.Apply();
            _blanco = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2f);
            return _blanco;
        }
    }
}
