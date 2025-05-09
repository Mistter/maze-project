using MazeEngine.Blocks;
using MazeEngine.Graphics;
using MazeEngine.Utils;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MazeEngine.Entities
{
    internal static class PlayerController
    {
        private static Camera _camera;
        private static Vector2 _lastMousePosition;
        private static bool _firstMouse = true;

        public static Vector3 Position => _camera.Position;

        public static void Initialize(Camera camera)
        {
            _camera = camera;
        }

        public static void Update(GameWindow window, World world, ref bool isPaused, ref bool debugMode, ref bool logConsoleOpen, double deltaTime)
        {
            if (window.KeyboardState.IsKeyPressed(Keys.Escape))
            {
                isPaused = !isPaused;
                window.CursorState = isPaused ? CursorState.Normal : CursorState.Grabbed;
            }

            if (window.KeyboardState.IsKeyPressed(Keys.X))
            {
                debugMode = !debugMode;
            }

            if (window.KeyboardState.IsKeyPressed(Keys.Y))
            {
                logConsoleOpen = !logConsoleOpen;
            }

            if (isPaused) return;

            ProcessKeyboardInput(window.KeyboardState, world, debugMode, deltaTime);
            ProcessMouseInput(window.MouseState, world);
        }

        private static void ProcessKeyboardInput(KeyboardState keyboardState, World world, bool debugMode, double deltaTime)
        {
            var direction = Vector3.Zero;

            if (keyboardState.IsKeyDown(Keys.W))
                direction += _camera.Front;
            if (keyboardState.IsKeyDown(Keys.S))
                direction -= _camera.Front;
            if (keyboardState.IsKeyDown(Keys.A))
                direction -= _camera.Right;
            if (keyboardState.IsKeyDown(Keys.D))
                direction += _camera.Right;
            if (keyboardState.IsKeyDown(Keys.Space))
                direction.Y += 1;
            if (keyboardState.IsKeyDown(Keys.LeftShift))
                direction.Y -= 1;

            if (direction.LengthSquared > 0.0001f)
            {
                direction.Normalize();
                float speed = keyboardState.IsKeyDown(Keys.LeftControl) ? 15f : 5f;
                _camera.ProcessKeyboard(direction, (float)deltaTime, speed);
            }

            // Se não está em modo debug, atualiza o mundo de acordo com a posição do player
            if (!debugMode)
            {
                world.Update(_camera.Position);
            }
        }

        private static void ProcessMouseInput(MouseState mouseState, World world)
        {
            if (_firstMouse)
            {
                _lastMousePosition = mouseState.Position;
                _firstMouse = false;
            }

            var deltaX = mouseState.X - _lastMousePosition.X;
            var deltaY = mouseState.Y - _lastMousePosition.Y;
            _lastMousePosition = mouseState.Position;

            _camera.ProcessMouseMovement(deltaX, deltaY);

            if (mouseState.IsButtonDown(MouseButton.Left))
            {
                var blockRaytrace = PerformRaytrace(world);
                BreakBlock(world, blockRaytrace);
            }

            if (mouseState.IsButtonDown(MouseButton.Right))
            {
                var blockRaytrace = PerformRaytrace(world);
                PlaceBlock(world, blockRaytrace);
            }
        }

        private static BlockRaytraceResult PerformRaytrace(World world)
        {
            var rayStart = _camera.Position;
            var rayDirection = _camera.Front;

            const float maxRayDistance = 10.0f;
            return world.BlockRaytrace(rayStart, rayDirection, maxRayDistance);
        }

        public static Matrix4 GetViewMatrix()
        {
            return _camera.GetViewMatrix();
        }

        public static void ResetMouse() => _lastMousePosition = Vector2.Zero;

        private static void BreakBlock(World world, BlockRaytraceResult blockRaytrace)
        {
            if (blockRaytrace != null)
            {
                world.SetBlock(blockRaytrace.BlockPos.X, blockRaytrace.BlockPos.Y, blockRaytrace.BlockPos.Z, 0);
                world.Update(_camera.Position);
            }
        }

        private static void PlaceBlock(World world, BlockRaytraceResult blockRaytrace)
        {
            if (blockRaytrace != null)
            {
                var placePos = blockRaytrace.BlockPos + blockRaytrace.Face.GetNormali();
                world.SetBlock(placePos.X, placePos.Y, placePos.Z, 1);
                world.Update(_camera.Position);
            }
        }
    }
}