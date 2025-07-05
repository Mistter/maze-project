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
        public static Camera Camera => _camera;
        private static Vector2 _lastMousePosition;
        private static bool _firstMouse = true;
        public static Vector3 Position => _camera.Position;

        public static void Initialize(Camera camera) => _camera = camera;

        public static void Update(GameWindow window, World world, ref bool isPaused, ref bool debugMode, ref bool logConsoleOpen, double deltaTime)
        {
            if (window.KeyboardState.IsKeyPressed(Keys.Escape))
            {
                isPaused = !isPaused;
                window.CursorState = isPaused ? CursorState.Normal : CursorState.Grabbed;
            }
            if (window.KeyboardState.IsKeyPressed(Keys.X)) debugMode = !debugMode;
            if (window.KeyboardState.IsKeyPressed(Keys.Y)) logConsoleOpen = !logConsoleOpen;
            if (isPaused) return;
            ProcessKeyboardInput(window.KeyboardState, world, debugMode, deltaTime);
            ProcessMouseInput(window.MouseState, world);
        }

        private static void ProcessKeyboardInput(KeyboardState keyboardState, World world, bool debugMode, double deltaTime)
        {
            var direction = Vector3.Zero;
            if (keyboardState.IsKeyDown(Keys.W)) direction += _camera.Front;
            if (keyboardState.IsKeyDown(Keys.S)) direction -= _camera.Front;
            if (keyboardState.IsKeyDown(Keys.A)) direction -= _camera.Right;
            if (keyboardState.IsKeyDown(Keys.D)) direction += _camera.Right;
            if (keyboardState.IsKeyDown(Keys.Space)) direction.Y += 1;
            if (keyboardState.IsKeyDown(Keys.LeftShift)) direction.Y -= 1;
            if (direction.LengthSquared > 0.0001f)
            {
                direction.Normalize();
                float speed = keyboardState.IsKeyDown(Keys.LeftControl) ? 15f : 8f;
                _camera.ProcessKeyboard(direction, (float)deltaTime, speed);
            }
            if (!debugMode) world.Update(_camera.Position);
        }

        private static void ProcessMouseInput(MouseState mouseState, World world)
        {
            if (_firstMouse) { _lastMousePosition = mouseState.Position; _firstMouse = false; }
            var deltaX = mouseState.X - _lastMousePosition.X;
            var deltaY = mouseState.Y - _lastMousePosition.Y;
            _lastMousePosition = mouseState.Position;
            _camera.ProcessMouseMovement(deltaX, deltaY);
            if (mouseState.IsButtonDown(MouseButton.Left)) BreakBlock(world);
            if (mouseState.IsButtonDown(MouseButton.Right)) PlaceBlock(world);
        }

        private static BlockRaytraceResult PerformRaytrace(World world) =>
            world.BlockRaytrace(_camera.Position, _camera.Front, 10.0f);

        private static void BreakBlock(World world)
        {
            var result = PerformRaytrace(world);
            if (result != null)
            {
                world.SetBlock(result.BlockPos.X, result.BlockPos.Y, result.BlockPos.Z, 0);
                world.Update(_camera.Position);
            }
        }

        private static void PlaceBlock(World world)
        {
            var result = PerformRaytrace(world);
            if (result != null)
            {
                var p = result.BlockPos + result.Face.GetNormali();
                world.SetBlock(p.X, p.Y, p.Z, 1);
                world.Update(_camera.Position);
            }
        }

        public static Matrix4 GetViewMatrix() => _camera.GetViewMatrix();
        public static void ResetMouse() => _lastMousePosition = Vector2.Zero;
    }
}
