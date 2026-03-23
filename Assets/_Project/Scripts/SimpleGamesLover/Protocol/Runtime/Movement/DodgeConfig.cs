using UnityEngine;

namespace SGL.Protocol.Runtime.Movement
{
    /// <summary>
    /// Configuration for DodgeState. Assign in Inspector.
    /// Controls how far and how fast the dodge moves.
    /// </summary>
    [CreateAssetMenu(fileName = "DodgeConfig", menuName = "Protocol/Movement/DodgeConfig")]
    public class DodgeConfig : ScriptableObject
    {
        /// <summary>Dodge distance in world units.</summary>
        [SerializeField] public float DodgeDistance = 3f;

        /// <summary>Dodge speed in units per second.</summary>
        [SerializeField] public float DodgeSpeed = 15f;

        /// <summary>
        /// Computed dodge duration. Not used internally — for external systems
        /// (animation length, i-frames window, UI cooldown display).
        /// </summary>
        public float DodgeTime => DodgeDistance / DodgeSpeed;
    }
}
