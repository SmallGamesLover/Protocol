using UnityEngine;
using SGL.Protocol.Shared.FSM;
using SGL.Protocol.Runtime.Movement.States;

namespace SGL.Protocol.Runtime.Movement
{
    /// <summary>
    /// Controls character movement via a hierarchical FSM.
    /// Input-agnostic: driven by PlayerInputReader or AI.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class CharacterMover2D : MonoBehaviour
    {
        [SerializeField] private WalkingConfig _walkingConfig;

        [SerializeField] private Vector2 GroundCheckSize = new Vector2(0.9f, 0.05f);
        [SerializeField] private Vector2 GroundCheckOffset = new Vector2(0f, 0f);
        [SerializeField] private LayerMask GroundLayerMask;

        private Rigidbody2D _rigidbody;
        private StateMachine<IState> _topFsm;

        /// <summary>True when the character is standing on ground or a platform.</summary>
        public bool IsGrounded { get; private set; }

        /// <summary>Current movement velocity. Read and written by FSM sub-states.</summary>
        public Vector2 Velocity { get; set; }

        /// <summary>Horizontal axis input in the range [-1, 1]. Set by Move().</summary>
        public float HorizontalInput { get; private set; }

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody2D>();

            var walkingState = new WalkingState(this, _walkingConfig);
            _topFsm = new StateMachine<IState>();
            _topFsm.SetInitialState(walkingState);
        }

        /// <summary>
        /// Sets horizontal (and optionally vertical) movement direction.
        /// WalkingState uses only direction.x; FlyingState may use both.
        /// </summary>
        public void Move(Vector2 direction)
        {
            HorizontalInput = direction.x;
        }

        /// <summary>Requests a jump. Handled by the current FSM state.</summary>
        public void Jump()
        {
        }

        /// <summary>Requests a dodge in the given direction.</summary>
        public void Dodge(Vector2 direction)
        {
        }

        private void FixedUpdate()
        {
            IsGrounded = CheckGround();
            _topFsm.EvaluateTransitions();
            (_topFsm.CurrentState as ITickable)?.Tick(Time.fixedDeltaTime);
            _rigidbody.MovePosition(_rigidbody.position + Velocity * Time.fixedDeltaTime);
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
