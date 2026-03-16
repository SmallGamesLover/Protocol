using UnityEngine;
using UnityEngine.InputSystem;

namespace SimpleGamesLover.Protocol.Runtime.Movement
{
    /// <summary>
    /// Reads raw keyboard input and forwards movement commands to CharacterMover2D.
    /// Uses UnityEngine.InputSystem (Keyboard.current) — no .inputactions asset required.
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] private CharacterMover2D _mover;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Horizontal input: A / Left = -1, D / Right = +1
            float horizontal = 0f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) horizontal += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) horizontal -= 1f;

            Vector2 direction = new Vector2(horizontal, 0f);
            _mover.Move(direction);

            // _mover.Jump();   // Uncomment in Phase 3
            // _mover.Dodge();  // Uncomment in Phase 5
        }
    }
}
