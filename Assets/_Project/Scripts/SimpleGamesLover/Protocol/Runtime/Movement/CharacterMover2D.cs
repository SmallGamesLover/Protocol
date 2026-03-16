using UnityEngine;

namespace SGL.Protocol.Runtime.Movement
{
    /// <summary>
    /// Controls character movement via a hierarchical FSM.
    /// Input-agnostic: driven by PlayerInputReader or AI.
    /// </summary>
    public class CharacterMover2D : MonoBehaviour
    {
        /// <summary>
        /// Sets horizontal (and optionally vertical) movement direction.
        /// WalkingState uses only direction.x; FlyingState may use both.
        /// </summary>
        public void Move(Vector2 direction)
        {
        }

        /// <summary>
        /// Requests a jump. Handled by the current FSM state.
        /// </summary>
        public void Jump()
        {
        }

        /// <summary>
        /// Requests a dodge in the given direction.
        /// </summary>
        public void Dodge(Vector2 direction)
        {
        }
    }
}
