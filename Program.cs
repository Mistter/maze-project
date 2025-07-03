// Program.cs
using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using MazeEngine.Graphics;
using MazeEngine.Blocks;
using MazeEngine.Utils;
using MazeEngine.Entities;

namespace MazeEngine
{
    internal class Program
    {
        private static GameWindow _window;
        private static Shader _shader;
        private static Shader _uiShader;
        private static Shader _sky2DShader;
        private static World _world;
        private static Matrix4 _projection;
        private static TextureArray _uiTextures;
        private static Texture _crosshairTex;
        private static Texture _skyTexture;

        private static int _quadVao;
        private static int _crosshairVao;

        // novos campos para a esfera
        private static int _skySphereVao;
        private static int _skySphereVbo;
        private static int _skySphereVertexCount;

        private const int CrosshairSize = 32;
        private static int _fpsCounter;
        private static double _fpsTimer;
        private static bool _isPaused = false;
        private static bool _debugMode = false;
        private static bool _logConsoleOpen = false;

        private static void Main(string[] args)
        {
            var nativeSettings = new NativeWindowSettings()
            {
                Size = new Vector2i(1280, 720),
                Title = "Maze Project"
            };
            _window = new GameWindow(GameWindowSettings.Default, nativeSettings);
            _window.VSync = VSyncMode.Off;

            _world = new World { RenderDistance = 8 };
            PlayerController.Initialize(new Camera(new Vector3(0f, 5f, 10f)));

            _window.Load += OnLoad;
            _window.UpdateFrame += OnUpdateFrame;
            _window.RenderFrame += OnRenderFrame;
            _window.Resize += OnResize;
            _window.Unload += OnUnload;
            _window.FocusedChanged += args => { if (_window.IsFocused) PlayerController.ResetMouse(); };

            _window.CursorState = CursorState.Grabbed;
            _window.Run();
        }

        private static void OnLoad()
        {
            // texturas alinhamento e projeção
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            UpdateProjection(_window.Size.X, _window.Size.Y);

            // shaders
            _shader = new Shader("shader");
            _uiShader = new Shader("ui_shader");
            _sky2DShader = new Shader("sky_2d");

            // estados GL
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);

            // blocos/UI
            BlockTexturesManager.Initialize();
            _uiTextures = new TextureArray(1024, 1024, 1);
            string menuPath = Path.Combine("Textures", "Blocks", "pause_menu.png");
            _uiTextures.SetTexture(0,
                File.Exists(menuPath)
                    ? new TextureData(menuPath)
                    : new TextureData(Path.Combine("Textures", "Blocks", "default.png"))
            );
            _uiTextures.GenerateMipmaps();

            // crosshair
            string crossPath = Path.Combine("Textures", "UI", "crosshair.png");
            if (!File.Exists(crossPath))
                Console.WriteLine($"Erro: crosshair não encontrada em {crossPath}");
            _crosshairTex = new Texture(crossPath);

            // sky equiretangular
            string skyPath = Path.Combine("Textures", "Environment", "sky.png");
            if (!File.Exists(skyPath))
                Console.WriteLine($"Erro: sky não encontrada em {skyPath}");
            _skyTexture = new Texture(skyPath);

            // cria quads e esfera
            CreateQuad();
            CreateCrosshairQuad();
            CreateSkySphere(64, 64);
        }

        private static void OnUnload()
        {
            _world.Unload();
        }

        private static void OnResize(ResizeEventArgs e)
        {
            GL.Viewport(0, 0, e.Width, e.Height);
            UpdateProjection(e.Width, e.Height);

            if (_quadVao != 0) GL.DeleteVertexArray(_quadVao);
            if (_crosshairVao != 0) GL.DeleteVertexArray(_crosshairVao);
            if (_skySphereVao != 0) GL.DeleteVertexArray(_skySphereVao);

            CreateQuad();
            CreateCrosshairQuad();
            CreateSkySphere(64, 64);
        }

