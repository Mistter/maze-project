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
        public const int ChunksPerRegion = RegionSize / Chunk.Size; // 2 chunks/região
        public static readonly int MaxAsyncChunkUpdates = Environment.ProcessorCount * 8;

        // Locks para proteger acesso concorrente
        private readonly object _lockLoadedRegions = new object();
        private readonly object _lockGeneratedRegions = new object();

        // Distância horizontal de renderização (em regiões)
        public int RenderDistance { get; set; } = 16;

        // Limites de Y do mapa (em blocos)
        // Geração procedural ficará entre MinWorldY e MaxProceduralY
        public int MinWorldY { get; set; } = 0;
        public int MaxWorldY { get; set; } = 512;
        public int MaxProceduralY { get; set; } = 256;

        // Altura base e variação para o terreno procedural
        public int TerrainBaseHeight { get; set; } = 100;       // nível médio do terreno
        public int TerrainHeightRange { get; set; } = 32;       // variação acima/abaixo da base

        // Controle de quantas regiões acima/abaixo do jogador gerar
        private readonly int VerticalRegionDistance = 2;

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

        public IEnumerable<Chunk> Chunks => loadedChunks.Values;
        public readonly Dictionary<Vector3i, Chunk> loadedChunks = new Dictionary<Vector3i, Chunk>();

        public int ChunksQueuedCount => _queuedChunkUpdatesHp.Count + _queuedChunkUpdatesLp.Count;
        public int ChunksReadyCount => _queuedReadyToUploadHp.Count + _queuedReadyToUploadLp.Count;
        public int ChunksLoadedCount => loadedChunks.Count;
        public int ChunkThreadsCount => _chunkThreadsCount;

        private readonly PerlinNoise _perlinNoise;

        private const int seed = 12345;
        private const int octaves = 6;
        private const float persistence = 0.5f;
        private const float frequency = 0.01f;
        private const float amplitude = 1.0f;

        // Throttle aumentado para maior throughput
        private const int MaxChunkUpdatesPerFrame = 4;

        // === Parâmetros de cavernas por região ===
        public static int CaveRegionSize = 200;            // Região de 200x200 blocos
        public static int MinWormsPerRegion = 3;           // MÍNIMO de worms por região
        public static int MaxWormsPerRegion = 6;           // MÁXIMO de worms por região
        public static int WormMinLength = 1000;            // Comprimento mínimo de cada caverna
        public static int WormMaxLength = 10000;           // Comprimento máximo de cada caverna
        public static float WormStep = 2.0f;               // Passo do worm
        public static float WormCurve = 0.65f;             // Intensidade das curvas
        public static double WormPunchChance = 0.22;       // Chance de quebrar direção
        public static float WormPunchStrength = 3.7f;      // Força que vai quebrar e mudar direção

        // Bifurcação das cavernas
        public static double WormBranchChance = 0.004;     // Chance de bifurcar por segmento (eg: 0.004 = 0.4%)
        public static int MaxBranchesPerWorm = 2;          // Máximo de branches que cada worm pode criar (máximo de branch permitido)
        public static int BranchRecursionDepth = 2;        // Profundidade máxima de recursão de branches (0 = um branch não pode gerar outro branch)
        public static float BranchCurveBoost = 1.3f;       // Quão caótico é o novo branch

        // Radius das cavernas
        public static float DefaultWormRadius = 3.0f;      // Tamanho padrão da caverna
        public static float MinWormRadius = 1.0f;          // Valor minimo que ele pode atingir
        public static float MaxWormRadius = 7.0f;          // Valor máximo que pode atingir
        public static float WormRadiusVariation = 0.90f;   // quanto maior, mais abrupta a variação de raio
        public static float WormRadiusSmoothness = 0.4f;   // quanto menor, mais suave a transição do raio (0.2-0.5 recomendado)

        // Mega cavernas
        public static float MegaRoomChance = 0.001f;       // chance de um segmento ser uma mega burraco
        public static int MegaRoomMinLength = 16;          // extensão/comprimento mínimo da mega room
        public static int MegaRoomMaxLength = 36;          // extensão/comprimento máximo da mega room
        public static float MegaRoomMinRadius = 7.0f;      // largura/espessura min do mega burraco
        public static float MegaRoomMaxRadius = 9.0f;      // largura/espessura do mega burraco
        public static int MegaRoomBlendLength = 18;        // suavização do mega buraco ao surgir (aumentar valor suaviza mais!)

        // Limitação de sobreposições
        public static float MinMegaRoomDistance = 32.0f;      // Distância mínima entre MegaRooms
        public static float MinWormStartDistance = 24.0f;     // Distância mínima entre starts de worms

        // Fim do tunel
        public static float WormDeathChance = 0.0004f;        // Chance de morte súbita por segmento
        public static int MinWormDeathLength = 2000;        // Só morre após esse ponto
        public static int WormDeathBlendLength = 48;        // Segmentos para sumir o raio do worm

        private static List<Vector3> _recentMegaRoomCenters = new List<Vector3>();
        private static List<Vector3> _recentWormStarts = new List<Vector3>();

        private static bool IsFarEnough(List<Vector3> list, Vector3 pos, float minDist)
        {
            foreach (var p in list)
                if ((p - pos).LengthSquared < minDist * minDist)
                    return false;
            return true;
        }

        // Estrutura para segmento de worm
        struct WormSegment
        {
            public Vector3 Position;
            public Vector3 Direction;
            public float Radius;
        }

        // Lista global de worms por região
        private static Dictionary<(int, int), List<WormSegment[]>> _allRegionWorms = new Dictionary<(int, int), List<WormSegment[]>>();
        private static readonly object _wormLock = new object();

        // Gera worms para UMA região (região de 200x200 em X/Z)
        private List<WormSegment[]> GenerateWormsForRegion(int regionX, int regionZ, int seed)
        {
            var worms = new List<WormSegment[]>();
            int regionSeed = seed ^ (int)(regionX * 341873128712L ^ regionZ * 132897987541L);
            var random = new Random(regionSeed);

            int wormCount = random.Next(MinWormsPerRegion, MaxWormsPerRegion + 1);
            for (int i = 0; i < wormCount; i++)
            {
                int wormSeed = regionSeed ^ (i * 10007);
                float startX = regionX * CaveRegionSize + random.Next(0, CaveRegionSize);
                float startY = random.Next(30, 80);
                float startZ = regionZ * CaveRegionSize + random.Next(0, CaveRegionSize);

                float angle = (float)(random.NextDouble() * Math.PI * 2);
                Vector3 dir = new Vector3((float)Math.Cos(angle), (float)((random.NextDouble() - 0.5) * 0.4f), (float)Math.Sin(angle));

                // Novo: comprimento aleatório do túnel!
                int wormLength = random.Next(WormMinLength, WormMaxLength + 1);

                var worm = GenerateWormSegments(
                    wormLength, wormSeed, new Vector3(startX, startY, startZ), dir, WormStep, WormCurve
                ).ToArray();
                worms.Add(worm);
            }
            return worms;
        }

        // Garante que worms de uma região estejam carregados
        private void EnsureRegionWormsLoaded(int regionX, int regionZ)
        {
            lock (_wormLock)
            {
                if (!_allRegionWorms.ContainsKey((regionX, regionZ)))
                {
                    _allRegionWorms[(regionX, regionZ)] = GenerateWormsForRegion(regionX, regionZ, seed);
                }
            }
        }

        // Gera o caminho do worm (igual ao seu antigo, mas agora retorna array)
        private static List<World.WormSegment> GenerateWormSegments(
    int count,
    int seed,
    Vector3 start,
    Vector3 dir,
    float stepSize,
    float curveAmount,
    int branchDepth = 0,
    int maxBranches = -1)
        {
            var list = new List<WormSegment>();
            var random = new Random(seed);
            Vector3 position = start;
            Vector3 direction = dir.Normalized();

            float yaw = (float)Math.Atan2(direction.Z, direction.X);
            float pitch = (float)Math.Asin(direction.Y);

            int branchesCreated = 0;
            int branchesLimit = (maxBranches == -1) ? MaxBranchesPerWorm : maxBranches;

            // ====== Sobreposição de início de worm ======
            if (branchDepth == 0)
            {
                if (!IsFarEnough(_recentWormStarts, start, MinWormStartDistance))
                    return list; // Não gera esse worm
                _recentWormStarts.Add(start);
            }

            // Mega room
            bool inMegaRoom = false;
            int megaRoomStart = -1, megaRoomEnd = -1, megaRoomLength = 0;
            float megaRoomTargetRadius = DefaultWormRadius;
            float defaultRadius = DefaultWormRadius;
            float radius = DefaultWormRadius;

            // Morte súbita
            bool dying = false;
            int deathStart = -1;

            for (int i = 0; i < count; i++)
            {
                // ====== Mega room: só inicia se não sobrepor ======
                if (!inMegaRoom && random.NextDouble() < MegaRoomChance)
                {
                    if (IsFarEnough(_recentMegaRoomCenters, position, MinMegaRoomDistance))
                    {
                        inMegaRoom = true;
                        megaRoomStart = i;
                        megaRoomLength = random.Next(MegaRoomMinLength, MegaRoomMaxLength + 1);
                        megaRoomEnd = megaRoomStart + megaRoomLength;
                        megaRoomTargetRadius = (float)(MegaRoomMinRadius + (MegaRoomMaxRadius - MegaRoomMinRadius) * random.NextDouble());
                        _recentMegaRoomCenters.Add(position);
                    }
                }

                // ====== Suavização de raio (mega room) ======
                if (inMegaRoom)
                {
                    int blendIn = Math.Min(i - megaRoomStart, MegaRoomBlendLength);
                    int blendOut = Math.Min(megaRoomEnd - i, MegaRoomBlendLength);

                    if (blendIn < MegaRoomBlendLength)
                        radius = MathHelper.Lerp(defaultRadius, megaRoomTargetRadius, blendIn / (float)MegaRoomBlendLength);
                    else if (blendOut < MegaRoomBlendLength)
                        radius = MathHelper.Lerp(megaRoomTargetRadius, defaultRadius, 1f - (blendOut / (float)MegaRoomBlendLength));
                    else
                        radius = megaRoomTargetRadius;

                    if (i >= megaRoomEnd)
                    {
                        inMegaRoom = false;
                        radius = defaultRadius;
                    }
                }
                else
                {
                    // ====== Radius padrão, com variação suavizada ======
                    float targetRadius = defaultRadius + ((float)random.NextDouble() - 0.5f) * (WormRadiusVariation * 3);
                    targetRadius = Math.Max(1.5f, targetRadius);
                    // Suaviza transição do raio, se desejado
                    float radiusSmoothFactor = 0.28f; // Novo parâmetro! Exponha se quiser mais controle
                    radius = radius * (1 - radiusSmoothFactor) + targetRadius * radiusSmoothFactor;
                }

                // ====== Morte súbita ======
                if (!dying && i > MinWormDeathLength && random.NextDouble() < WormDeathChance)
                {
                    dying = true;
                    deathStart = i;
                }
                if (dying)
                {
                    float t = (i - deathStart) / (float)WormDeathBlendLength;
                    radius = MathHelper.Lerp(radius, 0f, t);
                    if (t >= 1f)
                        break; // encerra worm
                }

                // ====== Variação suave do ângulo ======
                float deltaYaw = ((float)random.NextDouble() - 0.5f) * curveAmount * 1.4f;
                float deltaPitch = ((float)random.NextDouble() - 0.5f) * curveAmount * 0.35f;

                // Punch chance
                if (random.NextDouble() < WormPunchChance)
                {
                    float punch = (float)(random.NextDouble() - 0.5f) * curveAmount * WormPunchStrength;
                    deltaYaw += punch;
                    if (random.NextDouble() < 0.55)
                        deltaPitch += (float)(random.NextDouble() - 0.5f) * curveAmount * 0.7f;
                    else
                        deltaPitch += (float)(random.NextDouble() - 0.5f) * curveAmount * 1.6f;
                }

                // Virada brusca rara
                if (random.NextDouble() < 0.02)
                {
                    yaw += (float)(random.NextDouble() - 0.5f) * MathF.PI;
                    pitch += (float)(random.NextDouble() - 0.5f) * 0.7f;
                }

                yaw += deltaYaw;
                pitch += deltaPitch;
                pitch = Math.Clamp(pitch, -0.5f, 0.5f);

                direction.X = (float)(Math.Cos(pitch) * Math.Cos(yaw));
                direction.Y = (float)Math.Sin(pitch);
                direction.Z = (float)(Math.Cos(pitch) * Math.Sin(yaw));
                direction = direction.Normalized();

                // Salva o segmento com raio atual
                list.Add(new WormSegment { Position = position, Direction = direction, Radius = radius });

                // ====== Bifurcação ======
                if (branchDepth < BranchRecursionDepth && branchesCreated < branchesLimit)
                {
                    if (random.NextDouble() < WormBranchChance)
                    {
                        float branchYawOffset = ((float)random.NextDouble() - 0.5f) * MathF.PI * 0.7f;
                        float branchPitchOffset = ((float)random.NextDouble() - 0.5f) * 0.7f;

                        float branchYaw = yaw + branchYawOffset;
                        float branchPitch = pitch + branchPitchOffset;

                        Vector3 branchDir;
                        branchDir.X = (float)(Math.Cos(branchPitch) * Math.Cos(branchYaw));
                        branchDir.Y = (float)Math.Sin(branchPitch);
                        branchDir.Z = (float)(Math.Cos(branchPitch) * Math.Sin(branchYaw));
                        branchDir = branchDir.Normalized();

                        int branchSeed = seed ^ (i * 739391) ^ random.Next();
                        int branchLength = (int)(count * (0.4 + 0.4 * random.NextDouble()));
                        float branchCurve = curveAmount * (1f + BranchCurveBoost * (float)random.NextDouble());

                        var branchSegments = GenerateWormSegments(
                            branchLength, branchSeed, position, branchDir, stepSize, branchCurve, branchDepth + 1);

                        list.AddRange(branchSegments);
                        branchesCreated++;
                    }
                }

                position += direction * stepSize;
            }
            return list;
        }

        // Escava o worm no chunk (recebe array de segmentos)
        private void CarveWormInChunk(ChunkCache cache, Vector3i chunkMin, Vector3i chunkMax, WormSegment[] wormSegments)
        {
            foreach (var seg in wormSegments)
            {
                Vector3 pos = seg.Position;
                float radius = seg.Radius;

                // Checa bounding box do segmento com raio variável
                if (pos.X < chunkMin.X - radius || pos.X > chunkMax.X + radius) continue;
                if (pos.Y < chunkMin.Y - radius || pos.Y > chunkMax.Y + radius) continue;
                if (pos.Z < chunkMin.Z - radius || pos.Z > chunkMax.Z + radius) continue;

                int minX = Math.Max((int)Math.Floor(pos.X - radius), chunkMin.X);
                int maxX = Math.Min((int)Math.Ceiling(pos.X + radius), chunkMax.X);
                int minY = Math.Max((int)Math.Floor(pos.Y - radius), chunkMin.Y);
                int maxY = Math.Min((int)Math.Ceiling(pos.Y + radius), chunkMax.Y);
                int minZ = Math.Max((int)Math.Floor(pos.Z - radius), chunkMin.Z);
                int maxZ = Math.Min((int)Math.Ceiling(pos.Z + radius), chunkMax.Z);

                for (int x = minX; x <= maxX; x++)
                    for (int y = minY; y <= maxY; y++)
                        for (int z = minZ; z <= maxZ; z++)
                        {
                            var dist = Math.Sqrt(
                                (x + 0.5 - pos.X) * (x + 0.5 - pos.X) +
                                (y + 0.5 - pos.Y) * (y + 0.5 - pos.Y) +
                                (z + 0.5 - pos.Z) * (z + 0.5 - pos.Z));
                            if (dist <= radius)
                                cache.SetBlockWithoutUpdate(x, y, z, 0);
                        }
            }
        }

        public World()
        {
            _perlinNoise = new PerlinNoise(seed, octaves, persistence, frequency, amplitude);
        }

        /// <summary>
        /// Define um bloco na posição especificada.
        /// </summary>
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

        /// <summary>
        /// Enfileira atualização de chunk.
        /// </summary>
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
                    lock (q) { q.Enqueue(chunk); }
                }
            }
        }
        public void QueueChunkUpdate(Chunk c, bool lp) => QueueChunkUpdate(c.Position, lp);

        /// <summary>
        /// Loop principal: descarrega, carrega e atualiza chunks.
        /// </summary>
        public void Update(Vector3 playerPos)
        {
            if (_unloaded) return;
            UnloadChunks(false);
            LoadChunks(playerPos);
            UpdateChunks();
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
        /// Carrega regiões de chunks ao redor do jogador, dinâmica em Y.
        /// </summary>
        private void LoadChunks(Vector3 playerPos)
        {
            var pr = RegionInWorld(playerPos.ToVector3i());
            var max2 = RenderDistance * RenderDistance;
            int playerRegionY = pr.Y;
            int minRegionY = playerRegionY - VerticalRegionDistance;
            int maxRegionY = playerRegionY + VerticalRegionDistance;
            int globalMinRegionY = MinWorldY / RegionSize;
            int globalMaxProcRegionY = MaxProceduralY / RegionSize;
            minRegionY = Math.Max(minRegionY, globalMinRegionY);
            maxRegionY = Math.Min(maxRegionY, globalMaxProcRegionY);

            for (int x = -RenderDistance; x <= RenderDistance; x++)
                for (int z = -RenderDistance; z <= RenderDistance; z++)
                {
                    if (x * x + z * z > max2) continue;
                    for (int y = minRegionY; y <= maxRegionY; y++)
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

        /// <summary>
        /// Processa filas de geração pendentes em threads.
        /// </summary>
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

        /// <summary>
        /// Descarrega chunks fora do alcance ou em unloadAll.
        /// </summary>
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

        /// <summary>
        /// Carrega ou gera uma região inteira, incluindo terreno e cavernas.
        /// </summary>
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

            // --- NOVO: calcula quais regiões de caverna podem afetar este chunk ---
            int minRegionX = (worldMin.X) / CaveRegionSize;
            int maxRegionX = (worldMax.X) / CaveRegionSize;
            int minRegionZ = (worldMin.Z) / CaveRegionSize;
            int maxRegionZ = (worldMax.Z) / CaveRegionSize;

            // Garante que worms dessas regiões estão carregados
            for (int rx = minRegionX; rx <= maxRegionX; rx++)
            for (int rz = minRegionZ; rz <= maxRegionZ; rz++)
                EnsureRegionWormsLoaded(rx, rz);

            // Coleta todos os worms que podem afetar este chunk
            var affectingWorms = new List<WormSegment[]>();
            for (int rx = minRegionX; rx <= maxRegionX; rx++)
            for (int rz = minRegionZ; rz <= maxRegionZ; rz++)
                affectingWorms.AddRange(_allRegionWorms[(rx, rz)]);

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
                    int variation = (int)(noise * TerrainHeightRange);
                    int h = TerrainBaseHeight + variation;
                    for (int ly = 0; ly < Chunk.Size; ly++)
                    {
                        int wy = by + ly;
                        uint id;
                        if (wy > h) id = 0;
                        else if (wy == h) id = 2;
                        else if (wy >= h - 3) id = 3;
                        else id = 1;

                        if (id == 1 && new Random().Next(2) == 1)
                                    {
                                        id = 3;
                                    }

                        cache.SetBlockWithoutUpdate(bx + lx, wy, bz + lz, id);
                    }
                }

                Vector3i chunkMin = new Vector3i(bx, by, bz);
                Vector3i chunkMax = new Vector3i(bx + Chunk.Size - 1, by + Chunk.Size - 1, bz + Chunk.Size - 1);

                // Escava todos os worms relevantes
                foreach (var worm in affectingWorms)
                {
                    CarveWormInChunk(cache, chunkMin, chunkMax, worm);
                }

                try { cache.AddChunk(cc); } catch { }
            }
            lock (_regionsReadyToAdd) _regionsReadyToAdd.Enqueue(cache);
        }

        /// <summary>
        /// Gera e faz upload de chunks pendentes.
        /// </summary>
        private void UpdateChunks()
        {
            ProcessPendingRegions();
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
            lock (_regionsReadyToAdd)
            {
                while (_regionsReadyToAdd.Count > 0)
                {
                    var cache = _regionsReadyToAdd.Dequeue();
                    cache.AddToWorldAndUpdate();
                }
            }
        }

        /// <summary>
        /// Faz upload manual de todos os chunks pendentes.
        /// </summary>
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

        // Helpers estáticos
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
    }
}
