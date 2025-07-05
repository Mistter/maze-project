using System;
using OpenTK.Mathematics;

namespace MazeEngine.Utils
{
    /// <summary>
    /// Gera um caminho 3D baseado em Perlin noise para túneis ramificados.
    /// </summary>
    internal class PerlinWorm
    {
        private readonly PerlinNoise _noise;
        private readonly Random _rand;
        private Vector3 _position;
        private Vector3 _direction;

        public int WormLength { get; }
        public int WormStepSize { get; }
        public int WormRadius { get; }

        /// <summary>
        /// Cria um PerlinWorm.
        /// </summary>
        public PerlinWorm(PerlinNoise noiseSource, int seed, int wormLength, int wormStepSize, int wormRadius)
        {
            _noise = noiseSource;
            _rand = new Random(seed);
            WormLength = wormLength;
            WormStepSize = wormStepSize;
            WormRadius = wormRadius;
        }

        /// <summary>
        /// Define posição inicial do worm.
        /// </summary>
        public void SetPosition(Vector3 pos)
        {
            _position = pos;
            // direção inicial aleatória
            _direction = new Vector3(
                (float)(_rand.NextDouble() * 2 - 1),
                (float)(_rand.NextDouble() * 2 - 1),
                (float)(_rand.NextDouble() * 2 - 1)
            ).Normalized();
        }

        /// <summary>
        /// Avança um passo e retorna posição do bloco em Vector3i.
        /// </summary>
        public Vector3i Step()
        {
            // calcula nova direção via ruído 3D
            float dx = _noise.GetNoise(_position.X, _position.Z);
            float dy = _noise.GetNoise(_position.Y, _position.X);
            float dz = _noise.GetNoise(_position.Z, _position.Y);
            var nDir = new Vector3(dx, dy, dz);
            _direction = Vector3.Lerp(_direction, nDir, 0.2f).Normalized();

            // avança
            _position += _direction * WormStepSize;
            // retorna posição inteira para indexação de blocos
            return new Vector3i(
                (int)Math.Floor(_position.X),
                (int)Math.Floor(_position.Y),
                (int)Math.Floor(_position.Z)
            );
        }
    }
}
