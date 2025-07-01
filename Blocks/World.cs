// World.cs

using MazeEngine.Entities;
using MazeEngine.Utils;
using OpenTK.Mathematics;
using Vector3i = MazeEngine.Utils.Vector3i;

namespace MazeEngine.Blocks
{
    internal class World
    {
        public const int MaxChunkUploads = 1;
        public const int RegionSize = 2 * Chunk.Size; // 32 blocos
        public const int ChunksPerRegion = RegionSize / Chunk.Size; // 2 chunks por região

        public static readonly int MaxAsyncChunkUpdates = Environment.ProcessorCount * 4;

        public static Vector3i RegionInWorld(Vector3i v) => RegionInWorld(v.X, v.Y, v.Z);
        public static Vector3i RegionInWorld(int x, int y, int z) => new Vector3i(
            x < 0 ? (x + 1) / RegionSize - 1 : x / RegionSize,
            y < 0 ? (y + 1) / RegionSize - 1 : y / RegionSize,
            z < 0 ? (z + 1) / RegionSize - 1 : z / RegionSize
        );

        public static Vector3i ChunkInWorld(Vector3i v) => ChunkInWorld(v.X, v.Y, v.Z);
        public static Vector3i ChunkInWorld(int x, int y, int z) => new Vector3i(
            x < 0 ? (x + 1) / Chunk.Size - 1 : x / Chunk.Size,
            y < 0 ? (y + 1) / Chunk.Size - 1 : y / Chunk.Size,
            z < 0 ? (z + 1) / Chunk.Size - 1 : z / Chunk.Size
        );

        public static Vector3i BlockInChunk(int x, int y, int z) => new Vector3i(
            x < 0 ? (x + 1) % Chunk.Size + Chunk.Size - 1 : x % Chunk.Size,
            y < 0 ? (y + 1) % Chunk.Size + Chunk.Size - 1 : y % Chunk.Size,
            z < 0 ? (z + 1) % Chunk.Size + Chunk.Size - 1 : z % Chunk.Size
        );

        // Chunks realmente carregados na memória:
        public readonly Dictionary<Vector3i, Chunk> loadedChunks = new Dictionary<Vector3i, Chunk>();

        // Expondo apenas os valores para render
        public IEnumerable<Chunk> Chunks => loadedChunks.Values;

        public int ChunksQueuedCount => _queuedChunkUpdatesHp.Count + _queuedChunkUpdatesLp.Count;
        public int ChunksReadyCount => _queuedReadyToUploadHp.Count + _queuedReadyToUploadLp.Count;
        public int ChunksLoadedCount => loadedChunks.Count;
        public int ChunkThreadsCount => _chunkThreadsCount;

        // Alterado para HashSet para evitar duplicações e melhorar a performance
        private readonly HashSet<Vector3i> _loadedRegions = new HashSet<Vector3i>();

        // HashSets para evitar enfileiramento duplicado
        private readonly HashSet<Vector3i> _queuedChunksHpSet = new HashSet<Vector3i>();
        private readonly HashSet<Vector3i> _queuedChunksLpSet = new HashSet<Vector3i>();

        // Controle de regiões já geradas
        private readonly HashSet<Vector3i> _generatedRegions = new HashSet<Vector3i>();

        private readonly Queue<Chunk> _queuedChunkUpdatesHp = new Queue<Chunk>();
        private readonly Queue<Chunk> _queuedChunkUpdatesLp = new Queue<Chunk>();

        private readonly Queue<Chunk> _queuedReadyToUploadHp = new Queue<Chunk>();
        private readonly Queue<Chunk> _queuedReadyToUploadLp = new Queue<Chunk>();

        private readonly Queue<ChunkCache> _regionsReadyToAdd = new Queue<ChunkCache>();
        private readonly Queue<Vector3i> _regionsReadyToRemove = new Queue<Vector3i>();

        private int _chunkThreadsCount;
        private bool _unloaded;  // Mudado para bool
        public int RenderDistance { get; set; } = 4; // Ajustável para definir a área ao redor do player

        // Definindo o número total de regiões no eixo Y (0-256 / RegionSize)
        private const int MaxYRegions = 256 / RegionSize; // 8

        private readonly PerlinNoise _perlinNoise;
        private readonly PerlinWorm _perlinWorm;

        // Configurações para PerlinNoise
        private const int seed = 12345;
        private const int octaves = 6;
        private const float persistence = 0.5f;
        private const float frequency = 0.01f;
        private const float amplitude = 1.0f;

