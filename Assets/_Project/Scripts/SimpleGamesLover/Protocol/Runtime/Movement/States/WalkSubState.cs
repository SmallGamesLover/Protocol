using UnityEngine;
using SGL.Protocol.Shared.FSM;

namespace SGL.Protocol.Runtime.Movement.States
{
    /// <summary>
    /// Ground sub-state: horizontal input present, Shift not held.
    /// Accelerates velocity.x toward WalkSpeed; uses Deceleration when input opposes current velocity.
    /// </summary>
    public class WalkSubState : IState, ITickable
    {
        private readonly CharacterMover2D _mover;
        private readonly WalkingConfig _config;

        public WalkSubState(CharacterMover2D mover, WalkingConfig config)
        {
            _mover = mover;
            _config = config;
        }

        public void OnEnter() { }
        public void OnExit() { }

        /// <summary>Accelerates toward WalkSpeed in the input direction.</summary>
        public void Tick(float deltaTime)
        {
            float target = _config.WalkSpeed * Mathf.Sign(_mover.HorizontalInput);

            // Use Deceleration when input opposes the current velocity direction.
            bool opposing = _mover.Velocity.x != 0f &&
                            Mathf.Sign(_mover.HorizontalInput) != Mathf.Sign(_mover.Velocity.x);
            float rate = opposing ? _config.Deceleration : _config.Acceleration;

            Vector2 v = _mover.Velocity;
            v.x = Mathf.MoveTowards(v.x, target, rate * deltaTime);
            _mover.Velocity = v;
        }
    }
}
