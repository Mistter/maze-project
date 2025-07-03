// World.cs
using MazeEngine.Entities;
using MazeEngine.Utils;
using OpenTK.Mathematics;
using Vector3i = MazeEngine.Utils.Vector3i;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MazeEngine.Blocks
{
    internal class World
    {
        public const int MaxChunkUploads = 1;
        public const int RegionSize = 2 * Chunk.Size;      // 32 blocos
        public const int ChunksPerRegion = RegionSize / Chunk.Size; // 2 chunks/região

        public static readonly int MaxAsyncChunkUpdates = Environment.ProcessorCount * 8;

        // Locks para proteger acesso concorrente
        private readonly object _lockLoadedRegions = new object();
        private readonly object _lockGeneratedRegions = new object();

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

        public readonly Dictionary<Vector3i, Chunk> loadedChunks = new Dictionary<Vector3i, Chunk>();
        public IEnumerable<Chunk> Chunks => loadedChunks.Values;

        public int ChunksQueuedCount => _queuedChunkUpdatesHp.Count + _queuedChunkUpdatesLp.Count;
        public int ChunksReadyCount => _queuedReadyToUploadHp.Count + _queuedReadyToUploadLp.Count;
        public int ChunksLoadedCount => loadedChunks.Count;
        public int ChunkThreadsCount => _chunkThreadsCount;

        private readonly HashSet<Vector3i> _loadedRegions = new HashSet<Vector3i>();
        private readonly HashSet<Vector3i> _generatedRegions = new HashSet<Vector3i>();
        private readonly HashSet<Vector3i> _queuedChunksHpSet = new HashSet<Vector3i>();
        private readonly HashSet<Vector3i> _queuedChunksLpSet = new HashSet<Vector3i>();

        private readonly Queue<Chunk> _queuedChunkUpdatesHp = new Queue<Chunk>();
        private readonly Queue<Chunk> _queuedChunkUpdatesLp = new Queue<Chunk>();
        private readonly Queue<Chunk> _queuedReadyToUploadHp = new Queue<Chunk>();
        private readonly Queue<Chunk> _queuedReadyToUploadLp = new Queue<Chunk>();
        private readonly Queue<ChunkCache> _regionsReadyToAdd = new Queue<ChunkCache>();
        private readonly Queue<Vector3i> _regionsReadyToRemove = new Queue<Vector3i>();
        private readonly Queue<Vector3i> _pendingRegions = new Queue<Vector3i>();

        private int _chunkThreadsCount;
        private bool _unloaded;
        public int RenderDistance { get; set; } = 16;

        private const int MaxYRegions = 256 / RegionSize;
        private readonly int VerticalRegionDistance = 2;

        private readonly PerlinNoise _perlinNoise;
        private readonly PerlinWorm _perlinWorm;

        private const int seed = 12345;
        private const int octaves = 6;
        private const float persistence = 0.5f;
        private const float frequency = 0.01f;
        private const float amplitude = 1.0f;

        // Throttle aumentado para maior throughput
        private const int MaxChunkUpdatesPerFrame = 4;

        public World()
        {
            _perlinNoise = new PerlinNoise(seed, octaves, persistence, frequency, amplitude);
            _perlinWorm = new PerlinWorm(this, seed, wormLength: 200, wormStepSize: 5, wormRadius: 5);
        }

        private void PreGenerateCaverns()
        {
            var initial = new List<Vector3i>();
            for (int x = -RenderDistance; x <= RenderDistance; x++)
                for (int z = -RenderDistance; z <= RenderDistance; z++)
                    for (int y = 0; y < MaxYRegions; y++)
                        initial.Add(new Vector3i(x, y, z));

            foreach (var region in initial)
            {
                var cache = new ChunkCache(this);
                LoadRegion(cache, region,
                    region * RegionSize,
                    region * RegionSize + new Vector3i(RegionSize - 1, RegionSize - 1, RegionSize - 1)
                );
            }
        }

        public void SetBlock(Vector3i bp, uint id) => SetBlock(bp.X, bp.Y, bp.Z, id);
        public void SetBlock(int x, int y, int z, uint id) => SetBlock(x, y, z, id, true, false);
        public void SetBlock(int x, int y, int z, uint id, bool update, bool lowPriority)
        {
            var cp = ChunkInWorld(x, y, z);
            var bc = BlockInChunk(x, y, z);

            if (loadedChunks.TryGetValue(cp, out var chunk))
                chunk.SetBlock(bc.X, bc.Y, bc.Z, id);
            else
            {
                chunk = new Chunk(this, cp);
                chunk.SetBlock(bc.X, bc.Y, bc.Z, id);
                loadedChunks.Add(cp, chunk);
            }

            if (!update) return;

            QueueChunkUpdate(cp, lowPriority);
            if (bc.X == 0) QueueChunkUpdate(cp + new Vector3i(-1, 0, 0), lowPriority);
            else if (bc.X == Chunk.Size - 1) QueueChunkUpdate(cp + new Vector3i(+1, 0, 0), lowPriority);
            if (bc.Y == 0) QueueChunkUpdate(cp + new Vector3i(0, -1, 0), lowPriority);
            else if (bc.Y == Chunk.Size - 1) QueueChunkUpdate(cp + new Vector3i(0, +1, 0), lowPriority);
            if (bc.Z == 0) QueueChunkUpdate(cp + new Vector3i(0, 0, -1), lowPriority);
            else if (bc.Z == Chunk.Size - 1) QueueChunkUpdate(cp + new Vector3i(0, 0, +1), lowPriority);
        }

        public uint GetBlock(Vector3i bp) => GetBlock(bp.X, bp.Y, bp.Z);

        /// <summary>
        /// Retorna o ID do bloco. Se o chunk não existir, devolve 1 (stone) para bloquear face.
        /// </summary>
        public uint GetBlock(int x, int y, int z)
        {
            var cp = ChunkInWorld(x, y, z);
            var bc = BlockInChunk(x, y, z);
            if (!loadedChunks.TryGetValue(cp, out var c))
            {
                return 1;
            }
            return c.GetBlock(bc.X, bc.Y, bc.Z);
        }

        public void QueueChunkUpdate(Vector3i pos, bool lp)
        {
            if (!loadedChunks.TryGetValue(pos, out var chunk)) return;
            var set = lp ? _queuedChunksLpSet : _queuedChunksHpSet;
            var q = lp ? _queuedChunkUpdatesLp : _queuedChunkUpdatesHp;

            lock (set)
            {
                if (!set.Contains(pos))
                {
                    set.Add(pos);
                    lock (q) q.Enqueue(chunk);
                }
            }
        }
        public void QueueChunkUpdate(Chunk c, bool lp) => QueueChunkUpdate(c.Position, lp);

        public void Update(Vector3 playerPos)
        {
            if (_unloaded) return;
            UnloadChunks(false);
            LoadChunks(playerPos);
            UpdateChunks();
        }

        public BlockRaytraceResult BlockRaytrace(Vector3 pos, Vector3 dir, float r)
        {
            const float eps = 1e-6f;
            dir.NormalizeFast();
            var start = pos.ToVector3i();
            var end = (pos + dir * r).ToVector3i();

            int minX = Math.Min(start.X, end.X) - 1,
                minY = Math.Min(start.Y, end.Y) - 1,
                minZ = Math.Min(start.Z, end.Z) - 1;
            int maxX = Math.Max(start.X, end.X) + 1,
                maxY = Math.Max(start.Y, end.Y) + 1,
                maxZ = Math.Max(start.Z, end.Z) + 1;

            BlockRaytraceResult result = null;
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        var b = GetBlock(x, y, z);
                        if (b == 0) continue;
                        var center = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                        const float half = 0.5f;
                        foreach (var face in BlockFaceHelper.Faces)
                        {
                            var n = face.GetNormali().ToVector3();
                            var div = Vector3.Dot(n, dir);
                            if (Math.Abs(div) < eps) continue;
                            var dist = Vector3.Dot(center - pos, n) / div;
                            if (dist < 0 || dist > r) continue;
                            var pt = pos + dir * dist;
                            if (pt.X < center.X - half || pt.X > center.X + half ||
                                pt.Y < center.Y - half || pt.Y > center.Y + half ||
                                pt.Z < center.Z - half || pt.Z > center.Z + half)
                                continue;
                            if (result == null || result.Distance > dist)
                                result = new BlockRaytraceResult(face, new Vector3i(x, y, z), dist, pt.ToVector3i());
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

        private void LoadChunks(Vector3 playerPos)
        {
            var pr = RegionInWorld(playerPos.ToVector3i());
            var max2 = RenderDistance * RenderDistance;
            int minY = Math.Max(0, pr.Y - VerticalRegionDistance);
            int maxY = Math.Min(MaxYRegions - 1, pr.Y + VerticalRegionDistance);

            for (int x = -RenderDistance; x <= RenderDistance; x++)
                for (int z = -RenderDistance; z <= RenderDistance; z++)
                {
                    if (x * x + z * z > max2) continue;
                    for (int y = minY; y <= maxY; y++)
                    {
                        var region = new Vector3i(pr.X + x, y, pr.Z + z);
                        bool already;
                        lock (_lockLoadedRegions)
                        {
                            already = _loadedRegions.Contains(region);
                            if (!already) _loadedRegions.Add(region);
                        }
                        if (already) continue;
                        _pendingRegions.Enqueue(region);
                    }
                }
        }

        private void ProcessPendingRegions()
        {
            int dispatched = 0;
            while (dispatched < MaxChunkUpdatesPerFrame && _pendingRegions.Count > 0)
            {
                var reg = _pendingRegions.Dequeue();
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        var cache = new ChunkCache(this);
                        var wm = reg * RegionSize;
                        var wM = wm + new Vector3i(RegionSize - 1, RegionSize - 1, RegionSize - 1);
                        LoadRegion(cache, reg, wm, wM);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Erro ao gerar região {reg}: {ex.Message}");
                    }
                });
                dispatched++;
            }
        }

        private void UnloadChunks(bool unloadAll)
        {
            var stack = new Stack<Vector3i>();
            var pr = RegionInWorld(PlayerController.Position.ToVector3i());
            int r2 = RenderDistance * RenderDistance;

            List<Vector3i> snapshot;
            lock (_lockLoadedRegions) snapshot = _loadedRegions.ToList();

            foreach (var r in snapshot)
            {
                var v = r - pr;
                int d2 = v.X * v.X + v.Z * v.Z;
                if (!unloadAll && d2 <= r2) continue;
                stack.Push(r);
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
                lock (_lockLoadedRegions) { _loadedRegions.Remove(rg); }
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
                            lock (_queuedChunksHpSet) _queuedChunksHpSet.Remove(key);
                            lock (_queuedChunksLpSet) _queuedChunksLpSet.Remove(key);
            }
        }
        }

        private void LoadRegion(ChunkCache cache, Vector3i region, Vector3i worldMin, Vector3i worldMax)
        {
            if (WorldSerializer.LoadRegion(cache, region))
            {
                lock (_lockGeneratedRegions) _generatedRegions.Add(region);
                lock (_regionsReadyToAdd) _regionsReadyToAdd.Enqueue(cache);
                return;
            }
            lock (_lockGeneratedRegions) _generatedRegions.Add(region);
            var crm = new Vector3i(region.X * ChunksPerRegion, region.Y * ChunksPerRegion, region.Z * ChunksPerRegion);
            for (int cx = 0; cx < ChunksPerRegion; cx++)
                for (int cy = 0; cy < ChunksPerRegion; cy++)
                    for (int cz = 0; cz < ChunksPerRegion; cz++)
                    {
                        var cp = crm + new Vector3i(cx, cy, cz);
                        var cc = new CachedChunk(this, cp);
                        int bx = cp.X * Chunk.Size, by = cp.Y * Chunk.Size, bz = cp.Z * Chunk.Size;
                        for (int lx = 0; lx < Chunk.Size; lx++)
                            for (int lz = 0; lz < Chunk.Size; lz++)
                            {
                                float noise = _perlinNoise.GetNoise(bx + lx, bz + lz);
                                int h = (int)(noise * 32) + 32;
                                for (int ly = 0; ly < Chunk.Size; ly++)
                                {
                                    int wy = by + ly;
                                    uint id;
                                    if (wy > h) id = 0;
                                    else if (wy == h) id = 2;
                                    else if (wy >= h - 3) id = 3;
                                    else id = 1;
                                    cache.SetBlockWithoutUpdate(bx + lx, wy, bz + lz, id);
                                }
                            }
                        try { cache.AddChunk(cc); } catch { }
                    }
            lock (_regionsReadyToAdd) _regionsReadyToAdd.Enqueue(cache);
        }

        private void UpdateChunks()
        {
            ProcessPendingRegions();

            // 1) Geração
            int dispatched = 0;
            lock (_queuedChunkUpdatesHp)
            {
                while (dispatched < MaxChunkUpdatesPerFrame
                       && _queuedChunkUpdatesHp.Count > 0
                       && _chunkThreadsCount < MaxAsyncChunkUpdates)
                {
                    var chunk = _queuedChunkUpdatesHp.Dequeue();
                    _queuedChunksHpSet.Remove(chunk.Position);
                    Interlocked.Increment(ref _chunkThreadsCount);
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            if (chunk.Update())
                            {
                                lock (_queuedReadyToUploadHp)
                                    if (!_queuedReadyToUploadHp.Contains(chunk))
                                        _queuedReadyToUploadHp.Enqueue(chunk);
                            }
                        }
                        catch { }
                        finally { Interlocked.Decrement(ref _chunkThreadsCount); }
                    });
                    dispatched++;
                }
            }
            dispatched = 0;
            lock (_queuedChunkUpdatesLp)
            {
                while (dispatched < MaxChunkUpdatesPerFrame
                       && _queuedChunkUpdatesLp.Count > 0
                       && _chunkThreadsCount < MaxAsyncChunkUpdates)
                {
                    var chunk = _queuedChunkUpdatesLp.Dequeue();
                    _queuedChunksLpSet.Remove(chunk.Position);
                    Interlocked.Increment(ref _chunkThreadsCount);
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            if (chunk.Update())
                            {
                                lock (_queuedReadyToUploadLp)
                                    if (!_queuedReadyToUploadLp.Contains(chunk))
                                        _queuedReadyToUploadLp.Enqueue(chunk);
                            }
                        }
                        catch { }
                        finally { Interlocked.Decrement(ref _chunkThreadsCount); }
                    });
                    dispatched++;
                }
            }

            // 2) Upload
            const int maxUploads = 4;
            int ups = 0;
            lock (_queuedReadyToUploadHp)
            {
                while (ups < maxUploads && _queuedReadyToUploadHp.Count > 0)
                {
                    var c = _queuedReadyToUploadHp.Dequeue();
                    try { c.Upload(); } catch { }
                    ups++;
                }
            }
            ups = 0;
            lock (_queuedReadyToUploadLp)
            {
                while (ups < maxUploads && _queuedReadyToUploadLp.Count > 0)
                {
                    var c = _queuedReadyToUploadLp.Dequeue();
                    try { c.Upload(); } catch { }
                    ups++;
                }
            }

            // 3) Adicionar regiões prontas
            lock (_regionsReadyToAdd)
            {
                while (_regionsReadyToAdd.Count > 0)
                {
                    var cache = _regionsReadyToAdd.Dequeue();
                    cache.AddToWorldAndUpdate();
                }
            }
        }

        public void ForceUploadAllPending()
        {
            UploadChunkQueue(_queuedReadyToUploadHp, int.MaxValue);
            UploadChunkQueue(_queuedReadyToUploadLp, int.MaxValue);
        }

        private void UploadChunkQueue(Queue<Chunk> q, int max)
        {
            for (int i = 0; i < max && q.Count > 0; i++)
            {
                var c = q.Dequeue();
                if (c != null) c.Upload();
            }
        }
    }
}
