using SGL.Protocol.Shared.FSM;

namespace SGL.Protocol.Runtime.Movement.States
{
    /// <summary>
    /// Top-level state: covers all ground and airborne movement.
    /// Owns an internal sub-state FSM (Idle / Walk — Run / Jump / Fall added in later phases).
    /// </summary>
    public class WalkingState : IState, ITickable
    {
        private readonly StateMachine<IState> _subFsm = new();
        private readonly CharacterMover2D _mover;

        private readonly IdleSubState _idle;
        private readonly WalkSubState _walk;

        public WalkingState(CharacterMover2D mover, WalkingConfig config)
        {
            _mover = mover;

            _idle = new IdleSubState(mover, config);
            _walk = new WalkSubState(mover, config);

            // Task 27: Idle ↔ Walk transitions based on horizontal input.
            _subFsm.AddTransition(_idle, _walk, () => _mover.HorizontalInput != 0f);
            _subFsm.AddTransition(_walk, _idle, () => _mover.HorizontalInput == 0f);
        }

        /// <summary>Task 28: Enters the correct sub-state based on current conditions.</summary>
        public void OnEnter() => ResolveSubState();

        /// <summary>Task 30: Exits the active sub-state cleanly.</summary>
        public void OnExit() => _subFsm.CurrentState.OnExit();

        /// <summary>Task 29: Evaluates sub-state transitions, then ticks the active sub-state.</summary>
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
            IState initial = _mover.HorizontalInput != 0f ? (IState)_walk : _idle;
            _subFsm.SetInitialState(initial);
        }
    }
}
