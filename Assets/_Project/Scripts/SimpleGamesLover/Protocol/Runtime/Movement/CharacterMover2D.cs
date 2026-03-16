using UnityEngine;

namespace SGL.Protocol.Runtime.Movement
{
    /// <summary>
    /// Controls character movement via a hierarchical FSM.
    /// Input-agnostic: driven by PlayerInputReader or AI.
    /// </summary>
    public class CharacterMover2D : MonoBehaviour
    {
        [SerializeField] private Vector2 GroundCheckSize = new Vector2(0.9f, 0.05f);
        [SerializeField] private Vector2 GroundCheckOffset = new Vector2(0f, 0f);
        [SerializeField] private LayerMask GroundLayerMask;

        /// <summary>True when the character is standing on ground or a platform.</summary>
        public bool IsGrounded { get; private set; }

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

        private void FixedUpdate()
        {
            IsGrounded = CheckGround();
        }

        private bool CheckGround()
        {
            Vector2 origin = (Vector2)transform.position + GroundCheckOffset;
            return Physics2D.OverlapBox(origin, GroundCheckSize, 0f, GroundLayerMask);
        }

        private void OnDrawGizmosSelected()
        {
            Vector2 origin = (Vector2)transform.position + GroundCheckOffset;
            Gizmos.color = CheckGround() ? Color.green : Color.red;
            Gizmos.DrawWireCube(origin, GroundCheckSize);
        }
    }
}
