namespace MazeEngine.Utils
{
    internal class BlockRaytraceResult
    {
        public readonly BlockFace Face;
        public readonly Vector3i BlockPos;
        public readonly float Distance;
        public readonly Vector3i Point;

        public BlockRaytraceResult(BlockFace face, Vector3i blockPos, float distance, Vector3i point)
        {
            Face = face;
            BlockPos = blockPos;
            Distance = distance;
            Point = point;
        }

        public override string ToString() => $"BlockRaytraceResult (Face:{Face}, BlockPos:{BlockPos}, Distance:{Distance}, Point:${Point})";
    }
}
