using System;
using UnityEngine;

namespace Vista
{
    // SONIDO procedural (sin assets: cero licencias y cero descargas).
    // Efectos generados por código (clic, error, golpe, construir, entrenar,
    // caza, victoria, derrota) + música ambiental en bucle, todo con ondas
    // seno y ruido. La Vista lo llama con métodos estáticos (nulos si la
    // escena aún no lo creó); S o el botón del HUD silencian todo.
    public class SonidoJuego : MonoBehaviour
    {
        private const int Frecuencia = 22050;

        private static SonidoJuego _yo;
        public static bool Silenciado { get; private set; }

        private AudioClip _clic, _error, _golpe, _construir, _entrenar, _caza, _victoria, _derrota;
        private readonly AudioSource[] _voces = new AudioSource[6];
        private int _voz;
        private float _ultimoGolpe;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCrear()
        {
            if (FindAnyObjectByType<SonidoJuego>() != null) return;
            var go = new GameObject("SonidoJuego");
            go.AddComponent<SonidoJuego>();
        }

        private void Awake()
        {
            if (_yo != null && _yo != this)
            {
                Destroy(gameObject);
                return;
            }
            _yo = this;
            DontDestroyOnLoad(gameObject);

            _clic = Secuencia(new Nota[] { new Nota(880f, 0.07f, 0.5f) });
            _error = Secuencia(new Nota[] { new Nota(150f, 0.22f, 0.5f), new Nota(110f, 0.18f, 0.4f) });
            _golpe = CrearGolpe();
            _construir = Secuencia(new Nota[] { new Nota(440f, 0.10f, 0.5f), new Nota(660f, 0.12f, 0.5f) });
            _entrenar = Secuencia(new Nota[] { new Nota(330f, 0.10f, 0.5f), new Nota(495f, 0.12f, 0.5f) });
            _caza = Secuencia(new Nota[] { new Nota(523f, 0.07f, 0.5f), new Nota(659f, 0.07f, 0.5f), new Nota(784f, 0.10f, 0.5f) });
            _victoria = Secuencia(new Nota[]
            {
                new Nota(523f, 0.15f, 0.5f), new Nota(659f, 0.15f, 0.5f),
                new Nota(784f, 0.15f, 0.5f), new Nota(1046f, 0.30f, 0.5f)
            });
            _derrota = Secuencia(new Nota[]
            {
                new Nota(392f, 0.20f, 0.5f), new Nota(330f, 0.20f, 0.5f),
                new Nota(262f, 0.20f, 0.5f), new Nota(196f, 0.35f, 0.5f)
            });

            for (int i = 0; i < _voces.Length; i++)
            {
                var v = gameObject.AddComponent<AudioSource>();
                v.volume = 0.6f;
                v.playOnAwake = false;
                _voces[i] = v;
            }

            var musica = gameObject.AddComponent<AudioSource>();
            musica.clip = MusicaAmbiente();
            musica.loop = true;
            musica.volume = 0.30f;
            musica.playOnAwake = false;
            musica.Play();
            AudioListener.pause = Silenciado;
        }

        public static void AlternarMudez()
        {
            Silenciado = !Silenciado;
            AudioListener.pause = Silenciado;
        }

        public static void Clic() => Tocar(_yo != null ? _yo._clic : null, 1f);
        public static void Error() => Tocar(_yo != null ? _yo._error : null, 1f);
        public static void Construir() => Tocar(_yo != null ? _yo._construir : null, 1f);
        public static void Entrenar() => Tocar(_yo != null ? _yo._entrenar : null, 1f);
        public static void Caza() => Tocar(_yo != null ? _yo._caza : null, 1f);
        public static void Victoria() => Tocar(_yo != null ? _yo._victoria : null, 1f);
        public static void Derrota() => Tocar(_yo != null ? _yo._derrota : null, 1f);

        // El golpe suena MUCHO en batalla: como máximo uno cada 0.3 s.
        public static void Golpe()
        {
            if (_yo == null || _yo._golpe == null) return;
            if (Time.time - _yo._ultimoGolpe < 0.3f) return;
            _yo._ultimoGolpe = Time.time;
            Tocar(_yo._golpe, 0.8f);
        }

        private static void Tocar(AudioClip clip, float escala)
        {
            if (_yo == null || clip == null) return;
            AudioSource v = _yo._voces[_yo._voz % _yo._voces.Length];
            _yo._voz++;
            v.PlayOneShot(clip, escala);
        }