        /// <summary>
        /// Inicializa uma nova instância do World com geração procedural e Perlin Worm para cavernas.
        /// </summary>
        public World()
        {
            _perlinNoise = new PerlinNoise(seed: seed, octaves: octaves, persistence: persistence, frequency: frequency, amplitude: amplitude);
            //_perlinWorm = new PerlinWorm(this, seed: seed, wormLength: 200, wormStepSize: 5, wormRadius: 5);
        }

        private void PreGenerateCaverns()
        {
            var initialRegions = new List<Vector3i>();
            for (var x = -RenderDistance; x <= RenderDistance; x++)
            {
                for (var z = -RenderDistance; z <= RenderDistance; z++)
                {
                    for (var y = 0; y < MaxYRegions; y++)
                    {
                        initialRegions.Add(new Vector3i(x, y, z));
                    }
                }
            }

            foreach (var region in initialRegions)
            {
                var cache = new ChunkCache(this);
                LoadRegion(cache, region, region * RegionSize, region * RegionSize + new Vector3i(RegionSize - 1, RegionSize - 1, RegionSize - 1));
            }
        }

        public void SetBlock(Vector3i blockPos, uint id) => SetBlock(blockPos.X, blockPos.Y, blockPos.Z, id);
        public void SetBlock(int x, int y, int z, uint id) => SetBlock(x, y, z, id, true, false);

        public void SetBlock(int x, int y, int z, uint id, bool update, bool lowPriority)
        {
            var chunkInWorld = ChunkInWorld(x, y, z);
            var blockInChunk = BlockInChunk(x, y, z);

            if (loadedChunks.TryGetValue(chunkInWorld, out Chunk chunk))
            {
                chunk.SetBlock(blockInChunk.X, blockInChunk.Y, blockInChunk.Z, id);
            }
            else
            {
                chunk = new Chunk(this, chunkInWorld);
                chunk.SetBlock(blockInChunk.X, blockInChunk.Y, blockInChunk.Z, id);
                loadedChunks.Add(chunkInWorld, chunk);
            }

            if (!update) return;

            QueueChunkUpdate(chunkInWorld, lowPriority);

            // Atualiza chunks vizinhos se for na fronteira
            if (blockInChunk.X == 0)
                QueueChunkUpdate(chunkInWorld + new Vector3i(-1, 0, 0), lowPriority);
            else if (blockInChunk.X == Chunk.Size - 1)
                QueueChunkUpdate(chunkInWorld + new Vector3i(+1, 0, 0), lowPriority);

            if (blockInChunk.Y == 0)
                QueueChunkUpdate(chunkInWorld + new Vector3i(0, -1, 0), lowPriority);
            else if (blockInChunk.Y == Chunk.Size - 1)
                QueueChunkUpdate(chunkInWorld + new Vector3i(0, +1, 0), lowPriority);

            if (blockInChunk.Z == 0)
                QueueChunkUpdate(chunkInWorld + new Vector3i(0, 0, -1), lowPriority);
            else if (blockInChunk.Z == Chunk.Size - 1)
                QueueChunkUpdate(chunkInWorld + new Vector3i(0, 0, +1), lowPriority);
        }

        public uint GetBlock(Vector3i blockPos) => GetBlock(blockPos.X, blockPos.Y, blockPos.Z);

        public uint GetBlock(int x, int y, int z)
        {
            var chunkInWorld = ChunkInWorld(x, y, z);
            var blockInChunk = BlockInChunk(x, y, z);

            return loadedChunks.TryGetValue(chunkInWorld, out Chunk chunk)
                ? chunk.GetBlock(blockInChunk.X, blockInChunk.Y, blockInChunk.Z)
                : 0;
        }

        public void QueueChunkUpdate(Vector3i chunkPos, bool lowPriority)
        {
            if (loadedChunks.TryGetValue(chunkPos, out Chunk chunk))
            {
                if (lowPriority)
                {
                    lock (_queuedChunksLpSet)
                    {
                        if (!_queuedChunksLpSet.Contains(chunkPos))
                        {
                            _queuedChunkUpdatesLp.Enqueue(chunk);
                            _queuedChunksLpSet.Add(chunkPos);
                        }
                    }
                }
                else
                {
                    lock (_queuedChunksHpSet)
                    {
                        if (!_queuedChunksHpSet.Contains(chunkPos))
                        {
                            _queuedChunkUpdatesHp.Enqueue(chunk);
                            _queuedChunksHpSet.Add(chunkPos);
                        }
                    }
                }
            }
        }

