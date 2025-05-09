// VaoHelper.cs

using MazeEngine.Blocks;
using MazeEngine.Graphics;
using OpenTK.Mathematics;
using System;

namespace MazeEngine.Utils
{
    internal static class VaoHelper
    {
        // 6 faces * 4 vértices = 24
        private static readonly Vector3[] FacePositions =
        {
            // Left
            new Vector3(-0.5f, +0.5f, -0.5f), new Vector3(-0.5f, +0.5f, +0.5f),
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, +0.5f),

            // Right
            new Vector3(+0.5f, +0.5f, +0.5f), new Vector3(+0.5f, +0.5f, -0.5f),
            new Vector3(+0.5f, -0.5f, +0.5f), new Vector3(+0.5f, -0.5f, -0.5f),

            // Top
            new Vector3(-0.5f, +0.5f, -0.5f), new Vector3(+0.5f, +0.5f, -0.5f),
            new Vector3(-0.5f, +0.5f, +0.5f), new Vector3(+0.5f, +0.5f, +0.5f),

            // Bottom
            new Vector3(-0.5f, -0.5f, +0.5f), new Vector3(+0.5f, -0.5f, +0.5f),
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(+0.5f, -0.5f, -0.5f),

            // Back
            new Vector3(+0.5f, +0.5f, -0.5f), new Vector3(-0.5f, +0.5f, -0.5f),
            new Vector3(+0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f),

            // Front
            new Vector3(-0.5f, +0.5f, +0.5f), new Vector3(+0.5f, +0.5f, +0.5f),
            new Vector3(-0.5f, -0.5f, +0.5f), new Vector3(+0.5f, -0.5f, +0.5f),
        };

        // 4 vértices => 2 triângulos => 6 índices
        private static readonly uint[] FaceIndices = { 2, 1, 0, 2, 3, 1 };

        // As UVs (2D) são convertidas em (x,y,z) no VAO (z = camada).
        private static readonly Vector2[] FaceTexCoords =
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1),
        };

        public static void AddBlockToVao(World world, Vector3i blockPos, int x, int y, int z, uint blockId, VertexArrayObject vao)
        {
            if (blockId == 0) return;

            var blockType = (BlockType)blockId;

            foreach (var face in BlockFaceHelper.Faces)
            {
                var faceNormal = face.GetNormali();
                // Se ao lado desse bloco não há bloco, desenha a face
                if (world.GetBlock(blockPos + faceNormal) == 0)
                {
                    AddFaceToVao(blockType, x, y, z, face, vao);
                }
            }
        }

        private static void AddFaceToVao(BlockType blockType, int x, int y, int z, BlockFace face, VertexArrayObject vao)
        {
            var faceId = (int)face;

            var indexOffset = (uint)vao.VertexCount;

            // 4 vértices
            for (var i = 0; i < 4; i++)
            {
                Vector3 pos = FacePositions[faceId * 4 + i];
                pos += new Vector3(x, y, z);

                // Obter a camada da textura baseada no tipo de bloco e na face
                int layer = BlockTexturesManager.GetLayer(blockType, face);

                // Converter UV 2D em um Vector3: (u, v, camada)
                var uv = new Vector3(FaceTexCoords[i].X, FaceTexCoords[i].Y, layer);

                vao.Add(pos, uv);
            }

            // Índices
            var newIndices = new uint[FaceIndices.Length];
            for (var i = 0; i < FaceIndices.Length; i++)
            {
                newIndices[i] = FaceIndices[i] + indexOffset;
            }

            vao.AddIndices(newIndices);
        }
    }
}
