namespace MazeEngine.Utils
{
    public class PerlinNoise
    {
        private readonly int[] _permutation;
        private readonly int[] _p; // Permutation table
        private readonly int _octaves;
        private readonly float _persistence;
        private readonly float _frequency;
        private readonly float _amplitude;

        public PerlinNoise(int seed, int octaves = 4, float persistence = 0.5f, float frequency = 1.0f, float amplitude = 1.0f)
        {
            _octaves = octaves;
            _persistence = persistence;
            _frequency = frequency;
            _amplitude = amplitude;

            _permutation = new int[256];
            var random = new Random(seed);
            for (int i = 0; i < 256; i++)
                _permutation[i] = random.Next(256);

            // Duplicate the permutation table to avoid overflow
            _p = new int[512];
            for (int i = 0; i < 512; i++)
                _p[i] = _permutation[i % 256];
        }

        public float GetNoise(float x, float z)
        {
            float total = 0;
            float freq = _frequency;
            float amp = _amplitude;
            float maxValue = 0; // For normalization

            for (int i = 0; i < _octaves; i++)
            {
                total += Noise(x * freq, z * freq) * amp;

                maxValue += amp;

                amp *= _persistence;
                freq *= 2;
            }

            return total / maxValue;
        }

        private float Noise(float x, float z)
        {
            int xi = (int)Math.Floor(x) & 255;
            int zi = (int)Math.Floor(z) & 255;

            float xf = x - (int)Math.Floor(x);
            float zf = z - (int)Math.Floor(z);

            float u = Fade(xf);
            float v = Fade(zf);

            int aa, ab, ba, bb;
            aa = _p[_p[xi] + zi];
            ab = _p[_p[xi] + zi + 1];
            ba = _p[_p[xi + 1] + zi];
            bb = _p[_p[xi + 1] + zi + 1];

            float x1, x2;
            x1 = Lerp(Grad(aa, xf, zf), Grad(ba, xf - 1, zf), u);
            x2 = Lerp(Grad(ab, xf, zf - 1), Grad(bb, xf - 1, zf - 1), u);

            return Lerp(x1, x2, v);
        }

        private float Fade(float t)
        {
            // 6t^5 - 15t^4 + 10t^3
            return t * t * t * (t * (t * 6 - 15) + 10);
        }

        private float Lerp(float a, float b, float t)
        {
            return a + t * (b - a);
        }

        private float Grad(int hash, float x, float z)
        {
            int h = hash & 7; // Convert low 3 bits of hash code
            float u = h < 4 ? x : z;
            float v = h < 4 ? z : x;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        public float GetNoise3D(float x, float y, float z)
        {
            // Escala pré-aplicada antes de chamar
            int xi = (int)Math.Floor(x) & 255;
            int yi = (int)Math.Floor(y) & 255;
            int zi = (int)Math.Floor(z) & 255;

            float xf = x - (int)Math.Floor(x);
            float yf = y - (int)Math.Floor(y);
            float zf = z - (int)Math.Floor(z);

            float u = Fade(xf);
            float v = Fade(yf);
            float w = Fade(zf);

            int aaa = _p[_p[_p[xi] + yi] + zi];
            int aba = _p[_p[_p[xi] + yi + 1] + zi];
            int aab = _p[_p[_p[xi] + yi] + zi + 1];
            int abb = _p[_p[_p[xi] + yi + 1] + zi + 1];
            int baa = _p[_p[_p[xi + 1] + yi] + zi];
            int bba = _p[_p[_p[xi + 1] + yi + 1] + zi];
            int bab = _p[_p[_p[xi + 1] + yi] + zi + 1];
            int bbb = _p[_p[_p[xi + 1] + yi + 1] + zi + 1];

            float x1 = Lerp(Grad(aaa, xf, yf, zf),
                            Grad(baa, xf - 1, yf, zf), u);
            float x2 = Lerp(Grad(aba, xf, yf - 1, zf),
                            Grad(bba, xf - 1, yf - 1, zf), u);
            float y1 = Lerp(x1, x2, v);

            float x3 = Lerp(Grad(aab, xf, yf, zf - 1),
                            Grad(bab, xf - 1, yf, zf - 1), u);
            float x4 = Lerp(Grad(abb, xf, yf - 1, zf - 1),
                            Grad(bbb, xf - 1, yf - 1, zf - 1), u);
            float y2 = Lerp(x3, x4, v);

            return Lerp(y1, y2, w);
        }

        private float Grad(int hash, float x, float y, float z)
        {
            // 16 direções principais
            int h = hash & 15;
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : h == 12 || h == 14 ? x : z;
            return ((h & 1) == 0 ? u : -u)
                 + ((h & 2) == 0 ? v : -v);
        }
    }
}