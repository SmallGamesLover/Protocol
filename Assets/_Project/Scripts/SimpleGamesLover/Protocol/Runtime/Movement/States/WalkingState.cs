using SGL.Protocol.Shared.FSM;

namespace SGL.Protocol.Runtime.Movement.States
{
    /// <summary>
    /// Top-level state: covers all ground and airborne movement.
    /// Owns an internal sub-state FSM (Idle / Walk / Run — Jump / Fall added in Phase 3).
    /// </summary>
    public class WalkingState : IState, ITickable
    {
        private readonly StateMachine<IState> _subFsm = new();
        private readonly CharacterMover2D _mover;

        private readonly IdleSubState _idle;
        private readonly WalkSubState _walk;
        private readonly RunSubState _run;

        public WalkingState(CharacterMover2D mover, WalkingConfig config)
        {
            _mover = mover;

            _idle = new IdleSubState(mover, config);
            _walk = new WalkSubState(mover, config);
            _run  = new RunSubState(mover, config);

            // Idle ↔ Walk
            _subFsm.AddTransition(_idle, _walk, () => _mover.HorizontalInput != 0f && !_mover.IsRunRequested);
            _subFsm.AddTransition(_walk, _idle, () => _mover.HorizontalInput == 0f);

            // Walk ↔ Run
            _subFsm.AddTransition(_walk, _run, () => _mover.IsRunRequested && _mover.HorizontalInput != 0f);
            _subFsm.AddTransition(_run,  _walk, () => !_mover.IsRunRequested && _mover.HorizontalInput != 0f);

            // Idle ← Run (Shift released + input dropped while running)
            _subFsm.AddTransition(_run, _idle, () => _mover.HorizontalInput == 0f);

            // Idle → Run (direct: was idle, Shift held, input pressed)
            _subFsm.AddTransition(_idle, _run, () => _mover.HorizontalInput != 0f && _mover.IsRunRequested);
        }

        /// <summary>Enters the correct sub-state based on current conditions.</summary>
        public void OnEnter() => ResolveSubState();

        /// <summary>Exits the active sub-state cleanly.</summary>
        public void OnExit() => _subFsm.CurrentState.OnExit();

        /// <summary>Evaluates sub-state transitions, then ticks the active sub-state.</summary>
        public void Tick(float deltaTime)
        {
            _subFsm.EvaluateTransitions();
            (_subFsm.CurrentState as ITickable)?.Tick(deltaTime);
        }

        /// <summary>
        /// Picks the initial sub-state based on current conditions.
        /// Called on entry so that WalkingState never defaults blindly to Idle.
        /// </summary>
        private void ResolveSubState()
        {
            IState initial;

            if (_mover.HorizontalInput != 0f && _mover.IsRunRequested)
                initial = _run;
            else if (_mover.HorizontalInput != 0f)
                initial = _walk;
            else
                initial = _idle;

            _subFsm.SetInitialState(initial);
        }
    }
}
