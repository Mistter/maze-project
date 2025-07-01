using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using MazeEngine.Graphics;
using MazeEngine.Blocks;
using MazeEngine.Utils;
using MazeEngine.Entities;
using System;
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

        private static bool _isPaused = false;
        private static bool _debugMode = false;
        private static bool _logConsoleOpen = false;

        // Global mesh
        private static VertexArrayObject _globalVao;
        private static bool _meshDirty = true;
        private static int _lastLoadedChunks = 0;

        private static int _quadVao;

        private static void Main(string[] args)
        {
            var nativeWindowSettings = new NativeWindowSettings
            {
                Size = new Vector2i(1280, 720),
                Title = "Maze Project"
            };

            _window = new GameWindow(GameWindowSettings.Default, nativeWindowSettings);
            _world = new World();

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
            _projection = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.PiOver2,
                16f / 9,
                0.01f,
                100);

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            BlockTexturesManager.Initialize();

            _uiTextures = new TextureArray(1024, 1024, 1);
            string uiTexturePath = Path.Combine("Textures", "Blocks", "pause_menu.png");
            if (!File.Exists(uiTexturePath))
                _uiTextures.SetTexture(0, new TextureData(Path.Combine("textures", "blocks", "default.png")));
            else
                _uiTextures.SetTexture(0, new TextureData(uiTexturePath));
            _uiTextures.GenerateMipmaps();

            _shader.Bind();
        }

        private static void OnResize(ResizeEventArgs e)
        {
            GL.Viewport(0, 0, e.Width, e.Height);
            _projection = Matrix4.CreatePerspectiveFieldOfView(
                MathHelper.PiOver2,
                (float)e.Width / e.Height,
                0.01f,
                100);
            _meshDirty = true;
        }

        private static void WindowOnFocusedChanged(FocusedChangedEventArgs args)
        {
            if (_window.IsFocused)
                PlayerController.ResetMouse();
        }

        private static void OnUpdateFrame(FrameEventArgs e)
        {
            PlayerController.Update(_window, _world, ref _isPaused, ref _debugMode, ref _logConsoleOpen, e.Time);
            // Mark dirty when chunk count changes
            if (_world.ChunksLoadedCount != _lastLoadedChunks)
            {
                _meshDirty = true;
                _lastLoadedChunks = _world.ChunksLoadedCount;
            }
        }

        private static void OnRenderFrame(FrameEventArgs e)
        {
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);

            GL.ClearColor(0f, 0f, 0.5f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            BlockTexturesManager.TextureArray.Bind(TextureUnit.Texture0);

            if (_isPaused)
            {
                RenderPauseMenu();
            }
            else
            {
                var view = PlayerController.GetViewMatrix();
                var projection = _projection;

                if (_meshDirty)
                {
                    _globalVao?.Dispose();
                    _globalVao = GlobalMeshGenerator.BuildMesh(_world);
                    _globalVao.Upload();
                    _meshDirty = false;
                }

                _shader.Bind();
                Matrix4 mvp = view * projection;
                _shader.SetMVP(mvp);
                _globalVao.Draw();
            }

            _window.SwapBuffers();
        }

        private static void RenderPauseMenu()
        {
            _shader.Bind();
            GL.Disable(EnableCap.DepthTest);
            Matrix4 ortho = Matrix4.CreateOrthographicOffCenter(
                0, _window.Size.X,
                _window.Size.Y, 0,
                -1.0f, 1.0f);
            _shader.SetMVP(ortho);
            _uiTextures.Bind(TextureUnit.Texture0);
            GL.BindVertexArray(_quadVao);
            GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);
            GL.Enable(EnableCap.DepthTest);
        }

        private static void OnUnload()
        {
            _world.Unload();
            _globalVao?.Dispose();
        }
    }
}