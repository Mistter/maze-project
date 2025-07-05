using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using MazeEngine.Utils;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace MazeEngine.Graphics
{
    /// <summary>
    /// Renderiza texto bitmap usando um atlas de fonte ASCII com suporte a cor.
    /// Inclui logs de debug apenas na inicialização.
    /// </summary>
    public static class TextRenderer
    {
        private const int FirstChar = 32;
        private const int CharCount = 96;       // ASCII 32..127
        private const int Columns = 16;
        private const int Rows = 6;
        private static int _charWidth;
        private static int _charHeight;

        private static int _textureId;
        private static int _vao;
        private static int _vbo;
        private static int _shaderProgram;

        private static bool _initialized = false;
        private static readonly List<float> _vertices = new List<float>(1024);

        /// <summary>
        /// Cor RGBA para desenhar o texto.
        /// </summary>
        public static Vector4 Color { get; set; } = new Vector4(1f, 1f, 1f, 1f);

        /// <summary>
        /// Inicializa o renderer: carrega atlas e configura OpenGL. Logs apenas na primeira vez.
        /// </summary>
        private static void Initialize()
        {
            Logger.Debug("[TextRenderer] Initialize() start");
            string baseDir = AppContext.BaseDirectory;
            string relativePath = System.IO.Path.Combine("Textures", "Fonts", "ascii_font.png");
            string path = System.IO.Path.Combine(baseDir, relativePath);
            Logger.Debug($"[TextRenderer] Trying font path: {path}");
            if (!System.IO.File.Exists(path))
            {
                string alt = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(baseDir, "..", "..", "..", relativePath));
                Logger.Debug($"[TextRenderer] Trying alt font path: {alt}");
                if (System.IO.File.Exists(alt))
                {
                    path = alt;
                    Logger.Debug($"[TextRenderer] Using alt path: {path}");
                }
            }
            if (!System.IO.File.Exists(path))
                throw new Exception($"Font bitmap not found: {path}");

            using (var bmp = new Bitmap(path))
            {
                _charWidth = bmp.Width / Columns;
                _charHeight = bmp.Height / Rows;
                Logger.Debug($"[TextRenderer] Bitmap size: {bmp.Width}x{bmp.Height}, char: {_charWidth}x{_charHeight}");

                BitmapData data = bmp.LockBits(
                    new Rectangle(0, 0, bmp.Width, bmp.Height),
                    ImageLockMode.ReadOnly,
                    DrawingPixelFormat.Format32bppArgb);

                _textureId = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, _textureId);
                GL.TexImage2D(
                    TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                    bmp.Width, bmp.Height, 0,
                    OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte,
                    data.Scan0);
                bmp.UnlockBits(data);
                Logger.Debug($"[TextRenderer] Texture created ID={_textureId}");

                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                GL.BindTexture(TextureTarget.Texture2D, 0);
            }

            // Vertex shader
            int vs = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vs, @"
                #version 330 core
                layout(location = 0) in vec2 aPos;
                layout(location = 1) in vec2 aTex;
                uniform mat4 uMVP;
                out vec2 vTex;
                void main() {
                    gl_Position = uMVP * vec4(aPos, 0.0, 1.0);
                    vTex = aTex;
                }");
            GL.CompileShader(vs);

            // Fragment shader with color uniform
            int fs = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fs, @"
                #version 330 core
                in vec2 vTex;
                out vec4 FragColor;
                uniform sampler2D uFont;
                uniform vec4 uColor;
                void main() {
                    vec4 sampled = texture(uFont, vTex);
                    FragColor = sampled * uColor;
                }");
            GL.CompileShader(fs);

            _shaderProgram = GL.CreateProgram();
            GL.AttachShader(_shaderProgram, vs);
            GL.AttachShader(_shaderProgram, fs);
            GL.LinkProgram(_shaderProgram);
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
            Logger.Debug($"[TextRenderer] Shader program ID={_shaderProgram}");

            // Setup VAO/VBO
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.BindVertexArray(0);
            Logger.Debug("[TextRenderer] VAO/VBO configured");

            _initialized = true;
            Logger.Debug("[TextRenderer] Initialize() end");
        }

        /// <summary>
        /// Inicia batch de texto e configura o shader.
        /// </summary>
        public static void Begin(Matrix4 mvp)
        {
            if (!_initialized) Initialize();

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Disable(EnableCap.DepthTest);

            GL.UseProgram(_shaderProgram);
            int locMVP = GL.GetUniformLocation(_shaderProgram, "uMVP");
            GL.UniformMatrix4(locMVP, false, ref mvp);
            int fontLoc = GL.GetUniformLocation(_shaderProgram, "uFont");
            GL.Uniform1(fontLoc, 0);
            int colorLoc = GL.GetUniformLocation(_shaderProgram, "uColor");
            GL.Uniform4(colorLoc, Color);

            GL.BindVertexArray(_vao);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _textureId);
        }

        /// <summary>
        /// Desenha a string na posição informada (pixels de canto superior esquerdo).
        /// </summary>
        public static void DrawString(string text, Vector2 position)
        {
            _vertices.Clear();
            float x = position.X;
            float y = position.Y;
            float texW = Columns * _charWidth;
            float texH = Rows * _charHeight;

            foreach (char c in text)
            {
                int code = c;
                if (code < FirstChar || code >= FirstChar + CharCount)
                {
                    x += _charWidth;
                    continue;
                }
                int index = code - FirstChar;
                int col = index % Columns;
                int row = index / Columns;

                float u0 = col * _charWidth / texW;
                float v0 = row * _charHeight / texH;
                float u1 = (col + 1) * _charWidth / texW;
                float v1 = (row + 1) * _charHeight / texH;

                _vertices.AddRange(new[] { x, y + _charHeight, u0, v0 });
                _vertices.AddRange(new[] { x, y, u0, v1 });
                _vertices.AddRange(new[] { x + _charWidth, y, u1, v1 });
                _vertices.AddRange(new[] { x, y + _charHeight, u0, v0 });
                _vertices.AddRange(new[] { x + _charWidth, y, u1, v1 });
                _vertices.AddRange(new[] { x + _charWidth, y + _charHeight, u1, v0 });

                x += _charWidth;
            }

            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, _vertices.Count * sizeof(float), _vertices.ToArray(), BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _vertices.Count / 4);
        }

        /// <summary>
        /// Finaliza batch de texto.
        /// </summary>
        public static void End()
        {
            GL.Enable(EnableCap.DepthTest);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }

        /// <summary>
        /// Largura de cada caractere em pixels.
        /// </summary>
        public static int CharWidth => _charWidth;

        /// <summary>
        /// Altura de cada caractere em pixels.
        /// </summary>
        public static int CharHeight => _charHeight;
    }
}