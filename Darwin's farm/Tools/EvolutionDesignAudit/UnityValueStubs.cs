// Additional Unity value types used by production data but not needed by the
// headless ecology audit.
namespace UnityEngine
{
    public struct Vector2Int
    {
        public int x;
        public int y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
    }
}
