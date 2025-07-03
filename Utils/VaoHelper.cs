using MazeEngine.Blocks;
using MazeEngine.Graphics;
using MazeEngine.Utils;
using OpenTK.Mathematics;

namespace MazeEngine.Utils
{
    internal static class VaoHelper
    {
        // 6 faces * 4 vértices = 24 posições
        public static readonly Vector3[] FacePositions =
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

        public static readonly uint[] FaceIndices = { 2, 1, 0, 2, 3, 1 };
        public static readonly Vector2[] FaceTexCoords =
        {
            new Vector2(0,0), new Vector2(1,0),
            new Vector2(0,1), new Vector2(1,1)
        };

        public static void AddBlockToVao(
            World world,
            Vector3i blockPos,
            int x, int y, int z,
            uint blockId,
            VertexArrayObject vao)
        {
            if (blockId == 0) return;
            var blockType = (BlockType)blockId;

            foreach (var face in BlockFaceHelper.Faces)
            {
                var normal = face.GetNormali();
                var neighborPos = blockPos + normal;
                var neighborChunk = World.ChunkInWorld(neighborPos);
                bool chunkLoaded = world.loadedChunks.ContainsKey(neighborChunk);
                uint neighborBlock = world.GetBlock(neighborPos);
                // world.GetBlock returns 0 if chunk missing or block is air

                if (face == BlockFace.Bottom)
                {
                    // only draw bottom when the chunk below is loaded AND it's air there
                    if (chunkLoaded && neighborBlock == 0)
                        AddFace(blockType, x, y, z, face, vao);
                }
                else
                {
                    // for sides & top: draw if neighbor is air OR chunk isn't loaded (no seam)
                    if (neighborBlock == 0)
                        AddFace(blockType, x, y, z, face, vao);
                }
            }
        }

        private static void AddFace(
            BlockType blockType,
            int x, int y, int z,
            BlockFace face,
            VertexArrayObject vao)
        {
            int faceId = (int)face;
            uint offset = (uint)vao.VertexCount;

            for (int i = 0; i < 4; i++)
            {
                var v = FacePositions[faceId * 4 + i] + new Vector3(x, y, z);
                int layer = BlockTexturesManager.GetLayer(blockType, face);
                vao.Add(v, new Vector3(FaceTexCoords[i].X, FaceTexCoords[i].Y, layer));
            }

            var idx = new uint[FaceIndices.Length];
            for (int i = 0; i < idx.Length; i++)
                idx[i] = FaceIndices[i] + offset;
            vao.AddIndices(idx);
        }
    }
}