using UnityEngine;

namespace SGL.Protocol.Runtime.Utilities
{
    /// <summary>
    /// Extension methods for <see cref="UnityEngine.Vector2"/>.
    /// </summary>
    public static class Vector2Extensions
    {
        /// <summary>
        /// Projects a vector onto the axis perpendicular to the given normal.
        /// Removes the component of <paramref name="vector"/> that points along <paramref name="surfaceNormal"/>,
        /// leaving only the component that slides along the surface.
        /// <paramref name="surfaceNormal"/> must be normalized.
        /// </summary>
        public static Vector2 ProjectOnAxis(this Vector2 vector, Vector2 surfaceNormal)
        {
            return vector - Vector2.Dot(vector, surfaceNormal) * surfaceNormal;
        }
    }
}
