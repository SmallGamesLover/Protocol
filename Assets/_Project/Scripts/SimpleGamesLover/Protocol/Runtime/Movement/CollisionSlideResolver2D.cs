using System.Collections.Generic;
using UnityEngine;
using SGL.Protocol.Shared;

namespace SGL.Protocol.Runtime.Movement
{
    /// <summary>
    /// Resolves movement collisions using a recursive collide-and-slide algorithm.
    /// Does not move the rigidbody — returns a displacement vector for the caller to apply.
    /// </summary>
    public class CollisionSlideResolver2D
    {
        public const int MAX_BOUNCES = 3;
        public const float SKIN_WIDTH = 0.015f;

        private readonly List<RaycastHit2D> _hitBuffer = new List<RaycastHit2D>(16);

        private Rigidbody2D _rb;

        public CollisionSlideResolver2D(Rigidbody2D rb)
        {
            _rb = rb;
        }

        /// <summary>
        /// Entry point. Takes desired velocity for this frame, returns the resolved
        /// displacement vector that the caller should apply via MovePosition.
        /// </summary>
        public Vector2 CollideAndSlide(Vector2 velocity, ContactFilter2D contactFilter)
        {
            return CollideAndSlide(_rb.position, velocity, contactFilter);
        }

        /// <summary>
        /// Recursive collide-and-slide step.
        /// Each recursion handles one surface collision: moves to contact,
        /// projects the remainder along the surface, and recurses with the slide vector.
        /// Returns the total safe displacement accumulated across all recursion levels.
        /// </summary>
        private Vector2 CollideAndSlide(Vector2 position, Vector2 velocity, ContactFilter2D contactFilter, Vector2? previousNormal = null, int bounce = 0)
        {
            if (bounce >= MAX_BOUNCES)
                return Vector2.zero;

            // Cast distance extends beyond velocity by SKIN_WIDTH so we detect
            // surfaces that are within the skin gap ahead of us.
            float distance = velocity.magnitude + SKIN_WIDTH;
            Vector2 direction = velocity.normalized;
            if (distance < 1e-3f)
                return Vector2.zero;

            // Cast the collider from the given position along the movement direction.
            // Uses the overload that takes an explicit position so we don't need
            // MovePosition between recursion steps — the rigidbody stays in place,
            // and we pass the accumulated offset as the cast origin.
            int hitCount = _rb.Cast(position, 0f, direction, contactFilter, _hitBuffer, distance);
            if (hitCount > 0)
            {
                int closestIndex = FindClosestHitIndex(hitCount, direction);
                if (closestIndex < 0)
                    return velocity;

                RaycastHit2D hit = _hitBuffer[closestIndex];

                // Safe displacement: move up to the contact point, minus SKIN_WIDTH gap.
                // Clamped to zero — if already closer than SKIN_WIDTH, don't move backward.
                float safeMoveDist = Mathf.Max(0f, hit.distance - SKIN_WIDTH);
                Vector2 safeDisplacement = direction * safeMoveDist;

                // Remaining velocity is computed from hit.distance (the actual contact point),
                // not from safeMoveDist. SKIN_WIDTH affects where we stop, not how much
                // energy remains for sliding. This prevents remainder inflation.
                Vector2 remainingVelocity = velocity - direction * Mathf.Min(hit.distance, velocity.magnitude);

                // Project the remaining velocity onto the surface axis.
                // We preserve the original magnitude (leftOverMagnitude) instead of using
                // the projected length — this keeps slide speed constant regardless of
                // surface angle. Deceleration on slopes can be handled separately if needed.
                float leftOverMagnitude = remainingVelocity.magnitude;
                remainingVelocity = remainingVelocity.ProjectOnAxis(hit.normal).normalized;
                remainingVelocity *= leftOverMagnitude;

                // Corner check: if the slide direction goes into the surface from the
                // previous recursion step, the character is wedged between two surfaces.
                // Return only the safe portion — no further sliding is possible.
                bool wedged = previousNormal != null
                    && Vector2.Dot(remainingVelocity, previousNormal.Value) < 0f;

                //LogBounce(bounce, position, velocity, hit.distance, safeMoveDist,
                //    safeDisplacement, remainingVelocity, hit.normal, previousNormal, wedged);

                // Recurse: slide along the surface from the new position.
                // The total displacement is the safe portion plus whatever
                // the next recursion level resolves from the slide vector.
                if (wedged == false)
                    return safeDisplacement + CollideAndSlide(position + safeDisplacement, remainingVelocity, contactFilter, hit.normal, bounce + 1);
                else
                    return safeDisplacement;
            }

            // No collision — the entire velocity can be applied as displacement.
            return velocity;
        }

        /// <summary>
        /// Finds the closest hit that the character is actually moving into.
        /// Skips surfaces where dot(direction, normal) >= 0 — the character
        /// is moving away from or along them, so there is nothing to resolve.
        /// Returns -1 if no valid hit exists.
        /// </summary>
        private int FindClosestHitIndex(int hitCount, Vector2 direction)
        {
            int closestIndex = -1;
            float closestDistance = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                if (Vector2.Dot(direction, _hitBuffer[i].normal) >= 0f)
                    continue;

                if (_hitBuffer[i].distance < closestDistance)
                {
                    closestDistance = _hitBuffer[i].distance;
                    closestIndex = i;
                }
            }

            return closestIndex;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogBounce(int bounce, Vector2 position, Vector2 velocity,
    float hitDistance, float safeMoveDist, Vector2 safeDisplacement,
    Vector2 remainingVelocity, Vector2 hitNormal, Vector2? previousNormal, bool wedged)
        {
            UnityEngine.Debug.Log(
                $"[CnS] bounce={bounce} " +
                $"pos=({position.x:F4},{position.y:F4}) " +
                $"vel=({velocity.x:F4},{velocity.y:F4}) |vel|={velocity.magnitude:F4}\n" +
                $"  hit.dist={hitDistance:F4} safeDist={safeMoveDist:F4} " +
                $"normal=({hitNormal.x:F3},{hitNormal.y:F3})\n" +
                $"  safeDisp=({safeDisplacement.x:F4},{safeDisplacement.y:F4})\n" +
                $"  remainder=({remainingVelocity.x:F4},{remainingVelocity.y:F4}) " +
                $"|rem|={remainingVelocity.magnitude:F4}\n" +
                $"  prevNormal={(previousNormal.HasValue ? $"({previousNormal.Value.x:F3},{previousNormal.Value.y:F3})" : "none")} " +
                $"wedged={wedged}");
        }
    }
}
