using OpenTK.Mathematics; // Para compatibilidade com Vector3

namespace MazeEngine.Utils
{
    internal struct Vector3i : IEquatable<Vector3i>
    {
        public static Vector3i operator +(Vector3i v0, Vector3i v1) => new Vector3i(v0.X + v1.X, v0.Y + v1.Y, v0.Z + v1.Z);
        public static Vector3i operator -(Vector3i v0, Vector3i v1) => new Vector3i(v0.X - v1.X, v0.Y - v1.Y, v0.Z - v1.Z);
        public static Vector3i operator *(Vector3i v, int scalar) => new Vector3i(v.X * scalar, v.Y * scalar, v.Z * scalar);
        public static bool operator ==(Vector3i v0, Vector3i v1) => v0.Equals(v1);
        public static bool operator !=(Vector3i v0, Vector3i v1) => !v0.Equals(v1);

        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }

        public Vector3i(int i) : this(i, i, i)
        {
        }

        public Vector3i(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public int LengthSquared => X * X + Y * Y + Z * Z;

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + X;
                hash = hash * 31 + Y;
                hash = hash * 31 + Z;
                return hash;
            }
        }

        public override string ToString() => $"X: {X}, Y: {Y}, Z: {Z}";

        public override bool Equals(object obj)
        {
            if (obj is Vector3i other)
                return Equals(other);
            return false;
        }

        public bool Equals(Vector3i other) => X == other.X && Y == other.Y && Z == other.Z;
    }

    internal static class Vector3iExtensions
    {
        public static Vector3i ToVector3i(this Vector3 v)
            => new Vector3i((int)Math.Floor(v.X), (int)Math.Floor(v.Y), (int)Math.Floor(v.Z));

        public static Vector3 ToVector3(this Vector3i v)
            => new Vector3(v.X, v.Y, v.Z);
    }
}