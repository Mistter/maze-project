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
    }
}