        public void QueueChunkUpdate(Chunk chunk, bool lowPriority)
        {
            // Não necessário alterar este método
            QueueChunkUpdate(chunk.Position, lowPriority);
        }

        public void Update(Vector3 playerPosition)
        {
            if (_unloaded) return;

            UnloadChunks(false);
            LoadChunks(playerPosition);
            UpdateChunks();
        }

        public BlockRaytraceResult BlockRaytrace(Vector3 position, Vector3 direction, float range)
        {
            const float epsilon = 1e-6f;

            direction.NormalizeFast();
            var start = position.ToVector3i();
            var end = (position + direction * range).ToVector3i();

            var minX = Math.Min(start.X, end.X) - 1;
            var minY = Math.Min(start.Y, end.Y) - 1;
            var minZ = Math.Min(start.Z, end.Z) - 1;

            var maxX = Math.Max(start.X, end.X) + 1;
            var maxY = Math.Max(start.Y, end.Y) + 1;
            var maxZ = Math.Max(start.Z, end.Z) + 1;

            BlockRaytraceResult result = null;

            for (var x = minX; x <= maxX; x++)
            {
                for (var y = minY; y <= maxY; y++)
                {
                    for (var z = minZ; z <= maxZ; z++)
                    {
                        var block = GetBlock(x, y, z);
                        if (block == 0) continue;

                        var blockCenter = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                        var halfSize = 0.5f;

                        foreach (var face in BlockFaceHelper.Faces)
                        {
                            var normal = face.GetNormali().ToVector3();
                            var divisor = Vector3.Dot(normal, direction);
                            if (Math.Abs(divisor) < epsilon) continue;

                            var distance = Vector3.Dot(blockCenter - position, normal) / divisor;

                            if (distance < 0 || distance > range) continue;

                            var point = position + direction * distance;

                            if (point.X < x - halfSize || point.X > x + halfSize ||
                                point.Y < y - halfSize || point.Y > y + halfSize ||
                                point.Z < z - halfSize || point.Z > z + halfSize)
                            {
                                continue;
                            }

                            if (result == null || result.Distance > distance)
                            {
                                result = new BlockRaytraceResult(
                                    face,
                                    new Vector3i(x, y, z),
                                    distance,
                                    point.ToVector3i()
                                );
                            }
                        }
                    }
                }
            }

            return result;
        }

        public void Unload()
        {
            _unloaded = true;
            Logger.Info("Saving world...");

            while (loadedChunks.Count > 0)
            {
                UnloadChunks(true);
                Thread.Sleep(100);
            }

            Logger.Info("World saved!");
        }

        private void LoadChunks(Vector3 playerPosition)
        {
            var playerRegion = RegionInWorld(playerPosition.ToVector3i());
            var renderDistanceSquared = RenderDistance * RenderDistance;

            // Carrega regiões em torno do player baseado no RenderDistance (esférico no XZ)
            for (var x = -RenderDistance; x <= RenderDistance; x++)
            {
                for (var z = -RenderDistance; z <= RenderDistance; z++)
                {
                    var distanceSquared = x * x + z * z;

                    // Verifica se a região está dentro do raio (esférico no XZ)
                    if (distanceSquared > renderDistanceSquared)
                        continue;

                    for (var y = 0; y < MaxYRegions; y++) // Carrega todas as regiões no eixo Y
                    {
                        var regionPos = new Vector3i(playerRegion.X + x, y, playerRegion.Z + z);

                        // Se a região já estiver carregada, pula
                        if (_loadedRegions.Contains(regionPos))
                        {
                            continue;
                        }

                        _loadedRegions.Add(regionPos);

                        try
                        {
                            var cache = new ChunkCache(this);
                            LoadRegion(cache, regionPos, regionPos * RegionSize, regionPos * RegionSize + new Vector3i(RegionSize - 1, RegionSize - 1, RegionSize - 1));

                            // Coloca na fila para efetivamente adicionar
                            lock (_regionsReadyToAdd)
                                _regionsReadyToAdd.Enqueue(cache);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Erro ao carregar região {regionPos}: {ex.Message}");
                        }
                    }
                }
            }
        }

