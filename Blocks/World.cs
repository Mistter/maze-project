using MazeEngine.Entities;
using MazeEngine.Utils;
using OpenTK.Mathematics;
using Vector3i = MazeEngine.Utils.Vector3i;

namespace MazeEngine.Blocks
{
    internal class World
    {
        public const int MaxChunkUploads = 1;
        public const int RegionSize = 2 * Chunk.Size; // 32 blocks
        public const int ChunksPerRegion = RegionSize / Chunk.Size;
        public static readonly int MaxAsyncChunkUpdates = Environment.ProcessorCount * 8;
        private const int MaxChunkUpdatesPerFrame = 4;

        public int RenderDistance { get; set; } = 16;
        public int MinWorldY { get; set; } = 0;
        public int MaxProceduralY { get; set; } = 256;
        private readonly int VerticalRegionDistance = 2;

        public int TerrainBaseHeight { get; set; } = 100;
        public int TerrainHeightRange { get; set; } = 32;

        // PerlinWorm parameters
        public float CaveScale { get; set; } = 0.03f;
        public float CaveThreshold { get; set; } = 0.65f;
        public int CaveMinY { get; set; } = 20;
        public int CaveMaxY { get; set; } = 80;

        private readonly object _lockLoadedRegions = new object();
        private readonly object _lockGeneratedRegions = new object();
        private readonly HashSet<Vector3i> _loadedRegions = new HashSet<Vector3i>();
        private readonly HashSet<Vector3i> _generatedRegions = new HashSet<Vector3i>();
        private readonly Queue<Vector3i> _pendingRegions = new Queue<Vector3i>();
        private readonly Queue<ChunkCache> _regionsReadyToAdd = new Queue<ChunkCache>();
        private readonly Queue<Vector3i> _regionsReadyToRemove = new Queue<Vector3i>();
        
        public readonly Dictionary<Vector3i, Chunk> loadedChunks = new Dictionary<Vector3i, Chunk>();
        private readonly Queue<Chunk> _queuedChunkUpdatesHp = new Queue<Chunk>();
        private readonly Queue<Chunk> _queuedChunkUpdatesLp = new Queue<Chunk>();
        private readonly Queue<Chunk> _queuedReadyToUploadHp = new Queue<Chunk>();
        private readonly Queue<Chunk> _queuedReadyToUploadLp = new Queue<Chunk>();
        private readonly HashSet<Vector3i> _setHp = new HashSet<Vector3i>();
        private readonly HashSet<Vector3i> _setLp = new HashSet<Vector3i>();
        private int _chunkThreadsCount;
        private bool _unloaded;

        private readonly PerlinNoise _perlinNoise;

        public World()
        {
            _perlinNoise = new PerlinNoise(seed: 12345, octaves: 6, persistence: 0.5f, frequency: 0.01f, amplitude: 1.0f);
        }

        public void Update(Vector3 playerPos)
        {
            if (_unloaded) return;
            UnloadChunks(false);
            LoadChunks(playerPos);
            UpdateChunks();
        }

        private void LoadChunks(Vector3 playerPos)
        {
            var pr = RegionInWorld(playerPos.ToVector3i());
            int minY = Math.Max(pr.Y - VerticalRegionDistance, MinWorldY / RegionSize);
            int maxY = Math.Min(pr.Y + VerticalRegionDistance, MaxProceduralY / RegionSize);
            int r2 = RenderDistance * RenderDistance;
            for (int x = -RenderDistance; x <= RenderDistance; x++)
                for (int z = -RenderDistance; z <= RenderDistance; z++)
                {
                    if (x * x + z * z > r2) continue;
                    for (int y = minY; y <= maxY; y++)
                    {
                        var region = new Vector3i(pr.X + x, y, pr.Z + z);
                        bool already;
                        lock (_lockLoadedRegions)
                        {
                            already = _loadedRegions.Contains(region);
                            if (!already) _loadedRegions.Add(region);
                        }
                        if (!already) _pendingRegions.Enqueue(region);
                    }
                }
        }

        private void ProcessPendingRegions()
        {
            int dispatched = 0;
            while (dispatched < MaxChunkUpdatesPerFrame && _pendingRegions.Count > 0)
            {
                var region = _pendingRegions.Dequeue();
                ThreadPool.QueueUserWorkItem(_ => LoadRegion(region));
                dispatched++;
            }
        }