        private static void UpdateProjection(int w, int h)
        {
            float aspect = w / (float)h;
            _projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.PiOver2, aspect, 0.01f, 1_000_000f);
        }

        private static void OnUpdateFrame(FrameEventArgs e)
        {
            PlayerController.Update(_window, _world, ref _isPaused, ref _debugMode, ref _logConsoleOpen, e.Time);
        }

        private static void OnRenderFrame(FrameEventArgs e)
        {
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.ClearColor(0f, 0f, 0f, 1f);

            // 1) desenha esfera de céu com sky.png
            RenderSkySphere();

            // 2) mundo 3D ou menu de pausa
            BlockTexturesManager.TextureArray.Bind(TextureUnit.Texture0);
            if (_isPaused) RenderPauseMenu();
            else RenderGameScene();

            // 3) crosshair
            RenderCrosshair();

            _window.SwapBuffers();

            _fpsCounter++;
            _fpsTimer += e.Time;
            if (_fpsTimer >= 1)
            {
                Console.WriteLine($"FPS: {_fpsCounter}");
                _fpsCounter = 0;
                _fpsTimer -= 1;
            }
        }

        private static void RenderSkySphere()
        {
            GL.DepthMask(false);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);

            _sky2DShader.Bind();
            _sky2DShader.SetMatrix4("uProjection", _projection);

            var view = PlayerController.GetViewMatrix();
            view.Row3 = new Vector4(0, 0, 0, view.Row3.W);
            _sky2DShader.SetMatrix4("uView", view);

            GL.ActiveTexture(TextureUnit.Texture0);
            _skyTexture.Bind(TextureUnit.Texture0);
            _sky2DShader.SetInt("uSkyTexture", 0);

            GL.BindVertexArray(_skySphereVao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _skySphereVertexCount);
            GL.BindVertexArray(0);

            GL.Enable(EnableCap.CullFace);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(true);
        }

        private static void RenderGameScene()
        {
            _shader.Bind();
            var view = PlayerController.GetViewMatrix();
            foreach (var kv in _world.loadedChunks)
            {
                var pos = kv.Key;
                var model = Matrix4.CreateTranslation(pos.X * Chunk.Size, pos.Y * Chunk.Size, pos.Z * Chunk.Size);
                _shader.SetMVP(model * view * _projection);
                kv.Value.Draw();
            }
        }

        private static void RenderPauseMenu()
        {
            _shader.Bind();
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);

            var ortho = Matrix4.CreateOrthographicOffCenter(0, _window.Size.X, _window.Size.Y, 0, -1f, 1f);
            _shader.SetMVP(ortho);

            GL.ActiveTexture(TextureUnit.Texture0);
            _uiTextures.Bind(TextureUnit.Texture0);
            GL.BindVertexArray(_quadVao);
            GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedShort, 0);

            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
        }

        private static void RenderCrosshair()
        {
            _uiShader.Bind();
            _uiShader.SetInt("uTexture", 0);
            _uiShader.SetFloat("uSkyOffset", 0f);

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);

            var ortho = Matrix4.CreateOrthographicOffCenter(0, _window.Size.X, _window.Size.Y, 0, -1f, 1f);
            _uiShader.SetMVP(ortho);

            GL.ActiveTexture(TextureUnit.Texture0);
            _crosshairTex.Bind(TextureUnit.Texture0);

            GL.BindVertexArray(_crosshairVao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            GL.BindVertexArray(0);

            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.CullFace);
        }

        private static void CreateQuad()
        {
            float w = _window.Size.X, h = _window.Size.Y;
            float[] verts = {
                0f,  h, 0f,   0f,1f,  0,0,1,
                0f,  0, 0f,   0f,0f,  0,0,1,
                w,   0, 0f,   1f,0f,  0,0,1,
                w,   h, 0f,   1f,1f,  0,0,1
            };
            ushort[] idx = { 0, 1, 2, 0, 2, 3 };

            _quadVao = GL.GenVertexArray();
            GL.BindVertexArray(_quadVao);

            int vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);

            int ebo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Length * sizeof(ushort), idx, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 8 * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), 5 * sizeof(float));

            GL.BindVertexArray(0);
        }

        private static void CreateCrosshairQuad()
        {
            float s = CrosshairSize;
            float x = (_window.Size.X - s) / 2f, y = (_window.Size.Y - s) / 2f;
            float[] verts = {
                x,   y + s, 0f,  0f,1f,
                x,   y,     0f,  0f,0f,
                x+s, y,     0f,  1f,0f,
                x,   y + s, 0f,  0f,1f,
                x+s, y,     0f,  1f,0f,
                x+s, y + s, 0f,  1f,1f
            };

            _crosshairVao = GL.GenVertexArray();
            GL.BindVertexArray(_crosshairVao);

            int vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));

            GL.BindVertexArray(0);
        }

        private static void CreateSkySphere(int latBands, int longBands)
        {
            var data = new List<float>();
            for (int lat = 0; lat < latBands; lat++)
            {
                float theta1 = (float)(Math.PI * lat / latBands);
                float theta2 = (float)(Math.PI * (lat + 1) / latBands);
                for (int lon = 0; lon < longBands; lon++)
                {
                    float phi1 = (float)(2 * Math.PI * lon / longBands);
                    float phi2 = (float)(2 * Math.PI * (lon + 1) / longBands);

                    Vector3 p1 = new Vector3(
                        MathF.Sin(theta1) * MathF.Cos(phi1),
                        MathF.Cos(theta1),
                        MathF.Sin(theta1) * MathF.Sin(phi1)
                    );
                    Vector3 p2 = new Vector3(
                        MathF.Sin(theta2) * MathF.Cos(phi1),
                        MathF.Cos(theta2),
                        MathF.Sin(theta2) * MathF.Sin(phi1)
                    );
                    Vector3 p3 = new Vector3(
                        MathF.Sin(theta2) * MathF.Cos(phi2),
                        MathF.Cos(theta2),
                        MathF.Sin(theta2) * MathF.Sin(phi2)
                    );
                    Vector3 p4 = new Vector3(
                        MathF.Sin(theta1) * MathF.Cos(phi2),
                        MathF.Cos(theta1),
                        MathF.Sin(theta1) * MathF.Sin(phi2)
                    );

                    Vector2 uv1 = new Vector2(phi1 / (2 * MathF.PI), theta1 / MathF.PI);
                    Vector2 uv2 = new Vector2(phi1 / (2 * MathF.PI), theta2 / MathF.PI);
                    Vector2 uv3 = new Vector2(phi2 / (2 * MathF.PI), theta2 / MathF.PI);
                    Vector2 uv4 = new Vector2(phi2 / (2 * MathF.PI), theta1 / MathF.PI);

                    // tri 1
                    data.AddRange(new[] { p1.X, p1.Y, p1.Z, uv1.X, uv1.Y });
                    data.AddRange(new[] { p2.X, p2.Y, p2.Z, uv2.X, uv2.Y });
                    data.AddRange(new[] { p3.X, p3.Y, p3.Z, uv3.X, uv3.Y });
                    // tri 2
                    data.AddRange(new[] { p1.X, p1.Y, p1.Z, uv1.X, uv1.Y });
                    data.AddRange(new[] { p3.X, p3.Y, p3.Z, uv3.X, uv3.Y });
                    data.AddRange(new[] { p4.X, p4.Y, p4.Z, uv4.X, uv4.Y });
                }
            }

            _skySphereVertexCount = data.Count / 5;
            _skySphereVao = GL.GenVertexArray();
            _skySphereVbo = GL.GenBuffer();
            GL.BindVertexArray(_skySphereVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _skySphereVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, data.Count * sizeof(float), data.ToArray(), BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));

            GL.BindVertexArray(0);
        }
    }
}
