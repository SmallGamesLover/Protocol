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
        [SerializeField] private DodgeConfig _dodgeConfig;

        [SerializeField] private Vector2 GroundCheckSize = new Vector2(0.9f, 0.05f);
        [SerializeField] private Vector2 GroundCheckOffset = new Vector2(0f, 0f);
        [SerializeField] private Vector2 CeilingCheckSize = new Vector2(0.9f, 0.05f);
        [SerializeField] private Vector2 CeilingCheckOffset = new Vector2(0f, 0f);
        [SerializeField] private LayerMask GroundLayerMask;
        [SerializeField] private LayerMask CeilingLayerMask;

        private Rigidbody2D _rigidbody;
        private BoxCollider2D _boxCollider;
        private StateMachine<IState> _topFsm;
        private ContactFilter2D _contactFilter;
        private CollisionSlideResolver2D _collisionResolver;

        // Concrete references needed for transition conditions (IsFinished, etc.)
        private WalkingState _walkingState;
        private DodgeState _dodgeState;

        // One-way platform state
        private int _platformLayer;
        private Collider2D _dropThroughTarget;
        private readonly Collider2D[] _groundCheckBuffer = new Collider2D[8];
        private int _groundCheckCount;

        /// <summary>True when the character is standing on ground or a platform.</summary>
        public bool IsGrounded { get; private set; }

        public bool IsCeiling { get; private set; }

        /// <summary>Current movement velocity. Read and written by FSM sub-states.</summary>
        public Vector2 Velocity { get; set; }

        /// <summary>Horizontal axis input in the range [-1, 1]. Set by Move().</summary>
        public float HorizontalInput { get; private set; }

        /// <summary>True while the run button (Shift) is held. Set by PlayerInputReader or AI.</summary>
        public bool IsRunRequested { get; set; }

        /// <summary>Set to true by Jump(). Consumed by JumpSubState.OnEnter() via ConsumeJumpRequest().</summary>
        public bool IsJumpRequested { get; private set; }

        /// <summary>Set by Dodge(). Cleared by ConsumeDodgeRequest() inside DodgeState.OnEnter().</summary>
        public bool IsDodgeRequested { get; private set; }

        /// <summary>Direction captured from Dodge() call. Read by DodgeState.OnEnter().</summary>
        public Vector2 DodgeDirection { get; private set; }

        /// <summary>True while the jump button is held. Used by JumpSubState for low-jump gravity.</summary>
        public bool IsJumpHeld { get; set; }

        /// <summary>Grace window after walking off an edge during which a jump is still available.</summary>
        public float CoyoteTimer { get; set; }

        /// <summary>Window during which a jump pressed in the air is remembered and fires on landing.</summary>
        public float JumpBufferTimer { get; set; }

        /// <summary>
        /// Composite FSM state name for the debug overlay.
        /// Returns <c>"Walking &gt; {sub}"</c> when in WalkingState, otherwise the top-level type name.
        /// Editor-only: returns <c>""</c> in builds.
        /// </summary>
        public string DebugStateName
        {
#if UNITY_EDITOR
            get => _topFsm.CurrentState == _walkingState
                ? $"Walking > {_walkingState.DebugSubStateName}"
                : _topFsm.CurrentState?.GetType().Name ?? "None";
#else
            get => "";
#endif
        }

        /// <summary>
        /// True when a drop-through is in progress. Editor-only: returns <c>false</c> in builds.
        /// Avoids exposing the private <see cref="Collider2D"/> field to the overlay.
        /// </summary>
        public bool DebugIsDropThroughActive
        {
#if UNITY_EDITOR
            get => _dropThroughTarget != null;
#else
            get => false;
#endif
        }

        // Debug
        [Header("Debug")]
        [SerializeField] private float DebugVisualScale = 0.32f;
        private Vector2 _debugDesiredDisplacement;
        private Vector2 _debugResolvedDisplacement;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody2D>();
            _boxCollider = GetComponent<BoxCollider2D>();

            _platformLayer = LayerMask.NameToLayer("Platform");

            _contactFilter = new ContactFilter2D { useLayerMask = true, layerMask = GroundLayerMask };
            _collisionResolver = new CollisionSlideResolver2D(_rigidbody);

            _walkingState = new WalkingState(this, _walkingConfig);
            _dodgeState = new DodgeState(this, _dodgeConfig);

            _topFsm = new StateMachine<IState>();
            _topFsm.AddTransition(_walkingState, _dodgeState, () => IsDodgeRequested);
            _topFsm.AddTransition(_dodgeState, _walkingState, () => _dodgeState.IsFinished);
            _topFsm.SetInitialState(_walkingState);
        }

        /// <summary>
        /// Sets horizontal (and optionally vertical) movement direction.
        /// WalkingState uses only direction.x; FlyingState may use both.
        /// </summary>
        public void Move(Vector2 direction)
        {
            HorizontalInput = direction.x;
        }

        /// <summary>Requests a jump. Sets IsJumpRequested; the FSM transition consumes it.</summary>
        public void Jump()
        {
            IsJumpRequested = true;
        }

        /// <summary>Resets IsJumpRequested. Called by JumpSubState.OnEnter() so the flag isn't re-consumed.</summary>
        public void ConsumeJumpRequest()
        {
            IsJumpRequested = false;
        }

        /// <summary>Requests a dodge in the given direction. Sets IsDodgeRequested and stores DodgeDirection.</summary>
        public void Dodge(Vector2 direction)
        {
            IsDodgeRequested = true;
            DodgeDirection = direction;
        }

        /// <summary>Clears IsDodgeRequested. Called by DodgeState.OnEnter() so the flag is consumed exactly once.</summary>
        public void ConsumeDodgeRequest()
        {
            IsDodgeRequested = false;
        }

        /// <summary>
        /// Drops through the one-way platform the character is currently standing on.
        /// No-op when not grounded or not standing on a Platform-layer collider.
        /// </summary>
        public void DropThrough()
        {
            if (!IsGrounded) return;

            for (int i = 0; i < _groundCheckCount; i++)
            {
                if (_groundCheckBuffer[i].gameObject.layer == _platformLayer)
                {
                    _dropThroughTarget = _groundCheckBuffer[i];
                    return;
                }
            }
        }

        private void FixedUpdate()
        {
            IsGrounded = CheckGround();
            IsCeiling = CheckCeiling();

            if (IsCeiling && Velocity.y > 0f)
            {
                Vector2 v = Velocity;
                v.y = 0f;
                Velocity = v;
            }

            _topFsm.EvaluateTransitions();
            (_topFsm.CurrentState as ITickable)?.Tick(Time.fixedDeltaTime);
            ApplyMovement(Time.fixedDeltaTime);

            // Positional clearing: once the character's bottom edge passes below the platform
            // top, Mechanism 2 in the predicate takes over — explicit override no longer needed.
            if (_dropThroughTarget != null)
            {
                float colliderHalfHeight = _boxCollider.size.y * 0.5f;
                float charBottom = _rigidbody.position.y - colliderHalfHeight;
                if (charBottom < _dropThroughTarget.bounds.max.y - CollisionSlideResolver2D.SKIN_WIDTH)
                    _dropThroughTarget = null;
            }
        }

        /// <summary>
        /// Returns true when the given platform hit should be ignored by CollisionSlideResolver2D.
        /// Implements the two-mechanism one-way platform logic (see GDD §One-way Platforms).
        /// </summary>
        private bool ShouldIgnorePlatformHit(RaycastHit2D hit)
        {
            if (hit.collider.gameObject.layer != _platformLayer)
                return false;

            // Mechanism 1: explicit drop-through override.
            if (hit.collider == _dropThroughTarget)
                return true;

            // Unconditional side/bottom ignore: platforms only block from above.
            if (hit.normal.y < 0.5f)
                return true;

            // Mechanism 2: positional check — character is below or passing through platform.
            float colliderHalfHeight = _boxCollider.size.y * 0.5f;
            float charBottom = _rigidbody.position.y - colliderHalfHeight;
            return charBottom < hit.collider.bounds.max.y - CollisionSlideResolver2D.SKIN_WIDTH;
        }

        private void ApplyMovement(float deltaTime)
        {
            Vector2 desired = Velocity * deltaTime;
            Vector2 resolved = _collisionResolver.CollideAndSlide(desired, _contactFilter, ShouldIgnorePlatformHit);

#if UNITY_EDITOR
            _debugDesiredDisplacement = desired * DebugVisualScale / deltaTime;
            _debugResolvedDisplacement = resolved * DebugVisualScale / deltaTime;
#endif

            _rigidbody.MovePosition(_rigidbody.position + resolved);
        }

        private bool CheckGround()
        {
            float colliderHalfHeight = _boxCollider.size.y * 0.5f;

            Vector2 origin = (Vector2)transform.position + GroundCheckOffset;
            _groundCheckCount = Physics2D.OverlapBox(origin, GroundCheckSize, 0f, _contactFilter, _groundCheckBuffer);
            float charBottom = _rigidbody.position.y - colliderHalfHeight;

            for (int i = 0; i < _groundCheckCount; i++)
            {
                Collider2D col = _groundCheckBuffer[i];

                if (col == _dropThroughTarget)
                    continue;

                // Platform-layer colliders are only valid ground when the character is on top.
                if (col.gameObject.layer == _platformLayer)
                {
                    if (charBottom < col.bounds.max.y - CollisionSlideResolver2D.SKIN_WIDTH)
                        continue;
                }

                return true;
            }

            return false;
        }

        private bool CheckCeiling()
        {
            Vector2 origin = (Vector2)transform.position + CeilingCheckOffset;
            return Physics2D.OverlapBox(origin, CeilingCheckSize, 0f, CeilingLayerMask);
        }

