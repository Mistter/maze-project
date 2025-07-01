using System;
using System.Collections.Generic;
using MazeEngine.Blocks;
using MazeEngine.Graphics;
using MazeEngine.Utils;
using OpenTK.Mathematics;

namespace MazeEngine.Utils
{
    internal static class GlobalMeshGenerator
    {
        public static VertexArrayObject BuildMesh(World world)
        {
            var vao = new VertexArrayObject();
            var loaded = world.loadedChunks;

            int minCX = int.MaxValue, minCY = int.MaxValue, minCZ = int.MaxValue;
            int maxCX = int.MinValue, maxCY = int.MinValue, maxCZ = int.MinValue;
            foreach (var c in loaded.Keys)
            {
                minCX = Math.Min(minCX, c.X);
                minCY = Math.Min(minCY, c.Y);
                minCZ = Math.Min(minCZ, c.Z);
                maxCX = Math.Max(maxCX, c.X);
                maxCY = Math.Max(maxCY, c.Y);
                maxCZ = Math.Max(maxCZ, c.Z);
            }

            for (int cx = minCX; cx <= maxCX; cx++)
                for (int cy = minCY; cy <= maxCY; cy++)
                    for (int cz = minCZ; cz <= maxCZ; cz++)
                    {
                        var key = new Vector3i(cx, cy, cz);
                        if (!loaded.TryGetValue(key, out var chunk)) continue;

                        int baseX = cx * Chunk.Size;
                        int baseY = cy * Chunk.Size;
                        int baseZ = cz * Chunk.Size;

                        for (int lx = 0; lx < Chunk.Size; lx++)
                            for (int ly = 0; ly < Chunk.Size; ly++)
                                for (int lz = 0; lz < Chunk.Size; lz++)
                                {
                                    uint id = chunk.GetBlock(lx, ly, lz);
                                    if (id == 0) continue;

                                    var worldPos = new Vector3i(baseX + lx, baseY + ly, baseZ + lz);
                                    var blockType = (BlockType)id;

                                    foreach (var face in BlockFaceHelper.Faces)
                                    {
                                        var n = face.GetNormali();
                                        uint nid = world.GetBlock(worldPos + n);
                                        if (nid != 0) continue;
                                        AddFace(vao, blockType, face, worldPos);
                                    }
                                }
                    }

            return vao;
        }

        private static void AddFace(
            VertexArrayObject vao,
            BlockType blockType,
            BlockFace face,
            Vector3i worldPos)
        {
            int faceId = (int)face;
            uint offset = (uint)vao.VertexCount;

            for (int i = 0; i < 4; i++)
            {
                var local = VaoHelper.FacePositions[faceId * 4 + i];
                var pos = local + worldPos.ToVector3();
                int layer = BlockTexturesManager.GetLayer(blockType, face);
                vao.Add(pos, new Vector3(
                    VaoHelper.FaceTexCoords[i].X,
                    VaoHelper.FaceTexCoords[i].Y,
                    layer));
            }

            var idx = new uint[VaoHelper.FaceIndices.Length];
            for (int i = 0; i < idx.Length; i++)
                idx[i] = VaoHelper.FaceIndices[i] + offset;
            vao.AddIndices(idx);
        }
    }
}
