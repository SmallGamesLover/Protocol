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
        private static int MAX_BOUNCES = 3;
        private static float SKIN_WIDTH = 0.03f;

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
        private Vector2 CollideAndSlide(Vector2 position, Vector2 velocity, ContactFilter2D contactFilter, int bounce = 0, Vector2? previousNormal = null)
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
                int closestIndex = FindClosestHitIndex(hitCount);
                RaycastHit2D hit = _hitBuffer[closestIndex];

                // Safe displacement: move up to the contact point, minus SKIN_WIDTH gap.
                Vector2 safeDistance = direction * (hit.distance - SKIN_WIDTH);

                // The portion of velocity that couldn't be applied due to the surface.
                Vector2 remainingVelocity = velocity - safeDistance;

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
                if (previousNormal != null && Vector2.Dot(remainingVelocity, previousNormal.Value) < 0f)
                    return safeDistance;

                // Recurse: slide along the surface from the new position.
                // The total displacement is the safe portion plus whatever
                // the next recursion level resolves from the slide vector.
                return safeDistance + CollideAndSlide(position + safeDistance, remainingVelocity, contactFilter, bounce + 1, hit.normal);
            }

            // No collision — the entire velocity can be applied as displacement.
            return velocity;
        }

        /// <summary>
        /// Finds the index of the closest hit in the buffer by distance.
        /// </summary>
        private int FindClosestHitIndex(int hitCount)
        {
            int closestIndex = 0;
            float closestDistance = _hitBuffer[0].distance;

            for (int i = 1; i < hitCount; i++)
            {
                if (_hitBuffer[i].distance < closestDistance)
                {
                    closestDistance = _hitBuffer[i].distance;
                    closestIndex = i;
                }
            }

            return closestIndex;
        }
    }
}