#if UNITY_EDITOR

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;

            // Ground check box
            Vector2 groundOrigin = (Vector2)transform.position + GroundCheckOffset;
            Gizmos.color = IsGrounded ? Color.green : Color.red;
            Gizmos.DrawWireCube(groundOrigin, GroundCheckSize);

            // Ceiling check box
            Vector2 ceilingOrigin = (Vector2)transform.position + CeilingCheckOffset;
            Gizmos.color = IsCeiling ? Color.green : Color.red;
            Gizmos.DrawWireCube(ceilingOrigin, CeilingCheckSize);

            // Collide and slide debugging
            Vector2 center = _rigidbody.position;
            Vector2 size = _boxCollider.size;
            Vector2 skinSize = size + Vector2.one * CollisionSlideResolver2D.SKIN_WIDTH * 2f;

            // Current position
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(center, size);
            Gizmos.color = new Color(0f, 1f, 0f, 0.25f);
            Gizmos.DrawWireCube(center, skinSize);

            // Desired displacement (raw velocity)
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(center, center + _debugDesiredDisplacement);
            Gizmos.DrawWireCube(center + _debugDesiredDisplacement, size);
            Gizmos.color = new Color(1f, 1f, 0f, 0.25f);
            Gizmos.DrawWireCube(center + _debugDesiredDisplacement, skinSize);

            // Resolved displacement (after CollideAndSlide)
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(center, center + _debugResolvedDisplacement);
            Gizmos.DrawWireCube(center + _debugResolvedDisplacement, size);
            Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
            Gizmos.DrawWireCube(center + _debugResolvedDisplacement, skinSize);
        }
#endif

    }
}
