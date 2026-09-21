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

        [Header("Layout")]
        [SerializeField] private float tamanoCasilla = 1f;
        [SerializeField] private Color colorLocal = new Color(0.25f, 0.55f, 1f);
        [SerializeField] private Color colorEnemigo = new Color(1f, 0.35f, 0.3f);
        [SerializeField] private Color colorTileClaro = new Color(0.28f, 0.42f, 0.24f);
        [SerializeField] private Color colorTileOscuro = new Color(0.24f, 0.36f, 0.21f);

        private readonly List<SpriteRenderer> _pool = new List<SpriteRenderer>();
        private int _uso;
        private GestorJuego _gestor;
        private int _ultimoSeleccionX = int.MinValue;
        private int _ultimoSeleccionY = int.MinValue;
        private bool _haySeleccion;

        // [Fluidez] Ultima posicion pintada de cada unidad: el Modelo late cada
        // 100 ms (1 celda/s) y aqui se interpola hacia la casilla destino para
        // que el sprite DESLICE en vez de saltar de cuadro en cuadro.
        private readonly Dictionary<Unidad, Vector3> _posSuave = new Dictionary<Unidad, Vector3>();
        private int _podaCada;

        private static readonly Color[] ColoresUnidad =
        {
            new Color(0.95f, 0.85f, 0.55f), // Aldeano
            new Color(0.75f, 0.75f, 0.8f),  // Soldado
            new Color(0.55f, 0.75f, 0.45f), // Arquero
            new Color(0.85f, 0.55f, 0.35f), // Caballero
        };

        private static readonly Color[] ColoresEdificio =
        {
            new Color(0.55f, 0.45f, 0.35f), // Centro Urbano
            new Color(0.5f, 0.6f, 0.75f),   // Cuartel
            new Color(0.65f, 0.65f, 0.7f),  // Torre
            new Color(0.7f, 0.6f, 0.45f),   // Casa
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

        // Sin sprites enlazados en el Inspector (issue #5): el SpriteFactory
        // genera pixel-art temporal en memoria. Lo que este enlazado se respeta.
        private void CargarArtePorCodigo()
        {
            if (ArteVacio(spritesUnidad)) spritesUnidad = SpriteFactory.Unidades();
            if (ArteVacio(spritesEdificio)) spritesEdificio = SpriteFactory.Edificios();
            if (ArteVacio(spritesRecurso)) spritesRecurso = SpriteFactory.Recursos();
            if (ArteVacio(spritesItem)) spritesItem = SpriteFactory.Items();
            if (spriteTile == null) spriteTile = SpriteFactory.Tile();
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
            _haySeleccion = true;
            _ultimoSeleccionX = x;
            _ultimoSeleccionY = y;
        }

        public void LimpiarSeleccion()
        {
            _haySeleccion = false;
        }

        public void Actualizar(InstantaneaJuego foto)
        {
            if (foto == null) return;
            _uso = 0;

            // Rejilla: se redibuja cada frame con el pool (sin Instantiate).
            DibujarRejilla();

            // Recursos (debajo de unidades/edificios).
            foreach (Recurso r in foto.Recursos)
            {
                if (r.EstaAgotado) continue;
                Color c = ColorPorRecurso(r.Tipo);
                Dibujar(r.PosicionX, r.PosicionY, SpriteDe(spritesRecurso, (int)r.Tipo, spriteItem), c, 0.85f, 0.9f);
            }

            // Items (sprite propio por tipo; tinte blanco para no destiñir el arte).
            foreach (Item i in foto.Items)
            {
                Sprite sp = SpriteDe(spritesItem, (int)i.Tipo, null);
                if (sp != null) Dibujar(i.PosicionX, i.PosicionY, sp, Color.white, 0.7f, 1f);
                else Dibujar(i.PosicionX, i.PosicionY, spriteItem, Color.yellow, 0.7f, 1f);
            }

            // Edificios enemigos y locales
            foreach (Edificio e in foto.EdificiosEnemigo)
                DibujarEdificio(e, colorEnemigo);
            foreach (Edificio e in foto.EdificiosLocal)
                DibujarEdificio(e, colorLocal);

            // Unidades
            foreach (Unidad u in foto.UnidadesEnemigo)
                DibujarUnidad(u, colorEnemigo);
            foreach (Unidad u in foto.UnidadesLocal)
                DibujarUnidad(u, colorLocal);

            // Resaltado de selección (anillo detrás de la entidad).
            if (_haySeleccion)
            {
                var fondo = Obtener();
                fondo.transform.position = PosMundo(_ultimoSeleccionX, _ultimoSeleccionY) + new Vector3(0, 0, 0.1f);
                var sr = fondo.GetComponent<SpriteRenderer>();
                sr.sprite = spriteTile != null ? spriteTile : ObtenerSpriteBlanco();
                sr.color = new Color(1f, 1f, 0.3f, 0.35f);
                sr.sortingOrder = 5;
                sr.transform.localScale = Vector3.one * (tamanoCasilla * 0.95f);
            }

            // Ocultar sobrantes del pool (la rejilla ocupa [0, 900)).
            int rejilla = Mapa.Ancho * Mapa.Alto;
            for (int k = Mathf.Max(_uso, rejilla); k < _pool.Count; k++)
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
        }

        private void DibujarRejilla()
        {
            for (int y = 0; y < Mapa.Alto; y++)
            {
                for (int x = 0; x < Mapa.Ancho; x++)
                {
                    var sr = Obtener();
                    sr.transform.position = PosMundo(x, y);
                    sr.sprite = spriteTile != null ? spriteTile : ObtenerSpriteBlanco();
                    sr.color = ((x + y) % 2 == 0) ? colorTileClaro : colorTileOscuro;
                    sr.sortingOrder = 0;
                    sr.transform.localScale = Vector3.one * tamanoCasilla;
                    sr.gameObject.SetActive(true);
                    _uso++;
                }
            }
        }

        private void DibujarEdificio(Edificio e, Color equipo)
        {
            float alfa = e.Estado == EstadoEdificio.EnConstruccion ? 0.55f : 1f;
            Dibujar(e.PosicionX, e.PosicionY,
                SpriteDe(spritesEdificio, (int)e.Tipo, spriteItem),
                Color.Lerp(equipo, ColoresEdificio[(int)e.Tipo], 0.45f), 1f, alfa);
        }

        private void DibujarUnidad(Unidad u, Color equipo)
        {
            Color baseC = ColoresUnidad[(int)u.Tipo];
            Color c = Color.Lerp(baseC, equipo, 0.35f);

            // [Fluidez] Interpola entre latidos del Modelo (1 celda/100 ms = 10
            // celdas/s): el sprite se desliza en vez de teletransportarse.
            Vector3 meta = PosMundo(u.PosicionX, u.PosicionY);
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

            Dibujar(pos, SpriteDe(spritesUnidad, (int)u.Tipo, spriteItem), c, 1.15f, u.EstaViva ? 1f : 0.3f);
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