        // Una nota = seno con ataque instantáneo y caída exponencial.
        private struct Nota
        {
            public float Frec, Dur, Ganancia;
            public Nota(float frec, float dur, float ganancia)
            {
                Frec = frec; Dur = dur; Ganancia = ganancia;
            }
        }

        private static AudioClip Secuencia(Nota[] notas)
        {
            int total = 0;
            foreach (Nota n in notas) total += (int)(n.Dur * Frecuencia);
            float[] datos = new float[total];
            int pos = 0;
            foreach (Nota n in notas)
            {
                int len = (int)(n.Dur * Frecuencia);
                for (int i = 0; i < len; i++)
                {
                    float t = (float)i / Frecuencia;
                    float env = (float)Math.Exp(-4.0 * i / len);
                    datos[pos + i] = n.Ganancia * env * (float)Math.Sin(2.0 * Math.PI * n.Frec * t);
                }
                pos += len;
            }
            AudioClip clip = AudioClip.Create("sfx", total, 1, Frecuencia, false);
            clip.SetData(datos, 0);
            return clip;
        }

        // Golpe: ruido blanco con caída + retumbe grave.
        private static AudioClip CrearGolpe()
        {
            int len = (int)(0.14f * Frecuencia);
            float[] datos = new float[len];
            System.Random azar = new System.Random();
            for (int i = 0; i < len; i++)
            {
                float t = (float)i / Frecuencia;
                float env = (float)Math.Exp(-9.0 * i / len);
                float ruido = (float)(azar.NextDouble() * 2.0 - 1.0) * 0.45f;
                float grave = 0.35f * (float)Math.Sin(2.0 * Math.PI * 90.0 * t);
                datos[i] = env * (ruido + grave);
            }
            AudioClip clip = AudioClip.Create("golpe", len, 1, Frecuencia, false);
            clip.SetData(datos, 0);
            return clip;
        }

        // Música ambiental (8 s en bucle): colchón de acordes Am-F-C-G con
        // fundidos + punteo pentatónico esparso, todo bajito.
        private static AudioClip MusicaAmbiente()
        {
            float[][] acordes = new float[][]
            {
                new float[] { 110f, 164.8f, 220f, 261.6f },
                new float[] { 87.3f, 130.8f, 174.6f, 220f },
                new float[] { 98f, 130.8f, 164.8f, 196f },
                new float[] { 98f, 146.8f, 196f, 246.9f }
            };
            // Punteo: (segundo, nota). Pentatónica de La, esparsa y tranquila.
            float[,] punteo = new float[,]
            {
                { 0.5f, 440f }, { 2.5f, 523.3f }, { 4.5f, 659.3f },
                { 5.5f, 587.3f }, { 6.5f, 523.3f }
            };
            int total = 8 * Frecuencia;
            float[] datos = new float[total];
            for (int a = 0; a < 4; a++)
            {
                int ini = a * 2 * Frecuencia;
                int len = 2 * Frecuencia;
                foreach (float f in acordes[a])
                {
                    for (int i = 0; i < len; i++)
                    {
                        float t = (float)i / Frecuencia;
                        // Fundido raised-cosine de 0.4 s en cada borde.
                        float b = Math.Min(i, len - i) / (0.4f * Frecuencia);
                        if (b > 1f) b = 1f;
                        float env = 0.5f - 0.5f * (float)Math.Cos(Math.PI * b);
                        datos[ini + i] += 0.075f * env * (float)Math.Sin(2.0 * Math.PI * f * t);
                    }
                }
            }
            for (int p = 0; p < punteo.GetLength(0); p++)
            {
                int ini = (int)(punteo[p, 0] * Frecuencia);
                int len = (int)(0.9f * Frecuencia);
                for (int i = 0; i < len && ini + i < total; i++)
                {
                    float t = (float)i / Frecuencia;
                    float env = (float)Math.Exp(-5.0 * i / len);
                    datos[ini + i] += 0.11f * env * (float)Math.Sin(2.0 * Math.PI * punteo[p, 1] * t);
                }
            }
            AudioClip clip = AudioClip.Create("musica", total, 1, Frecuencia, false);
            clip.SetData(datos, 0);
            return clip;
        }
    }
}
