using UnityEngine;
using SGL.Protocol.Shared.FSM;

namespace SGL.Protocol.Runtime.Movement.States
{
    /// <summary>
    /// Airborne sub-state: character is moving downward or has walked off an edge.
    /// Applies amplified gravity, caps fall speed, and manages the coyote and jump buffer timers.
    /// </summary>
    public class FallSubState : IState, ITickable
    {
        private readonly CharacterMover2D _mover;
        private readonly WalkingConfig _config;

        public FallSubState(CharacterMover2D mover, WalkingConfig config)
        {
            _mover = mover;
            _config = config;
        }

        public void OnEnter() { }
        public void OnExit()
        {
            // Clear accumulated fall velocity so it doesn't carry over to grounded states.
            Vector2 v = _mover.Velocity;
            v.y = 0f;
            _mover.Velocity = v;
        }

        /// <summary>
        /// Applies fall gravity, caps velocity, and ticks both timers.
        /// Jump requests received here are stored in JumpBufferTimer for use on landing.
        /// </summary>
        public void Tick(float deltaTime)
        {
            // Capture a jump press into the buffer while airborne.
            if (_mover.IsJumpRequested)
            {
                _mover.JumpBufferTimer = _config.JumpBufferTime;
                _mover.ConsumeJumpRequest();
            }

            // Decrement both timers each tick.
            _mover.CoyoteTimer     = Mathf.Max(0f, _mover.CoyoteTimer     - deltaTime);
            _mover.JumpBufferTimer = Mathf.Max(0f, _mover.JumpBufferTimer - deltaTime);

            // Apply fall gravity and cap speed.
            Vector2 v = _mover.Velocity;
            v.y += _config.Gravity * _config.FallMultiplier * deltaTime;
            v.y  = Mathf.Max(v.y, -_config.MaxFallSpeed);
            v.x  = _config.AirParams.Apply(v.x, _mover.HorizontalInput, deltaTime);
            _mover.Velocity = v;
        }
    }
}
