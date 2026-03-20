using UnityEngine;

namespace SGL.Protocol.Runtime.Movement
{
    /// <summary>
    /// Configuration for WalkingState: horizontal movement and jump parameters.
    /// Shared across player and enemies via separate asset instances.
    /// Tweak values in the Inspector during Play Mode without recompilation.
    /// </summary>
    [CreateAssetMenu(fileName = "WalkingConfig", menuName = "Protocol/Movement/WalkingConfig")]
    public class WalkingConfig : ScriptableObject
    {
        // --- Horizontal movement ---

        [field: SerializeField]
        /// <summary>Maximum speed while walking (no Shift held).</summary>
        public float WalkSpeed { get; private set; } = 5f;

        [field: SerializeField]
        /// <summary>Maximum speed while running (Shift held).</summary>
        public float RunSpeed { get; private set; } = 8f;

        [field: SerializeField]
        /// <summary>Rate of acceleration toward target speed (units/s²).</summary>
        public float Acceleration { get; private set; } = 50f;

        [field: SerializeField]
        /// <summary>Rate of deceleration toward zero (units/s²). Higher than Acceleration for a sense of control.</summary>
        public float Deceleration { get; private set; } = 70f;

        // --- Jump ---

        [field: SerializeField]
        /// <summary>Desired jump height in Unity units.</summary>
        public float JumpHeight { get; private set; } = 3f;

        [field: SerializeField]
        /// <summary>Time in seconds to reach the jump apex.</summary>
        public float TimeToApex { get; private set; } = 0.4f;

        [field: SerializeField]
        /// <summary>Time in seconds to fall from the apex back to the ground.</summary>
        public float TimeToDescent { get; private set; } = 0.6f;

        [field: SerializeField]
        /// <summary>Gravity multiplier applied when the jump button is released before the apex.</summary>
        public float LowJumpMultiplier { get; private set; } = 2f;

        [field: SerializeField]
        /// <summary>Maximum fall speed cap (units/s).</summary>
        public float MaxFallSpeed { get; private set; } = 15f;

        // --- Responsiveness ---

        [field: SerializeField]
        /// <summary>Grace window after walking off an edge during which a jump is still available (seconds).</summary>
        public float CoyoteTime { get; private set; } = 0.1f;

        [field: SerializeField]
        /// <summary>Window during which a jump input pressed in the air is remembered on landing (seconds).</summary>
        public float JumpBufferTime { get; private set; } = 0.1f;

        // --- Computed values ---

        /// <summary>Computed: -2 * JumpHeight / TimeToApex². Always negative.</summary>
        public float Gravity => -2f * JumpHeight / (TimeToApex * TimeToApex);

        /// <summary>Computed: 2 * JumpHeight / TimeToApex. Initial upward velocity for a jump.</summary>
        public float JumpVelocity => 2f * JumpHeight / TimeToApex;

        /// <summary>Computed: (TimeToApex / TimeToDescent)². Gravity multiplier during fall.</summary>
        public float FallMultiplier => (TimeToApex / TimeToDescent) * (TimeToApex / TimeToDescent);
    }
}
