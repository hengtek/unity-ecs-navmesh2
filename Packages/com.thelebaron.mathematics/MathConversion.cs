using Unity.Mathematics;
using UnityEngine;

namespace thelebaron.mathematics
{
    public static class ColorMathConversion
    {
        public static float4 ToFloat4(this Color color)
        {
            return new float4(color.r, color.b, color.g, color.a);
        }
    }

}