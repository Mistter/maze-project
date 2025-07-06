using OpenTK.Mathematics;

namespace MazeEngine.Utils
{
    public class PerlinWorm
    {
        private readonly PerlinNoise _noise;
        private readonly Random _rand;
        private readonly int _length;
        private readonly float _stepSize;
        private readonly int _radius;
        private Vector3 _position;

        // Ajuste esta escala para controlar a “suavidade” da curva
        private const float NoiseScale = 0.01f;

        public int WormLength => _length;
        public float WormStepSize => _stepSize;
        public int WormRadius => _radius;

        public PerlinWorm(PerlinNoise noise, int seed, int wormLength, int wormStepSize, int wormRadius)
        {
            _noise = noise;
            _rand = new Random(seed);
            _length = wormLength;
            _stepSize = wormStepSize;
            _radius = wormRadius;
            _position = Vector3.Zero;
        }

        /// <summary>
        /// Define a posição inicial do worm.
        /// </summary>
        public void SetPosition(Vector3 pos)
        {
            _position = pos;
        }

        /// <summary>
        /// Avança o worm um passo, retornando a nova posição.
        /// </summary>
        public Vector3 Step()
        {
            // Escala usada para amostrar o ruído suavemente
            const float NoiseScale = 0.01f;

            // Converte a posição atual para coordenadas de ruído
            float nx = _position.X * NoiseScale;
            float ny = _position.Y * NoiseScale;
            float nz = _position.Z * NoiseScale;

            // Calcula a direção a partir de três amostras de ruído 3D
            float dx = _noise.GetNoise3D(nx + 1000, ny, nz);
            float dy = _noise.GetNoise3D(nx, ny + 1000, nz);
            float dz = _noise.GetNoise3D(nx, ny, nz + 1000);

            var dir = new Vector3(dx, dy, dz);

            // Se o vetor for muito pequeno, gera uma direção aleatória
            if (dir.LengthFast < 1e-6f)
                dir = new Vector3(
                    (float)_rand.NextDouble() - 0.5f,
                    (float)_rand.NextDouble() - 0.5f,
                    (float)_rand.NextDouble() - 0.5f
                );

            dir.NormalizeFast();
            _position += dir * _stepSize;
            return _position;
        }
    }
}
