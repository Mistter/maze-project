// BlockTexturesManager.cs

using MazeEngine.Blocks;
using MazeEngine.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace MazeEngine.Graphics
{
    internal static class BlockTexturesManager
    {
        public static TextureArray TextureArray { get; private set; }
        private static Dictionary<(BlockType, BlockFace), int> _textureLayers = new Dictionary<(BlockType, BlockFace), int>();
        private static Dictionary<BlockType, string> _genericTextures = new Dictionary<BlockType, string>();

        public static void Initialize()
        {
            var blockTypes = Enum.GetValues(typeof(BlockType))
                                 .Cast<BlockType>()
                                 .Where(bt => bt != BlockType.Air)
                                 .ToList();

            int totalLayers = blockTypes.Count * Enum.GetValues(typeof(BlockFace)).Length;
            TextureArray = new TextureArray(16, 16, totalLayers);

            int layer = 0;
            foreach (var block in blockTypes)
            {
                // Tenta carregar a textura genérica {block}.png uma vez
                string genericFilename = $"{block.ToString().ToLower()}.png";
                string genericFilepath = Path.Combine("Textures", "Blocks", genericFilename);
                bool genericExists = File.Exists(genericFilepath);
                if (genericExists)
                {
                    _genericTextures[block] = genericFilepath;
                }

                foreach (BlockFace face in Enum.GetValues(typeof(BlockFace)))
                {
                    string specificFilename = $"{block.ToString().ToLower()}_{face.ToString().ToLower()}.png";
                    string specificFilepath = Path.Combine("Textures", "Blocks", specificFilename);

                    string finalPath = specificFilepath;

                    if (!File.Exists(specificFilepath))
                    {
                        // Se a textura específica não existir, tenta a genérica
                        if (_genericTextures.TryGetValue(block, out string genericPath))
                        {
                            finalPath = genericPath;
                        }
                        else
                        {
                            // Se a genérica também não existir, usa default.png
                            string defaultPath = Path.Combine("Textures", "Blocks", "default.png");
                            if (File.Exists(defaultPath))
                            {
                                finalPath = defaultPath;
                            }
                            else
                            {
                                Console.WriteLine($"Erro: Arquivo de textura padrão não encontrado: {defaultPath}");
                                throw new FileNotFoundException($"Texture not found for block {block} and face {face}, and default texture is missing.");
                            }
                        }
                    }

                    try
                    {
                        TextureArray.SetTexture(layer, new TextureData(finalPath));
                        _textureLayers[(block, face)] = layer;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro ao carregar textura '{finalPath}': {ex.Message}");
                        // Tenta carregar a textura padrão se ocorrer algum erro
                        string defaultPathFallback = Path.Combine("Textures", "Blocks", "default.png");
                        if (File.Exists(defaultPathFallback))
                        {
                            try
                            {
                                TextureArray.SetTexture(layer, new TextureData(defaultPathFallback));
                                _textureLayers[(block, face)] = layer;
                            }
                            catch (Exception fallbackEx)
                            {
                                Console.WriteLine($"Erro ao carregar textura padrão '{defaultPathFallback}': {fallbackEx.Message}");
                                throw; // Re-lança a exceção após falhar no fallback
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Erro: Arquivo de textura padrão não encontrado: {defaultPathFallback}");
                            throw new FileNotFoundException($"Default texture not found while handling texture loading errors for block {block} and face {face}.");
                        }
                    }

                    layer++;
                }
            }

            TextureArray.GenerateMipmaps();
        }

        public static int GetLayer(BlockType block, BlockFace face)
        {
            if (_textureLayers.TryGetValue((block, face), out int layer))
                return layer;
            return 0; // camada padrão (default.png)
        }
    }
}
