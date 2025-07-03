// Program.cs
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using MazeEngine.Graphics;
using MazeEngine.Blocks;
using MazeEngine.Utils;
using MazeEngine.Entities;
using System.IO;

namespace MazeEngine
{
    internal class Program
    {
        private static GameWindow _window;
        private static Shader _shader;
        private static World _world;
        private static Matrix4 _projection;
        private static TextureArray _uiTextures;
        private static int _fpsCounter;
        private static double _fpsTimer;

        private static bool _isPaused = false;
        private static bool _debugMode = false;
        private static bool _logConsoleOpen = false;

        // Global mesh
        private static VertexArrayObject _globalVao;
        private static bool _meshDirty = true;
        private static int _lastLoadedChunks = 0;

        private static int _quadVao;
        private static Vector3 _lightDirection = new Vector3(-0.2f, -1.0f, -0.3f);

        private static void Main(string[] args)
        {
            var nativeWindowSettings = new NativeWindowSettings
            {
                ClientSize = new Vector2i(1280, 720),
                Title = "Maze Project"
            };

            _window = new GameWindow(GameWindowSettings.Default, nativeWindowSettings);
            _window.VSync = VSyncMode.Off;

            _world = new World
            {
                RenderDistance = 8
            };

            PlayerController.Initialize(new Camera(new Vector3(0f, 5f, 10f)));

            _window.Load += OnLoad;
            _window.UpdateFrame += OnUpdateFrame;
            _window.RenderFrame += OnRenderFrame;
            _window.Unload += OnUnload;
            _window.Resize += OnResize;
            _window.FocusedChanged += WindowOnFocusedChanged;

            _window.CursorState = CursorState.Grabbed;
            _shader = new Shader("shader");
            _window.Run();
        }

        private static void OnLoad()
        {
            UpdateProjection(_window.Size.X, _window.Size.Y);

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            BlockTexturesManager.Initialize();

            _uiTextures = new TextureArray(1024, 1024, 1);
            string uiTexturePath = Path.Combine("Textures", "Blocks", "pause_menu.png");
            if (!File.Exists(uiTexturePath))
            {
                Console.WriteLine($"Erro: textura de UI não encontrada: {uiTexturePath}");
                _uiTextures.SetTexture(0, new TextureData(Path.Combine("textures", "blocks", "default.png")));
            }
            else
            {
                _uiTextures.SetTexture(0, new TextureData(uiTexturePath));
            }
            _uiTextures.GenerateMipmaps();

            _shader.Bind();
            CreateQuad();
        }

        private static void OnUnload()
        {
            _world.Unload();
        }

        private static void OnResize(ResizeEventArgs e)
        {
            GL.Viewport(0, 0, e.Width, e.Height);
            UpdateProjection(e.Width, e.Height);
        }

        private static void UpdateProjection(int width, int height)
        {
            float aspect = width / (float)height;
            float farClip = 1_000_000f;
            _projection = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.PiOver2,
                aspect,
                0.01f,
                farClip
            );
        }

        private static void WindowOnFocusedChanged(FocusedChangedEventArgs args)
        {
            if (_window.IsFocused)
                PlayerController.ResetMouse();
        }

        private static void OnUpdateFrame(FrameEventArgs e)
        {
            PlayerController.Update(
                _window,
                _world,
                ref _isPaused,
                ref _debugMode,
                ref _logConsoleOpen,
                e.Time
            );
        }

        private static void OnRenderFrame(FrameEventArgs e)
        {
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);

            GL.ClearColor(0f, 0f, 0.5f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            BlockTexturesManager.TextureArray.Bind(TextureUnit.Texture0);

            if (_isPaused)
                RenderPauseMenu();
            else
                RenderGameScene();

            _window.SwapBuffers();

            _fpsCounter++;
            _fpsTimer += e.Time;
            if (_fpsTimer >= 1)
            {
                Console.WriteLine($"FPS: {_fpsCounter}");
                Console.WriteLine(
                    $"ChunksQueued: {_world.ChunksQueuedCount}, " +
                    $"ChunksReady: {_world.ChunksReadyCount}, " +
                    $"ChunksLoaded: {_world.ChunksLoadedCount}, " +
                    $"ChunkThreads: {_world.ChunkThreadsCount}"
                );
                _fpsTimer -= 1;
                _fpsCounter = 0;
            }
        }

        private static void RenderGameScene()
        {
            _shader.Bind();

            var view = PlayerController.GetViewMatrix();
            var proj = _projection;

            foreach (var entry in _world.loadedChunks)
            {
                var model = Matrix4.CreateTranslation(
                    entry.Key.X * Chunk.Size,
                    entry.Key.Y * Chunk.Size,
                    entry.Key.Z * Chunk.Size
                );
                _shader.SetMVP(model * view * proj);
                entry.Value.Draw();
            }
        }

        private static void RenderPauseMenu()
        {
            _shader.Bind();
            GL.Disable(EnableCap.DepthTest);

            var ortho = Matrix4.CreateOrthographicOffCenter(
                0, _window.Size.X,
                _window.Size.Y, 0,
                -1.0f, 1.0f
            );
            _shader.SetMVP(Matrix4.Identity * ortho);

            _uiTextures.Bind(TextureUnit.Texture0);
            GL.BindVertexArray(_quadVao);
            GL.DrawElements(
                PrimitiveType.Triangles,
                6,
                DrawElementsType.UnsignedInt,
                0
            );

            GL.Enable(EnableCap.DepthTest);
        }

        private static void CreateQuad()
        {
            float[] vertices = {
                0f, _window.Size.Y, 0f,  0f,0f,0f,  0f,0f,1f,
                0f, 0f,            0f,  0f,1f,0f,  0f,0f,1f,
                _window.Size.X, 0f,  0f,  1f,1f,0f,  0f,0f,1f,
                _window.Size.X, _window.Size.Y, 0f, 1f,0f,0f,  0f,0f,1f
            };
            uint[] indices = { 0, 1, 2, 0, 2, 3 };

            _quadVao = GL.GenVertexArray();
            GL.BindVertexArray(_quadVao);

            int vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                vertices.Length * sizeof(float),
                vertices,
                BufferUsageHint.StaticDraw
            );

            int ebo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(
                BufferTarget.ElementArrayBuffer,
                indices.Length * sizeof(uint),
                indices,
                BufferUsageHint.StaticDraw
            );

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(
                0, 3, VertexAttribPointerType.Float,
                false, 9 * sizeof(float), 0
            );

            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(
                1, 3, VertexAttribPointerType.Float,
                false, 9 * sizeof(float), 3 * sizeof(float)
            );

            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(
                2, 3, VertexAttribPointerType.Float,
                false, 9 * sizeof(float), 6 * sizeof(float)
            );

            GL.BindVertexArray(0);
        }
    }
}
