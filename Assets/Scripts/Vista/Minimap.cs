using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Modelo;

namespace Vista
{
    // Minimapa esquemático (solo Vista): puntitos de tropas/edificios/
    // recursos sobre el fondo de biomas + marco blanco de la cámara.
    // Clic para centrar la cámara ahí. No toca el Modelo: solo lee la foto.
    public class Minimap : MonoBehaviour
    {
        private GestorJuego _gestor;
        private RawImage _vista;
        private Texture2D _tex;
        private Color32[] _fondo;
        private float _proximoRefresco;

        private static readonly Color FondoPradera = new Color(0.13f, 0.30f, 0.12f);
        private static readonly Color FondoArena = new Color(0.55f, 0.48f, 0.28f);
        private static readonly Color FondoBosque = new Color(0.06f, 0.20f, 0.09f);
        private static readonly Color PuntoOro = new Color(1f, 0.85f, 0.2f);
        private static readonly Color PuntoLocal = new Color(0.35f, 0.65f, 1f);
        private static readonly Color PuntoEnemigo = new Color(1f, 0.35f, 0.3f);
        private static readonly Color PuntoCaza = new Color(0.5f, 1f, 0.5f);

        public void Inicializar(GestorJuego gestor, Transform canvas)
        {
            _gestor = gestor;
            int w = Mapa.Ancho, h = Mapa.Alto;
            _tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            _tex.filterMode = FilterMode.Point;

            var panel = UiFabrica.Panel(canvas, "PanelMinimap",
                new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0),
                new Vector2(164, 164), new Vector2(8, 8));
            UiFabrica.Fondo(panel.gameObject, new Color(0.10f, 0.14f, 0.12f, 0.95f));

            var go = new GameObject("Vista", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(panel.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(150, 150);
            rt.anchoredPosition = Vector2.zero;
            _vista = go.GetComponent<RawImage>();
            _vista.texture = _tex;
            _vista.raycastTarget = true;
            // El clic lo recibe el RawImage (él tiene el Graphic): reenvía aquí.
            var clic = go.AddComponent<MinimapClick>();
            clic.dueno = this;

            // Fondo inmutable: se calcula UNA vez (biomas + pesos).
            _fondo = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    _fondo[y * w + x] = FondoBioma(x, y);
        }

        public void CentrarCamara(Vector2 uv)
        {
            if (_gestor == null || _gestor.MenuInicio == null || _gestor.MenuInicio.Abierto) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 p = cam.transform.position;
            p.x = Mathf.Clamp(uv.x * Mapa.Ancho, -2f, Mapa.Ancho + 1f);
            p.y = Mathf.Clamp(uv.y * Mapa.Alto, -2f, Mapa.Alto + 1f);
            cam.transform.position = p;
        }

        private void Update()
        {
            if (_gestor == null || _tex == null) return;
            if (Time.unscaledTime < _proximoRefresco) return;
            _proximoRefresco = Time.unscaledTime + 0.25f;
            Redibujar();
        }

        private void Redibujar()
        {
            int w = Mapa.Ancho, h = Mapa.Alto;
            _tex.SetPixels32(_fondo);
            var foto = _gestor.UltimaFoto;
            if (foto != null)
            {
                foreach (Recurso r in foto.Recursos)
                    if (!r.EstaAgotado) Punto(r.PosicionX, r.PosicionY, PuntoOro);
                foreach (Unidad u in foto.UnidadesLocal)
                    if (u.EstaViva) Punto(u.PosicionX, u.PosicionY, PuntoLocal);
                foreach (Unidad u in foto.UnidadesEnemigo)
                    if (u.EstaViva)
                    {
                        string f = ArteRecursos.FaccionDeTropa(u, foto.EdificiosEnemigo);
                        Punto(u.PosicionX, u.PosicionY, f != null ? ArteRecursos.ColorFaccion(f) : PuntoEnemigo);
                    }
                if (foto.Animales != null)
                    foreach (Unidad c in foto.Animales)
                        if (c.EstaViva) Punto(c.PosicionX, c.PosicionY, PuntoCaza);
                foreach (Edificio e in foto.EdificiosLocal) Bloque(e, PuntoLocal);
                foreach (Edificio e in foto.EdificiosEnemigo)
                    Bloque(e, e != null && !string.IsNullOrEmpty(e.Faccion)
                        ? ArteRecursos.ColorFaccion(e.Faccion) : PuntoEnemigo);
            }
            MarcoCamara();
            _tex.Apply(false);
        }

        private static Color32 FondoBioma(int x, int y)
        {
            if (ArteRecursos.EsRio(x, y)) return new Color(0.2f, 0.45f, 0.7f);
            ArteRecursos.PesoBiomas(x, y, out float arena, out float bosque);
            Color c = FondoPradera;
            if (arena > 0f) c = Color.Lerp(c, FondoArena, arena);
            if (bosque > 0f) c = Color.Lerp(c, FondoBosque, bosque);
            return c;
        }

        private void Punto(int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= Mapa.Ancho || y >= Mapa.Alto) return;
            _tex.SetPixel(x, y, c);
        }

        private void Bloque(Edificio e, Color c)
        {
            for (int dx = 0; dx < e.Lado; dx++)
                for (int dy = 0; dy < e.Lado; dy++)
                    Punto(e.PosicionX + dx, e.PosicionY + dy, c);
        }

        private void MarcoCamara()
        {
            Camera cam = Camera.main;
            if (cam == null || !cam.orthographic) return;
            float mitadAlto = cam.orthographicSize;
            float mitadAncho = mitadAlto * Screen.width / Mathf.Max(1f, Screen.height);
            int x0 = Mathf.RoundToInt(cam.transform.position.x - mitadAncho);
            int x1 = Mathf.RoundToInt(cam.transform.position.x + mitadAncho);
            int y0 = Mathf.RoundToInt(cam.transform.position.y - mitadAlto);
            int y1 = Mathf.RoundToInt(cam.transform.position.y + mitadAlto);
            var blanco = Color.white;
            for (int x = x0; x <= x1; x++)
            {
                Punto(x, y0, blanco);
                Punto(x, y1, blanco);
            }
            for (int y = y0; y <= y1; y++)
            {
                Punto(x0, y, blanco);
                Punto(x1, y, blanco);
            }
        }
    }

    // Reenviador: el RawImage recibe el clic (tiene el Graphic) y lo pasa
    // al Minimap con coordenadas UV (0..1) de la imagen.
    public class MinimapClick : MonoBehaviour, IPointerDownHandler
    {
        public Minimap dueno;

        public void OnPointerDown(PointerEventData evento)
        {
            if (dueno == null) return;
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rt, evento.position, evento.pressEventCamera, out Vector2 local))
                return;
            float u = Mathf.Clamp01(local.x / rt.rect.width + 0.5f);
            float v = Mathf.Clamp01(local.y / rt.rect.height + 0.5f);
            dueno.CentrarCamara(new Vector2(u, v));
        }
    }
}
