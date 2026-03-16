using UnityEngine;

namespace SGL.Protocol.Runtime.Movement
{
    /// <summary>
    /// Configuration for WalkingState horizontal movement parameters.
    /// Shared across player and enemies via separate asset instances.
    /// Tweak values in the Inspector during Play Mode without recompilation.
    /// </summary>
    [CreateAssetMenu(fileName = "WalkingConfig", menuName = "Protocol/Movement/WalkingConfig")]
    public class WalkingConfig : ScriptableObject
    {
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
    }
}
