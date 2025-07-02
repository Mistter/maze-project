using MazeEngine.Graphics;
using MazeEngine.Utils;
using System.IO;

namespace MazeEngine.Blocks
{
    internal class Chunk : IDisposable
    {
        public const int Size = 16;

        public readonly Vector3i Position;

        private uint[,,] _blockIds = new uint[Size, Size, Size];
        private readonly World _world;

        private Vector3i _min = new Vector3i(Size);
        private Vector3i _max = new Vector3i(-1);

        private readonly VertexArrayObject _vao = new VertexArrayObject();
        private bool _interrupted;

        public Chunk(World world, Vector3i position)
        {
            _world = world;
            Position = position;
        }

        public Chunk(CachedChunk cachedChunk)
            : this(cachedChunk.World, cachedChunk.Position)
        {
            _blockIds = cachedChunk.BlockIds;
            _min = cachedChunk.Min;
            _max = cachedChunk.Max;
        }

        public void SetBlock(int x, int y, int z, uint id)
        {
            if (_blockIds[x, y, z] == id) return;
            _blockIds[x, y, z] = id;
            if (x < _min.X) _min.X = x;
            if (y < _min.Y) _min.Y = y;
            if (z < _min.Z) _min.Z = z;
            if (x > _max.X) _max.X = x;
            if (y > _max.Y) _max.Y = y;
            if (z > _max.Z) _max.Z = z;
        }

        public uint GetBlock(int x, int y, int z)
        {
            if (x < 0 || x >= Size || y < 0 || y >= Size || z < 0 || z >= Size) return 0;
            return _blockIds[x, y, z];
        }

        public bool Update()
        {
            lock (this)
            {
                _interrupted = false;
                AddBlockToVao();
                return !_interrupted;
            }
        }

        public void Upload()
        {
            lock (this)
            {
                _vao.Upload();
                _vao.Clear();
            }
        }

        public void InterruptUpdate() => _interrupted = true;

        public void Draw() => _vao.Draw();

        public void Write(BinaryWriter writer)
        {
            writer.Write(_min.X); writer.Write(_min.Y); writer.Write(_min.Z);
            writer.Write(_max.X); writer.Write(_max.Y); writer.Write(_max.Z);
            for (int x = _min.X; x <= _max.X; x++)
                for (int y = _min.Y; y <= _max.Y; y++)
                    for (int z = _min.Z; z <= _max.Z; z++)
                        writer.Write(_blockIds[x, y, z]);
        }

        public void Dispose()
        {
            lock (this)
            {
                _vao.Dispose();
            }
        }

        private void AddBlockToVao()
        {
            for (int x = _min.X; x <= _max.X; x++)
                for (int y = _min.Y; y <= _max.Y; y++)
                    for (int z = _min.Z; z <= _max.Z; z++)
                    {
                        if (_interrupted) return;
                        VaoHelper.AddBlockToVao(
                            _world,
                            Position * Size + new Vector3i(x, y, z),
                            x, y, z,
                            _blockIds[x, y, z],
                            _vao
                        );
                    }
        }

        public bool IsEmpty() => _vao.UploadedCount == 0;
    }
}