        private void UnloadChunks(bool unloadAll)
        {
            var regionsToUnload = new Stack<Vector3i>();
            var playerRegion = RegionInWorld(PlayerController.Position.ToVector3i());
            int renderDistSq = RenderDistance * RenderDistance;

            // identifica quais regiões devem sair da memória
            foreach (var region in _loadedRegions.ToList())
            {
                var v = region - playerRegion;
                int distSq = v.X * v.X + v.Z * v.Z;

                if (!unloadAll && distSq <= renderDistSq)
                    continue;

                regionsToUnload.Push(region);
            }

            // para cada uma, agenda salvamento e remoção
            while (regionsToUnload.Count > 0)
            {
                var region = regionsToUnload.Pop();

                // **ESSENCIAL**: permita nova geração quando o player voltar
                _generatedRegions.Remove(region);

                // agenda salvar no disco e depois remover de loadedChunks
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    WorldSerializer.SaveRegion(this, region);
                    lock (_regionsReadyToRemove)
                        _regionsReadyToRemove.Enqueue(region);
                });

                _loadedRegions.Remove(region);
            }

            // efetiva a remoção dos chunks em seguida
            while (_regionsReadyToRemove.Count > 0)
            {
                var region = _regionsReadyToRemove.Dequeue();
                var chunkMinPos = ChunkInWorld(region * RegionSize);

                for (int cx = 0; cx < ChunksPerRegion; cx++)
                    for (int cy = 0; cy < ChunksPerRegion; cy++)
                        for (int cz = 0; cz < ChunksPerRegion; cz++)
                        {
                            var key = chunkMinPos + new Vector3i(cx, cy, cz);
                            if (!loadedChunks.TryGetValue(key, out var chunk))
                                continue;

                            chunk.Dispose();
                            loadedChunks.Remove(key);

                            lock (_queuedChunksHpSet) _queuedChunksHpSet.Remove(key);
                            lock (_queuedChunksLpSet) _queuedChunksLpSet.Remove(key);
                        }
            }
        }

        private void SaveRegion(Vector3i region)
            => WorldSerializer.SaveRegion(this, region);

        /// <summary>
        /// Carrega uma região específica, gerando terreno e cavernas se necessário.
        /// </summary>
        /// <param name="cache">Cache de chunks onde os blocos serão armazenados.</param>
        /// <param name="region">Posição da região a ser carregada.</param>
        /// <param name="worldMin">Posição mínima do mundo da região.</param>
        /// <param name="worldMax">Posição máxima do mundo da região.</param>
        // World.cs (dentro da classe World)

        /// <summary>
        /// Gera proceduralmente cada chunk de uma região (2×2×2 chunks),
        /// garantindo que não haja área sem blocos definidos.
        /// </summary>
        private void LoadRegion(ChunkCache cache, Vector3i region, Vector3i worldMin, Vector3i worldMax)
        {
            // 1) se o disco já tem a região, carrega e sai
            if (WorldSerializer.LoadRegion(cache, region))
            {
                _generatedRegions.Add(region);
                lock (_regionsReadyToAdd)
                    _regionsReadyToAdd.Enqueue(cache);
                return;
            }

            // 2) marca como gerada
            _generatedRegions.Add(region);

            // 3) gere chunk por chunk
            Vector3i chunkRegionMin = new Vector3i(
                region.X * ChunksPerRegion,
                region.Y * ChunksPerRegion,
                region.Z * ChunksPerRegion
            );

            for (int cx = 0; cx < ChunksPerRegion; cx++)
                for (int cy = 0; cy < ChunksPerRegion; cy++)
                    for (int cz = 0; cz < ChunksPerRegion; cz++)
                    {
                        var chunkPos = chunkRegionMin + new Vector3i(cx, cy, cz);
                        var chunkCache = new CachedChunk(this, chunkPos);

                        int baseX = chunkPos.X * Chunk.Size;
                        int baseY = chunkPos.Y * Chunk.Size;
                        int baseZ = chunkPos.Z * Chunk.Size;

                        for (int lx = 0; lx < Chunk.Size; lx++)
                            for (int lz = 0; lz < Chunk.Size; lz++)
                            {
                                float noiseVal = _perlinNoise.GetNoise(baseX + lx, baseZ + lz);
                                int h = (int)(noiseVal * 32) + 32;

                                for (int ly = 0; ly < Chunk.Size; ly++)
                                {
                                    int wy = baseY + ly;
                                    uint id;

                                    if (wy > h)
                                        id = 0;      // ar
                                    else if (wy == h)
                                        id = 2;      // grama
                                    else if (wy >= h - 3)
                                        id = 3;      // terra
                                    else
                                        id = 1;      // pedra

                                    cache.SetBlockWithoutUpdate(
                                        baseX + lx,
                                        wy,
                                        baseZ + lz,
                                        id
                                    );
                                }
                            }

                        // 4) safe-add para evitar “key already added”
                        try
                        {
                            cache.AddChunk(chunkCache);
                        }
                        catch (ArgumentException)
                        {
                            // ok, já tinha sido adicionado
                        }
                    }

            // 5) enfileira tudo para entrar no mundo
            lock (_regionsReadyToAdd)
                _regionsReadyToAdd.Enqueue(cache);
        }


        private void UpdateChunks()
        {
            // 1) Enumera e gera toda a geometria pendente, HP + LP
            List<Chunk> toGenerate = new List<Chunk>();
            lock (_queuedChunkUpdatesHp)
            {
                while (_queuedChunkUpdatesHp.Count > 0)
                    toGenerate.Add(_queuedChunkUpdatesHp.Dequeue());
            }
            lock (_queuedChunkUpdatesLp)
            {
                while (_queuedChunkUpdatesLp.Count > 0)
                    toGenerate.Add(_queuedChunkUpdatesLp.Dequeue());
            }

            foreach (var chunk in toGenerate)
            {
                try
                {
                    // Gera a VAO completa
                    chunk.Update();
                    // Imediatamente envia para upload
                    _queuedReadyToUploadHp.Enqueue(chunk);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Erro ao gerar chunk {chunk.Position}: {ex.Message}");
                }
            }

            // 2) Agora sobe tudo o que estiver pronto (HP + LP também)
            lock (_queuedReadyToUploadHp)
            {
                while (_queuedReadyToUploadHp.Count > 0)
                {
                    var chunk = _queuedReadyToUploadHp.Dequeue();
                    try
                    {
                        chunk.Upload();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Erro ao subir chunk {chunk.Position}: {ex.Message}");
                    }
                }
            }
            lock (_queuedReadyToUploadLp)
            {
                while (_queuedReadyToUploadLp.Count > 0)
                {
                    var chunk = _queuedReadyToUploadLp.Dequeue();
                    try
                    {
                        chunk.Upload();
                    }
                    catch { }
                }
            }

            // 3) Adiciona regiões novas
            lock (_regionsReadyToAdd)
            {
                while (_regionsReadyToAdd.Count > 0)
                    _regionsReadyToAdd.Dequeue().AddToWorldAndUpdate();
            }
        }


        /// <summary>
        /// Envia imediatamente todos os chunks prontos (alta e baixa prioridade)
        /// para a GPU, garantindo que nada fique esperando.
        /// </summary>
        public void ForceUploadAllPending()
        {
            // sobe tudo que estiver pronto, sem limites
            UploadChunkQueue(_queuedReadyToUploadHp, int.MaxValue);
            UploadChunkQueue(_queuedReadyToUploadLp, int.MaxValue);
        }

        private void UploadChunkQueue(Queue<Chunk> queue, int max)
        {
            // Sobe os chunks já atualizados (geom pronta) para a GPU
            for (int i = 0; i < max && queue.Count > 0; i++)
            {
                var chunk = queue.Dequeue();
                if (chunk == null)
                    continue;      // descarta eventuais nulos
                chunk.Upload();
            }
        }

        private void UpdateChunkQueue(
     Queue<Chunk> updateQueue,
     Queue<Chunk> uploadQueue,
     HashSet<Vector3i> queuedSet,
     bool isHighPriority)
        {
            while (updateQueue.Count > 0 && _chunkThreadsCount < MaxAsyncChunkUpdates)
            {
                var chunk = updateQueue.Dequeue();
                queuedSet.Remove(chunk?.Position ?? default);

                if (chunk == null)
                    continue;   // descarta nulos aqui também

                Interlocked.Increment(ref _chunkThreadsCount);

                ThreadPool.QueueUserWorkItem(state =>
                {
                    try
                    {
                        if (chunk.Update())
                        {
                            lock (uploadQueue)
                            {
                                if (!uploadQueue.Contains(chunk))
                                    uploadQueue.Enqueue(chunk);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Erro ao atualizar chunk: {ex.Message}");
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _chunkThreadsCount);
                    }
                });
            }
        }
    }
}
