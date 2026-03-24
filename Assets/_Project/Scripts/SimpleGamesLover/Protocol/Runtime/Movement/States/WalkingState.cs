using SGL.Protocol.Shared.FSM;

namespace SGL.Protocol.Runtime.Movement.States
{
    /// <summary>
    /// Top-level state: covers all ground and airborne movement.
    /// Owns an internal sub-state FSM (Idle / Walk / Run / Jump / Fall).
    /// </summary>
    public class WalkingState : IState, ITickable
    {
        private readonly StateMachine<IState> _subFsm = new();
        private readonly CharacterMover2D _mover;
        private readonly WalkingConfig _config;

        private readonly IdleSubState _idle;
        private readonly WalkSubState _walk;
        private readonly RunSubState _run;
        private readonly JumpSubState _jump;
        private readonly FallSubState _fall;


        /// <summary>
        /// Current sub-state type name for the debug overlay.
        /// Editor-only: returns <c>""</c> in builds.
        /// </summary>
        public string DebugSubStateName
        {
#if UNITY_EDITOR
            get => _subFsm.CurrentState?.GetType().Name ?? "None";
#else
            get => "";
#endif
        }

        public WalkingState(CharacterMover2D mover, WalkingConfig config)
        {
            _mover = mover;
            _config = config;

            _idle = new IdleSubState(mover, config);
            _walk = new WalkSubState(mover, config);
            _run = new RunSubState(mover, config);
            _jump = new JumpSubState(mover, config);
            _fall = new FallSubState(mover, config);

            RegisterTransitions();
        }

        /// <summary>Enters the correct sub-state based on current conditions.</summary>
        public void OnEnter() => ResolveSubState();

        /// <summary>Exits the active sub-state cleanly.</summary>
        public void OnExit() => _subFsm.CurrentState.OnExit();

        /// <summary>
        /// Evaluates transitions, starts coyote timer on edge walk-off,
        /// then ticks the active sub-state.
        /// </summary>
        public void Tick(float deltaTime)
        {
            var prevState = _subFsm.CurrentState;
            _subFsm.EvaluateTransitions();

            // Task 43: start coyote timer when walking off an edge.
            // Condition: just entered Fall, but NOT from Jump (that would allow double-jump via coyote).
            if (_subFsm.CurrentState == _fall && prevState != _jump && prevState != _fall)
                _mover.CoyoteTimer = _config.CoyoteTime;

            (_subFsm.CurrentState as ITickable)?.Tick(deltaTime);
        }

        private void RegisterTransitions()
        {
            // --- Ground → Jump (highest priority; checked before Fall and Walk) ---
            _subFsm.AddTransition(_idle, _jump, () => CanJump());
            _subFsm.AddTransition(_walk, _jump, () => CanJump());
            _subFsm.AddTransition(_run, _jump, () => CanJump());

            // --- Jump → Fall ---
            _subFsm.AddTransition(_jump, _fall, () => _mover.Velocity.y < 0f);

            // --- Ground → Fall (walked off edge) ---
            _subFsm.AddTransition(_idle, _fall, () => !_mover.IsGrounded);
            _subFsm.AddTransition(_walk, _fall, () => !_mover.IsGrounded);
            _subFsm.AddTransition(_run, _fall, () => !_mover.IsGrounded);

            // --- Fall → Jump (coyote time — jumped after walking off edge) ---
            _subFsm.AddTransition(_fall, _jump, () => _mover.IsJumpRequested && _mover.CoyoteTimer > 0f);

            // --- Fall → Jump (buffered jump fires on landing, checked before Idle/Walk) ---
            _subFsm.AddTransition(_fall, _jump, () => _mover.IsGrounded && _mover.JumpBufferTimer > 0f);

            // --- Fall → Idle / Walk / Run (landing, no buffer active) ---
            _subFsm.AddTransition(_fall, _idle,
                () => _mover.IsGrounded && _mover.HorizontalInput == 0f && _mover.JumpBufferTimer <= 0f);
            _subFsm.AddTransition(_fall, _walk,
                () => _mover.IsGrounded && _mover.HorizontalInput != 0f && !_mover.IsRunRequested && _mover.JumpBufferTimer <= 0f);
            _subFsm.AddTransition(_fall, _run,
                () => _mover.IsGrounded && _mover.HorizontalInput != 0f && _mover.IsRunRequested && _mover.JumpBufferTimer <= 0f);

            // --- Idle ↔ Walk ---
            _subFsm.AddTransition(_idle, _walk, () => _mover.HorizontalInput != 0f && !_mover.IsRunRequested);
            _subFsm.AddTransition(_walk, _idle, () => _mover.HorizontalInput == 0f);

            // --- Walk ↔ Run ---
            _subFsm.AddTransition(_walk, _run, () => _mover.IsRunRequested && _mover.HorizontalInput != 0f);
            _subFsm.AddTransition(_run, _walk, () => !_mover.IsRunRequested && _mover.HorizontalInput != 0f);

            // --- Run → Idle ---
            _subFsm.AddTransition(_run, _idle, () => _mover.HorizontalInput == 0f);

            // --- Idle → Run (direct: was idle, Shift held, input pressed) ---
            _subFsm.AddTransition(_idle, _run, () => _mover.HorizontalInput != 0f && _mover.IsRunRequested);
        }

        /// <summary>
        /// Jump is available when the button is pressed AND the character is
        /// grounded or still within the coyote time window.
        /// </summary>
        private bool CanJump() =>
            _mover.IsJumpRequested && (_mover.IsGrounded || _mover.CoyoteTimer > 0f);

        /// <summary>
        /// Picks the initial sub-state based on current conditions.
        /// Called on entry so that WalkingState never defaults blindly to Idle.
        /// Task 45: airborne entry resolves to FallSubState.
        /// </summary>
        private void ResolveSubState()
        {
            IState initial;

            if (!_mover.IsGrounded)
                initial = _fall;
            else if (_mover.HorizontalInput != 0f && _mover.IsRunRequested)
                initial = _run;
            else if (_mover.HorizontalInput != 0f)
                initial = _walk;
            else
                initial = _idle;

            _subFsm.SetInitialState(initial);
        }
    }
}
