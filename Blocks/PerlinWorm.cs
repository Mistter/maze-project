using MazeEngine.Utils;
using OpenTK.Mathematics;
using System.Collections.Generic;
using Vector3i = MazeEngine.Utils.Vector3i;

namespace MazeEngine.Blocks
{
    internal class PerlinWorm
    {
        private readonly PerlinNoise _noise;
        private readonly World _world;
        private readonly int _wormLength;
        private readonly float _wormStepSize;
        private readonly float _wormRadius;

        public PerlinWorm(World world, int seed, int wormLength, float wormStepSize, float wormRadius)
        {
            _world = world;
            _wormLength = wormLength;
            _wormStepSize = wormStepSize;
            _wormRadius = wormRadius;

            // Usamos o Perlin Noise para guiar o movimento do verme
            _noise = new PerlinNoise(seed, octaves: 4, persistence: 0.5f, frequency: 0.1f, amplitude: 1.0f);
        }

        public void Generate(Vector3i startPosition)
        {
            var currentPosition = startPosition.ToVector3();
            var direction = new Vector3(1, 0, 0); // Direção inicial do verme

            for (int i = 0; i < _wormLength; i++)
            {
                // Gera um túnel na posição atual
                GenerateTunnel(currentPosition);

                // Atualiza a direção com base no Perlin Noise
                float angleX = _noise.GetNoise(currentPosition.X, currentPosition.Z) * 2 * MathHelper.Pi;
                float angleY = _noise.GetNoise(currentPosition.Y, currentPosition.Z) * 2 * MathHelper.Pi;

                direction.X = MathF.Cos(angleX);
                direction.Y = MathF.Sin(angleY);
                direction.Z = MathF.Sin(angleX);

                direction.NormalizeFast();

                // Move o verme para a próxima posição
                currentPosition += direction * _wormStepSize;
            }
        }

        private void GenerateTunnel(Vector3 position)
        {
            var center = position.ToVector3i();
            var radius = (int)MathF.Ceiling(_wormRadius);

            // Gera um cilindro ao redor da posição atual
            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    for (int z = -radius; z <= radius; z++)
                    {
                        var blockPos = center + new Vector3i(x, y, z);

                        // Verifica se o bloco está dentro do raio do túnel
                        if (Vector3.DistanceSquared(blockPos.ToVector3(), position) <= _wormRadius * _wormRadius)
                        {
                            // Remove o bloco apenas se for um bloco sólido (pedra, terra, etc.)
                            var blockId = _world.GetBlock(blockPos);
                            if (blockId == 1 || blockId == 3) // 1 = Pedra, 3 = Terra
                            {
                                _world.SetBlock(blockPos, 0); // Remove o bloco (cria o túnel)
                            }
                        }
                    }
                }
            }
        }
    }
}