using System.Diagnostics;

namespace thelebaron.mathematics
{
    public static class MathsValidation
    {
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void ValidateBufferLengthsAreEqual(int expected, int value)
        {
            if (expected != value)
                throw new System.InvalidOperationException($"Buffer length must match: '{expected}' and '{value}'.");
        }
    }
}