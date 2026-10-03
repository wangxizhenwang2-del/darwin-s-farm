using System;

namespace UnityEngine
{
    public class MonoBehaviour { }
    public class ScriptableObject { }
    public class Sprite { }
    [AttributeUsage(AttributeTargets.Field)] public class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public class MinAttribute : Attribute { public MinAttribute(float value) { } }
    [AttributeUsage(AttributeTargets.Field)] public class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }
    [AttributeUsage(AttributeTargets.Field)] public class HeaderAttribute : Attribute { public HeaderAttribute(string value) { } }
    [AttributeUsage(AttributeTargets.Class)] public class CreateAssetMenuAttribute : Attribute { public string menuName; }

    public static class Mathf
    {
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Clamp(int x, int lo, int hi) => Math.Clamp(x, lo, hi);
        public static float Clamp(float x, float lo, float hi) => Math.Clamp(x, lo, hi);
        public static float Clamp01(float x) => Clamp(x, 0f, 1f);
        public static int Abs(int x) => Math.Abs(x);
        public static float Abs(float x) => Math.Abs(x);
        public static int RoundToInt(float x) => (int)MathF.Round(x, MidpointRounding.ToEven);
        public static int FloorToInt(float x) => (int)MathF.Floor(x);
        public static float Sign(float x) => x >= 0 ? 1f : -1f;
    }

    public static class Random
    {
        private static System.Random generator = new System.Random(1);
        public static void InitState(int seed) => generator = new System.Random(seed);
        public static float value => (float)generator.NextDouble();
    }
}

public class SimulationTime
{
    public event Action<int> OnDayChanged;
}
