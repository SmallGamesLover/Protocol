using UnityEngine;
using SGL.Protocol.Shared.FSM;

namespace SGL.Protocol.Runtime.Movement.States
{
    /// <summary>
    /// Ground sub-state: no horizontal input. Decelerates velocity.x toward zero each tick.
    /// </summary>
    public class IdleSubState : IState, ITickable
    {
        private readonly CharacterMover2D _mover;
        private readonly WalkingConfig _config;

        public IdleSubState(CharacterMover2D mover, WalkingConfig config)
        {
            _mover = mover;
            _config = config;
        }

        public void OnEnter() { }
        public void OnExit() { }

        /// <summary>Decelerates horizontal velocity toward zero.</summary>
        public void Tick(float deltaTime)
        {
            Vector2 v = _mover.Velocity;
            v.x = Mathf.MoveTowards(v.x, 0f, _config.Deceleration * deltaTime);
            _mover.Velocity = v;
        }
    }
}
