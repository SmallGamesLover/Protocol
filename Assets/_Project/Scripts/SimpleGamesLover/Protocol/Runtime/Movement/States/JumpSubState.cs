using UnityEngine;
using SGL.Protocol.Shared.FSM;

namespace SGL.Protocol.Runtime.Movement.States
{
    /// <summary>
    /// Airborne sub-state: character is moving upward after a jump.
    /// Applies gravity each tick; transitions to FallSubState when velocity.y drops below zero.
    /// </summary>
    public class JumpSubState : IState, ITickable
    {
        private readonly CharacterMover2D _mover;
        private readonly WalkingConfig _config;

        public JumpSubState(CharacterMover2D mover, WalkingConfig config)
        {
            _mover = mover;
            _config = config;
        }

        /// <summary>Sets upward velocity, consumes the jump request, and clears both timers.</summary>
        public void OnEnter()
        {
            Vector2 v = _mover.Velocity;
            v.y = _config.JumpVelocity;
            _mover.Velocity = v;

            _mover.ConsumeJumpRequest();
            _mover.JumpBufferTimer = 0f;
            _mover.CoyoteTimer = 0f;
        }

        public void OnExit() { }

        /// <summary>
        /// Applies gravity. If the jump button is released before the apex,
        /// gravity is multiplied by LowJumpMultiplier for a shorter arc.
        /// </summary>
        public void Tick(float deltaTime)
        {
            float gravity = _config.Gravity;

            // Short press: amplify gravity while still ascending
            if (!_mover.IsJumpHeld && _mover.Velocity.y > 0f)
                gravity *= _config.LowJumpMultiplier;

            Vector2 v = _mover.Velocity;
            v.y += gravity * deltaTime;
            _mover.Velocity = v;
        }
    }
}
