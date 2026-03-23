using UnityEngine;
using SGL.Protocol.Shared.FSM;

namespace SGL.Protocol.Runtime.Movement.States
{
    /// <summary>
    /// Top-level state: horizontal dodge with distance-based tracking.
    /// Zeroes vertical velocity on entry — airborne dodge acts as a horizontal hover.
    /// Returns to WalkingState when IsFinished is true.
    /// </summary>
    public class DodgeState : IState, ITickable
    {
        private readonly CharacterMover2D _mover;
        private readonly DodgeConfig _config;

        private float _remainingDistance;
        private float _direction;

        /// <summary>True when the dodge has covered its full distance.</summary>
        public bool IsFinished => _remainingDistance <= 0f;

        public DodgeState(CharacterMover2D mover, DodgeConfig config)
        {
            _mover = mover;
            _config = config;
        }

        /// <summary>
        /// Captures dodge direction, zeroes vertical velocity,
        /// resets remaining distance, and consumes the request flag.
        /// </summary>
        public void OnEnter()
        {
            float dirX = _mover.DodgeDirection.x;
            _direction = dirX != 0f ? Mathf.Sign(dirX) : 1f;

            _mover.ConsumeDodgeRequest();

            Vector2 v = _mover.Velocity;
            v.y = 0f;
            _mover.Velocity = v;

            _remainingDistance = _config.DodgeDistance;
        }

        /// <summary>No cleanup needed on exit.</summary>
        public void OnExit() { }

        /// <summary>
        /// Moves at DodgeSpeed each tick. On the last frame, clamps velocity so the
        /// character covers exactly the remaining distance — no overshoot.
        /// _remainingDistance is decremented by the intended step (not actual displacement)
        /// so a dodge into a wall ends on schedule rather than hovering.
        /// </summary>
        public void Tick(float deltaTime)
        {
            float maxStep = _config.DodgeSpeed * deltaTime;

            if (maxStep >= _remainingDistance)
            {
                // Last frame: cover exactly what's left.
                _mover.Velocity = new Vector2((_remainingDistance / deltaTime) * _direction, 0f);
                _remainingDistance = 0f;
            }
            else
            {
                _mover.Velocity = new Vector2(_config.DodgeSpeed * _direction, 0f);
                _remainingDistance -= maxStep;
            }
        }
    }
}
