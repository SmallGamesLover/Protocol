using UnityEngine;
using SGL.Protocol.Shared.FSM;

namespace SGL.Protocol.Runtime.Movement.States
{
    /// <summary>
    /// Ground sub-state: horizontal input present, Shift held.
    /// Accelerates velocity.x toward RunSpeed; uses Deceleration when input opposes current velocity.
    /// </summary>
    public class RunSubState : IState, ITickable
    {
        private readonly CharacterMover2D _mover;
        private readonly WalkingConfig _config;

        public RunSubState(CharacterMover2D mover, WalkingConfig config)
        {
            _mover = mover;
            _config = config;
        }

        public void OnEnter() { }
        public void OnExit() { }

        /// <summary>Accelerates toward RunSpeed in the input direction.</summary>
        public void Tick(float deltaTime)
        {
            Vector2 v = _mover.Velocity;
            v.x = _config.GroundRunParams.Apply(v.x, _mover.HorizontalInput, deltaTime);
            _mover.Velocity = v;
        }
    }
}
