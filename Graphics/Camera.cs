using OpenTK.Mathematics;

namespace MazeEngine.Graphics
{
    internal class Camera
    {
        public Vector3 Position { get; private set; }
        public Vector3 Front { get; private set; } = -Vector3.UnitZ;
        public Vector3 Up { get; private set; } = Vector3.UnitY;
        public Vector3 Right { get; private set; } = Vector3.UnitX;
        public float Yaw { get; private set; } = -90f; // Inicialmente olhando para o eixo -Z
        public float Pitch { get; private set; } = 0f;

        public float Sensitivity { get; set; } = 0.1f;

        public Camera(Vector3 position)
        {
            Position = position;
            UpdateCameraVectors();
        }

        public Matrix4 GetViewMatrix() => Matrix4.LookAt(Position, Position + Front, Up);

        public void ProcessMouseMovement(float deltaX, float deltaY)
        {
            Yaw += deltaX * Sensitivity;
            Pitch -= deltaY * Sensitivity;

            // Limita o ângulo de pitch para evitar a inversão da câmera
            Pitch = MathHelper.Clamp(Pitch, -89f, 89f);

            UpdateCameraVectors();
        }

        public void ProcessKeyboard(Vector3 direction, float deltaTime, float speed)
        {
            Position += direction * speed * deltaTime;
        }

        private void UpdateCameraVectors()
        {
            var front = new Vector3(
                MathF.Cos(MathHelper.DegreesToRadians(Yaw)) * MathF.Cos(MathHelper.DegreesToRadians(Pitch)),
                MathF.Sin(MathHelper.DegreesToRadians(Pitch)),
                MathF.Sin(MathHelper.DegreesToRadians(Yaw)) * MathF.Cos(MathHelper.DegreesToRadians(Pitch))
            );
            Front = front.Normalized();
            Right = Vector3.Cross(Front, Vector3.UnitY).Normalized();
            Up = Vector3.Cross(Right, Front).Normalized();
        }
    }
}