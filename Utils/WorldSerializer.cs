using MazeEngine.Blocks;

namespace MazeEngine.Utils
{
    internal static class WorldSerializer
    {
        public const string WorldFolder = "world";
        public const string ChunksFolder = "chunks"; // pode trocar de Regions para Chunks

        // Salva um chunk individual
        public static void SaveChunk(World world, Vector3i chunkPos)
        {
            var dir = Path.Combine(WorldFolder, ChunksFolder);
            Directory.CreateDirectory(dir);

            string temp = Path.Combine(dir, $"{chunkPos.X} {chunkPos.Y} {chunkPos.Z}.tmp");
            string dest = Path.Combine(dir, $"{chunkPos.X} {chunkPos.Y} {chunkPos.Z}");

            // 1) Grava em .tmp
            using (var writer = new BinaryWriter(File.Create(temp)))
            {
                if (!world.loadedChunks.TryGetValue(chunkPos, out var chunk))
                    return;
                chunk.Write(writer);
            }

            // 2) Substitui o antigo
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(temp, dest);
        }

        // Carrega um chunk individual
        public static bool LoadChunk(ChunkCache cache, Vector3i chunkPos)
        {
            var file = new FileInfo(Path.Combine(WorldFolder, ChunksFolder, GetChunkFileName(chunkPos)));
            if (!file.Exists) return false;

            using (var reader = new BinaryReader(file.OpenRead()))
            {
                var chunk = new CachedChunk(cache.World, chunkPos, reader);
                cache.AddChunk(chunk);
            }

            return true;
        }

        public static bool SafeLoadChunk(ChunkCache cache, Vector3i pos)
        {
            try
            {
                return LoadChunk(cache, pos);
            }
            catch (EndOfStreamException)
            {
                // joga fora e regenera
                var file = Path.Combine(WorldFolder, ChunksFolder, $"{pos.X} {pos.Y} {pos.Z}");
                if (File.Exists(file)) File.Delete(file);
                return false;
            }
        }

        private static string GetChunkFileName(Vector3i chunkPos) => $"{chunkPos.X} {chunkPos.Y} {chunkPos.Z}";
    }
}
