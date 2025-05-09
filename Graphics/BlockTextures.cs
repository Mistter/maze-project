// BlockTextures.cs

using System;
using System.Collections.Generic;
using MazeEngine.Blocks;
using MazeEngine.Utils;

namespace MazeEngine.Graphics
{
    internal static class BlockTextures
    {
        public static readonly Dictionary<BlockType, Dictionary<BlockFace, int>> TextureLayers = new Dictionary<BlockType, Dictionary<BlockFace, int>>();

        public static int TotalLayers { get; private set; } = 0;

        public static void Initialize()
        {
            foreach (BlockType block in Enum.GetValues(typeof(BlockType)))
            {
                if (block == BlockType.Air) continue;

                TextureLayers[block] = new Dictionary<BlockFace, int>();

                foreach (BlockFace face in BlockFaceHelper.Faces)
                {
                    TextureLayers[block][face] = TotalLayers++;
                }
            }
        }

        public static int GetLayer(BlockType block, BlockFace face)
        {
            if (TextureLayers.TryGetValue(block, out var faceDict))
            {
                if (faceDict.TryGetValue(face, out int layer))
                {
                    return layer;
                }
            }

            return 0; // Camada padrão
        }
    }
}
