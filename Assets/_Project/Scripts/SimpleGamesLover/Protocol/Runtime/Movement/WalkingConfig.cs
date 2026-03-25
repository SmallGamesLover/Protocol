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

        /// <summary>Maximum speed while walking (no Shift held).</summary>
        [field: SerializeField]
        [field: Min(0.01f)]
        public float WalkSpeed { get; private set; } = 5f;

        /// <summary>Maximum speed while running (Shift held).</summary>
        [field: SerializeField]
        [field: Min(0.01f)]
        public float RunSpeed { get; private set; } = 8f;

        /// <summary>Rate of acceleration toward target speed (units/s²).</summary>
        [field: SerializeField]
        [field: Min(0.01f)]
        public float Acceleration { get; private set; } = 50f;

        /// <summary>Rate of deceleration toward zero (units/s²). Higher than Acceleration for a sense of control.</summary>
        [field: SerializeField]
        [field: Min(0.01f)]
        public float Deceleration { get; private set; } = 70f;

        // --- Air movement ---

        /// <summary>Horizontal acceleration in the air (units/s²). Typically 30–60% of ground Acceleration.</summary>
        [field: SerializeField]
        [field: Min(0.01f)]
        public float AirAcceleration { get; private set; } = 25f;

        /// <summary>Horizontal deceleration in the air (units/s²). Low values preserve momentum.</summary>
        [field: SerializeField]
        [field: Min(0.01f)]
        public float AirDeceleration { get; private set; } = 10f;

        // --- Jump ---

        /// <summary>Desired jump height in Unity units.</summary>
        [field: SerializeField]
        [field: Min(0.01f)]
        public float JumpHeight { get; private set; } = 3f;

        /// <summary>Time in seconds to reach the jump apex.</summary>
        [field: SerializeField]
        [field: Min(0.01f)]
        public float TimeToApex { get; private set; } = 0.4f;

        /// <summary>Time in seconds to fall from the apex back to the ground.</summary>
        [field: SerializeField]
        [field: Min(0.01f)]
        public float TimeToDescent { get; private set; } = 0.6f;

        /// <summary>Gravity multiplier applied when the jump button is released before the apex.</summary>
        [field: SerializeField]
        public float LowJumpMultiplier { get; private set; } = 2f;

        /// <summary>Maximum fall speed cap (units/s).</summary>
        [field: SerializeField]
        [field: Min(0.01f)]
        public float MaxFallSpeed { get; private set; } = 15f;

        // --- Responsiveness ---

        /// <summary>Grace window after walking off an edge during which a jump is still available (seconds).</summary>
        [field: SerializeField]
        public float CoyoteTime { get; private set; } = 0.2f;

        /// <summary>Window during which a jump input pressed in the air is remembered on landing (seconds).</summary>
        [field: SerializeField]
        public float JumpBufferTime { get; private set; } = 0.1f;

        // --- Computed values ---

        /// <summary>Computed: -2 * JumpHeight / TimeToApex². Always negative.</summary>
        public float Gravity => -2f * JumpHeight / (TimeToApex * TimeToApex);

        /// <summary>Computed: 2 * JumpHeight / TimeToApex. Initial upward velocity for a jump.</summary>
        public float JumpVelocity => 2f * JumpHeight / TimeToApex;

        /// <summary>Computed: (TimeToApex / TimeToDescent)². Gravity multiplier during fall.</summary>
        public float FallMultiplier => (TimeToApex / TimeToDescent) * (TimeToApex / TimeToDescent);

        // --- HorizontalMoveParams presets ---

        /// <summary>Ground walk parameters: WalkSpeed with ground Acceleration/Deceleration. Used by IdleSubState and WalkSubState.</summary>
        public HorizontalMoveParams GroundWalkParams => new(WalkSpeed, Acceleration, Deceleration);

        /// <summary>Ground run parameters: RunSpeed with ground Acceleration/Deceleration. Used by RunSubState.</summary>
        public HorizontalMoveParams GroundRunParams => new(RunSpeed, Acceleration, Deceleration);

        /// <summary>Air parameters: RunSpeed with AirAcceleration/AirDeceleration. Used by JumpSubState and FallSubState.</summary>
        public HorizontalMoveParams AirParams => new(RunSpeed, AirAcceleration, AirDeceleration);
    }
}