        private void LoadRegion(Vector3i region)
        {
            var cache = new ChunkCache(this);

            // 1) Tenta carregar do disco
            if (WorldSerializer.LoadRegion(cache, region))
            {
                lock (_lockGeneratedRegions) _generatedRegions.Add(region);
                lock (_regionsReadyToAdd) _regionsReadyToAdd.Enqueue(cache);
                return;
            }
            lock (_lockGeneratedRegions) _generatedRegions.Add(region);

            // Prepara bounds
            int baseCX = region.X * ChunksPerRegion;
            int baseCY = region.Y * ChunksPerRegion;
            int baseCZ = region.Z * ChunksPerRegion;
            var regionMin = region * RegionSize;
            var regionMax = regionMin + new Vector3i(RegionSize - 1);

            // 2) Geração de terreno (idêntica à sua implementação atual)
            for (int cx = 0; cx < ChunksPerRegion; cx++)
                for (int cz = 0; cz < ChunksPerRegion; cz++)
                    for (int cy = 0; cy < ChunksPerRegion; cy++)
                    {
                        int cpX = baseCX + cx, cpY = baseCY + cy, cpZ = baseCZ + cz;
                        int bx = cpX * Chunk.Size, by = cpY * Chunk.Size, bz = cpZ * Chunk.Size;
                        for (int lx = 0; lx < Chunk.Size; lx++)
                            for (int lz = 0; lz < Chunk.Size; lz++)
                                for (int ly = 0; ly < Chunk.Size; ly++)
                                {
                                    int wx = bx + lx, wy = by + ly, wz = bz + lz;
                                    float n = _perlinNoise.GetNoise(wx, wz);
                                    int h = TerrainBaseHeight + (int)(n * TerrainHeightRange);
                                    uint id = wy > h ? 0u
                                              : wy == h ? 2u
                                              : wy >= h - 3 ? 3u
                                              : 1u;
                                    cache.SetBlockWithoutUpdate(wx, wy, wz, id);
                                }
                        cache.AddChunk(new CachedChunk(this, new Vector3i(cpX, cpY, cpZ)));
                    }

            // 3) Carving de cavernas com múltiplos worms interligados
            var rnd = new Random(region.X * 734287 + region.Y * 912783 + region.Z);
            const int worms = 6;
            const int length = 200;
            const int step = 5;
            const int radius = 3;

            // pré-gera offsets de esfera para esse raio
            var sphere = GenerateSphereOffsets(radius);

            for (int i = 0; i < worms; i++)
            {
                // cria seed única por worm
                int seed = rnd.Next();
                var worm = new PerlinWorm(_perlinNoise, seed, wormLength: length, wormStepSize: step, wormRadius: radius);

                // posiciona worm num ponto aleatório dentro da região, entre CaveMinY e CaveMaxY
                int startX = rnd.Next(regionMin.X, regionMax.X + 1);
                int startY = rnd.Next(regionMin.Y + CaveMinY, Math.Min(regionMin.Y + CaveMaxY, regionMax.Y) + 1);
                int startZ = rnd.Next(regionMin.Z, regionMax.Z + 1);
                worm.SetPosition(new Vector3(startX, startY, startZ));

                // percorre e escava
                for (int s = 0; s < worm.WormLength; s++)
                {
                    var pos = worm.Step();
                    foreach (var off in sphere)
                    {
                        var cp = pos + off;
                        if (cp.X < regionMin.X || cp.X > regionMax.X ||
                            cp.Y < regionMin.Y || cp.Y > regionMax.Y ||
                            cp.Z < regionMin.Z || cp.Z > regionMax.Z)
                            continue;
                        cache.SetBlockWithoutUpdate(cp.X, cp.Y, cp.Z, 0u);
                    }
                }
            }

            // 4) Enfileira região pronta
            lock (_regionsReadyToAdd)
                _regionsReadyToAdd.Enqueue(cache);
        }

