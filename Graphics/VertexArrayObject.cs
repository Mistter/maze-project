using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;

namespace MazeEngine.Graphics
{
    internal class VertexArrayObject : IDisposable
    {
        private readonly int _vaoId;
        private readonly int[] _bufferIds = new int[2]; // Precisamos de 2 VBOs (posições e texCoords)
        private readonly int _indicesId;                 // 1 IBO (buffer de índices)

        private int _count;  // Quantidade de índices

        private List<Vector3> _positions;
        private List<Vector3> _texCoords;
        private List<uint> _indices;

        public int UploadedCount;
        public int VertexCount => (_positions?.Count) ?? 0;
        public int IndicesCount => (_indices?.Count) ?? 0;

        public VertexArrayObject()
        {
            _vaoId = GL.GenVertexArray();
            GL.GenBuffers(_bufferIds.Length, _bufferIds);
            _indicesId = GL.GenBuffer();
        }

        public void Add(Vector3 position, Vector3 texCoord)
        {
            if (_positions == null)
            {
                _positions = new List<Vector3>(1024);
                _texCoords = new List<Vector3>(1024);
                _indices = new List<uint>(1024);
            }

            _positions.Add(position);
            _texCoords.Add(texCoord);
        }

        public void AddIndices(uint[] indices)
        {
            _indices.AddRange(indices);
        }

        public void Upload()
        {
            if (IndicesCount <= 0)
                return;

            GL.BindVertexArray(_vaoId);

            if (UploadedCount == 0)
            {
                // Posições
                GL.BindBuffer(BufferTarget.ArrayBuffer, _bufferIds[0]);
                GL.BufferData(BufferTarget.ArrayBuffer,
                              _positions.Count * Vector3.SizeInBytes,
                              _positions.ToArray(),
                              BufferUsageHint.StaticDraw);

                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);
                GL.EnableVertexAttribArray(0);

                // TexCoords
                GL.BindBuffer(BufferTarget.ArrayBuffer, _bufferIds[1]);
                GL.BufferData(BufferTarget.ArrayBuffer,
                              _texCoords.Count * Vector3.SizeInBytes,
                              _texCoords.ToArray(),
                              BufferUsageHint.StaticDraw);

                GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 0, 0);
                GL.EnableVertexAttribArray(1);
            }
            else
            {
                // Atualiza buffer de posições
                GL.BindBuffer(BufferTarget.ArrayBuffer, _bufferIds[0]);
                GL.BufferData(BufferTarget.ArrayBuffer,
                              _positions.Count * Vector3.SizeInBytes,
                              _positions.ToArray(),
                              BufferUsageHint.StaticDraw);

                // Atualiza buffer de texCoords
                GL.BindBuffer(BufferTarget.ArrayBuffer, _bufferIds[1]);
                GL.BufferData(BufferTarget.ArrayBuffer,
                              _texCoords.Count * Vector3.SizeInBytes,
                              _texCoords.ToArray(),
                              BufferUsageHint.StaticDraw);
            }

            // Indices
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, _indicesId);
            GL.BufferData(BufferTarget.ElementArrayBuffer,
                          _indices.Count * sizeof(uint),
                          _indices.ToArray(),
                          BufferUsageHint.StaticDraw);

            UploadedCount = _indices.Count;

            GL.BindVertexArray(0);
        }

        public void Clear()
        {
            _positions = null;
            _texCoords = null;
            _indices = null;
        }

        public void Draw()
        {
            if (UploadedCount <= 0) return;

            GL.BindVertexArray(_vaoId);
            GL.DrawElements(PrimitiveType.Triangles, UploadedCount, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }

        public void Dispose()
        {
            GL.DeleteBuffer(_indicesId);
            GL.DeleteBuffers(_bufferIds.Length, _bufferIds);
            GL.DeleteVertexArray(_vaoId);
        }
    }
}