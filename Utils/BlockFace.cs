using OpenTK.Mathematics;

namespace MazeEngine.Utils
{
    internal enum BlockFace
    {
        Left,
        Right,
        Top,
        Bottom,
        Back,
        Front
    }

    internal static class BlockFaceHelper
    {
        public static readonly BlockFace[] Faces =
        {
            BlockFace.Left, BlockFace.Right, BlockFace.Top, BlockFace.Bottom, BlockFace.Front, BlockFace.Back
        };

        public static Vector3 GetNormal(this BlockFace face)
        {
            return face.GetNormali().ToVector3();
        }

        public static Vector3i GetNormali(this BlockFace face)
        {
            switch (face)
            {
                case BlockFace.Left:
                    return new Vector3i(-1, 0, 0);
                case BlockFace.Right:
                    return new Vector3i(+1, 0, 0);
                case BlockFace.Bottom:
                    return new Vector3i(0, -1, 0);
                case BlockFace.Top:
                    return new Vector3i(0, +1, 0);
                case BlockFace.Back:
                    return new Vector3i(0, 0, -1);
                case BlockFace.Front:
                    return new Vector3i(0, 0, +1);
            }

            throw new Exception("Invalid BlockFace!");
        }
    }
}