        private void UpdateChunks()
        {
            ProcessPendingRegions();
            int dispatched = 0;
            lock (_queuedChunkUpdatesHp)
            {
                while (dispatched < MaxChunkUpdatesPerFrame && _queuedChunkUpdatesHp.Count > 0 && _chunkThreadsCount < MaxAsyncChunkUpdates)
                {
                    var chunk = _queuedChunkUpdatesHp.Dequeue();
                    _setHp.Remove(chunk.Position);
                    Interlocked.Increment(ref _chunkThreadsCount);
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            if (chunk.Update()) lock (_queuedReadyToUploadHp) { if (!_queuedReadyToUploadHp.Contains(chunk)) _queuedReadyToUploadHp.Enqueue(chunk); }
                        }
                        finally { Interlocked.Decrement(ref _chunkThreadsCount); }
                    });
                    dispatched++;
                }
            }
            ProcessUploads(_queuedReadyToUploadHp);
            ProcessUploads(_queuedReadyToUploadLp);
            ProcessAddedRegions();
        }

        private void ProcessUploads(Queue<Chunk> queue)
        {
            const int max = 4;
            int count = 0;
            lock (queue)
            {
                while (count < max && queue.Count > 0)
                {
                    queue.Dequeue().Upload();
                    count++;
                }
            }
        }

        private void ProcessAddedRegions()
        {
            lock (_regionsReadyToAdd)
            {
                while (_regionsReadyToAdd.Count > 0)
                    _regionsReadyToAdd.Dequeue().AddToWorldAndUpdate();
            }
        }

        private void UnloadChunks(bool unloadAll)
        {
            var stack = new Stack<Vector3i>();
            var pr = RegionInWorld(PlayerController.Position.ToVector3i());
            int r2 = RenderDistance * RenderDistance;
            lock (_lockLoadedRegions)
            {
                foreach (var r in _loadedRegions)
                {
                    var d = r - pr;
                    if (!unloadAll && d.X * d.X + d.Z * d.Z <= r2) continue;
                    stack.Push(r);
                }
            }
            while (stack.Count > 0)
            {
                var rg = stack.Pop();
                lock (_lockGeneratedRegions) { _generatedRegions.Remove(rg); }
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    WorldSerializer.SaveRegion(this, rg);
                    lock (_regionsReadyToRemove) _regionsReadyToRemove.Enqueue(rg);
                });
                lock (_lockLoadedRegions) _loadedRegions.Remove(rg);
            }
            while (_regionsReadyToRemove.Count > 0)
            {
                var rg = _regionsReadyToRemove.Dequeue();
                var cm = ChunkInWorld(rg * RegionSize);
                for (int cx = 0; cx < ChunksPerRegion; cx++)
                    for (int cy = 0; cy < ChunksPerRegion; cy++)
                        for (int cz = 0; cz < ChunksPerRegion; cz++)
                        {
                            var key = cm + new Vector3i(cx, cy, cz);
                            if (!loadedChunks.TryGetValue(key, out var c)) continue;
                            c.Dispose();
                            loadedChunks.Remove(key);
                        }
            }
        }

        /// <summary>
        /// Descarrega todas as regiões e salva.
        /// </summary>
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

        /// <summary>
        /// Raytrace para seleção de blocos.
        /// </summary>
        public BlockRaytraceResult BlockRaytrace(Vector3 pos, Vector3 dir, float r)
        {
            const float eps = 1e-6f;
            dir.NormalizeFast();
            var start = pos.ToVector3i();
            var end = (pos + dir * r).ToVector3i();
            int minX = Math.Min(start.X, end.X) - 1, minY = Math.Min(start.Y, end.Y) - 1, minZ = Math.Min(start.Z, end.Z) - 1;
            int maxX = Math.Max(start.X, end.X) + 1, maxY = Math.Max(start.Y, end.Y) + 1, maxZ = Math.Max(start.Z, end.Z) + 1;
            BlockRaytraceResult result = null;
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        var b = GetBlock(x, y, z);
                        if (b == 0) continue;
                        var center = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                        foreach (var face in BlockFaceHelper.Faces)
                        {
                            var n = face.GetNormali().ToVector3();
                            var div = Vector3.Dot(n, dir);
                            if (Math.Abs(div) < eps) continue;
                            var dist = Vector3.Dot(center - pos, n) / div;
                            if (dist < 0 || dist > r) continue;
                            var pt = pos + dir * dist;
                            const float half = 0.5f;
                            if (pt.X < center.X - half || pt.X > center.X + half ||
                                pt.Y < center.Y - half || pt.Y > center.Y + half ||
                                pt.Z < center.Z - half || pt.Z > center.Z + half) continue;
                            if (result == null || result.Distance > dist)
                                result = new BlockRaytraceResult(face, new Vector3i(x, y, z), dist, pt.ToVector3i());
                        }
                    }
            return result;
        }

        /// <summary>
        /// Retorna o ID do bloco. Se não existir, retorna 1 (stone).
        /// </summary>
        public uint GetBlock(Vector3i bp) => GetBlock(bp.X, bp.Y, bp.Z);
        public uint GetBlock(int x, int y, int z)
        {
            var cp = ChunkInWorld(x, y, z);
            var bc = BlockInChunk(x, y, z);
            if (!loadedChunks.TryGetValue(cp, out var c))
                return 1;
            return c.GetBlock(bc.X, bc.Y, bc.Z);
        }

        public void SetBlock(int x, int y, int z, uint id) => SetBlock(x, y, z, id, true, false);
        public void SetBlock(int x, int y, int z, uint id, bool update, bool lp)
        {
            var cp = ChunkInWorld(x, y, z);
            var bc = BlockInChunk(x, y, z);
            if (loadedChunks.TryGetValue(cp, out var chunk)) chunk.SetBlock(bc.X, bc.Y, bc.Z, id);
            else { var c = new Chunk(this, cp); c.SetBlock(bc.X, bc.Y, bc.Z, id); loadedChunks[cp] = c; }
            if (!update) return;
            QueueUpdate(cp, lp);
        }

        /// <summary>
        /// Enfileira atualização de chunk.
        /// </summary>
        public void QueueChunkUpdate(Vector3i pos, bool lp)
        {
            if (!loadedChunks.TryGetValue(pos, out var chunk)) return;
            var set = lp ? _setLp : _setHp;
            var q = lp ? _queuedChunkUpdatesLp : _queuedChunkUpdatesHp;

            lock (set)
            {
                if (!set.Contains(pos))
                {
                    set.Add(pos);
                    lock (q) { q.Enqueue(chunk); }
                }
            }
        }

        private void QueueUpdate(Vector3i pos, bool lp)
        {
            var set = lp ? _setLp : _setHp;
            var q = lp ? _queuedChunkUpdatesLp : _queuedChunkUpdatesHp;
            lock (set)
            {
                if (!set.Contains(pos)) { set.Add(pos); lock (q) { q.Enqueue(loadedChunks[pos]); } }
            }
        }

        // Helpers
        public static Vector3i RegionInWorld(Vector3i v) => RegionInWorld(v.X, v.Y, v.Z);
        public static Vector3i RegionInWorld(int x, int y, int z) => new Vector3i(
            x < 0 ? (x + 1) / RegionSize - 1 : x / RegionSize,
            y < 0 ? (y + 1) / RegionSize - 1 : y / RegionSize,
            z < 0 ? (z + 1) / RegionSize - 1 : z / RegionSize);
        public static Vector3i ChunkInWorld(Vector3i v) => ChunkInWorld(v.X, v.Y, v.Z);
        public static Vector3i ChunkInWorld(int x, int y, int z) => new Vector3i(
            x < 0 ? (x + 1) / Chunk.Size - 1 : x / Chunk.Size,
            y < 0 ? (y + 1) / Chunk.Size - 1 : y / Chunk.Size,
            z < 0 ? (z + 1) / Chunk.Size - 1 : z / Chunk.Size);
        public static Vector3i BlockInChunk(int x, int y, int z) => new Vector3i(
            x < 0 ? ((x + 1) % Chunk.Size + Chunk.Size - 1) : x % Chunk.Size,
            y < 0 ? ((y + 1) % Chunk.Size + Chunk.Size - 1) : y % Chunk.Size,
            z < 0 ? ((z + 1) % Chunk.Size + Chunk.Size - 1) : z % Chunk.Size);

        private static Vector3i[] GenerateSphereOffsets(int r)
        {
            var list = new List<Vector3i>();
            int rr = r * r;
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dz = -r; dz <= r; dz++)
                        if (dx * dx + dy * dy + dz * dz <= rr)
                            list.Add(new Vector3i(dx, dy, dz));
            return list.ToArray();
        }
    }
}
