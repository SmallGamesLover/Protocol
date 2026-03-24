using UnityEngine;

namespace SGL.Protocol.Runtime.Movement
{
    /// <summary>
    /// Value object encapsulating horizontal acceleration parameters and the shared
    /// movement formula used by all sub-states (Idle, Walk, Run, Jump, Fall).
    /// Created fresh from WalkingConfig properties — Inspector changes apply immediately.
    /// </summary>
    public readonly struct HorizontalMoveParams
    {
        /// <summary>Target horizontal speed in the input direction (units/s).</summary>
        public readonly float MaxSpeed;

        /// <summary>Rate of reaching target speed (units/s²).</summary>
        public readonly float Acceleration;

        /// <summary>Rate of slowing when input is zero or opposes velocity (units/s²).</summary>
        public readonly float Deceleration;

        public HorizontalMoveParams(float maxSpeed, float acceleration, float deceleration)
        {
            MaxSpeed = maxSpeed;
            Acceleration = acceleration;
            Deceleration = deceleration;
        }

        /// <summary>
        /// Computes new horizontal velocity. Pure function — no side effects.
        /// Passing <paramref name="input"/> = 0 targets zero speed and uses Deceleration.
        /// Opposing input (direction vs current velocity) also uses Deceleration.
        /// </summary>
        /// <param name="currentVelX">Current horizontal velocity.</param>
        /// <param name="input">Horizontal input in range [-1, 1]. Zero decelerates to stop.</param>
        /// <param name="deltaTime">Time step (seconds).</param>
        /// <returns>New horizontal velocity.</returns>
        public float Apply(float currentVelX, float input, float deltaTime)
        {
            float target = input != 0f ? MaxSpeed * Mathf.Sign(input) : 0f;

            // Use Deceleration when stopping (input == 0) or when input opposes current velocity.
            bool isSlowing = input == 0f ||
                             (currentVelX != 0f && Mathf.Sign(input) != Mathf.Sign(currentVelX));
            float rate = isSlowing ? Deceleration : Acceleration;

            return Mathf.MoveTowards(currentVelX, target, rate * deltaTime);
        }
    }
}
