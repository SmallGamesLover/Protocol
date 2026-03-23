using System.Collections;
using UnityEngine;

namespace SGL.Protocol.Runtime.Movement
{
    /// <summary>
    /// Temporary smoke test for Phase 6A — Full API smoke test.
    /// Drives CharacterMover2D through a scripted coroutine sequence that covers every public API method.
    /// No keyboard input required. Disable PlayerInputReader on the player before entering Play Mode.
    /// DELETE this script after Phase 6 verification is complete (task 100).
    /// </summary>
    /// <remarks>
    /// Scene requirements: a one-way platform (layer Platform) must be reachable via a jump
    /// to the right from the character's starting position, with open space below it to land on.
    /// </remarks>
    public class AutoMoverTest : MonoBehaviour
    {
        [SerializeField] private CharacterMover2D _mover;
        [SerializeField] private DodgeConfig _dodgeConfig;

        [Header("Timing")]
        /// <summary>Duration of the initial walk-right phase (seconds).</summary>
        [SerializeField] private float WalkDuration = 1f;
        /// <summary>Extra buffer added on top of DodgeTime when waiting for the dodge to finish.</summary>
        [SerializeField] private float DodgeWaitBuffer = 0.15f;
        /// <summary>Duration to walk right after the dodge in order to approach the one-way platform.</summary>
        [SerializeField] private float PlatformApproachDuration = 1.5f;

        private void Start()
        {
            StartCoroutine(RunScenario());
        }

        private IEnumerator RunScenario()
        {
            // Step 1: Walk right
            Debug.Log("[AutoMoverTest] Step 1: Move right");
            yield return WalkFor(Vector2.right, WalkDuration);

            // Step 2: Jump
            Debug.Log("[AutoMoverTest] Step 2: Jump");
            _mover.IsJumpHeld = true;
            _mover.Jump();
            yield return new WaitUntil(() => !_mover.IsGrounded);

            // Step 3: Switch to moving left mid-air
            Debug.Log("[AutoMoverTest] Step 3: Move left mid-air");
            _mover.Move(Vector2.left);

            // Step 4: Wait for landing
            yield return new WaitUntil(() => _mover.IsGrounded);
            _mover.IsJumpHeld = false;
            _mover.Move(Vector2.zero);
            Debug.Log("[AutoMoverTest] Step 4: Landed");
            yield return null;

            // Step 5: Dodge right
            Debug.Log("[AutoMoverTest] Step 5: Dodge right");
            _mover.Dodge(Vector2.right);

            // Step 6: Wait for dodge to complete
            float dodgeWait = (_dodgeConfig != null ? _dodgeConfig.DodgeTime : 0.3f) + DodgeWaitBuffer;
            yield return new WaitForSeconds(dodgeWait);
            Debug.Log("[AutoMoverTest] Step 6: Dodge complete");

            // Step 7: Jump onto a one-way platform to the right
            Debug.Log("[AutoMoverTest] Step 7: Jump onto one-way platform");
            _mover.IsJumpHeld = true;
            _mover.Jump();
            yield return null;
            yield return WalkFor(Vector2.right, PlatformApproachDuration);

            // Wait for landing on the platform
            yield return new WaitUntil(() => _mover.IsGrounded);
            _mover.IsJumpHeld = false;
            _mover.Move(Vector2.zero);
            Debug.Log("[AutoMoverTest] Step 7: Landed on platform");
            yield return null;

            // Step 8: Drop through the platform
            Debug.Log("[AutoMoverTest] Step 8: DropThrough");
            _mover.DropThrough();

            // Brief gap to allow the character to leave the platform surface
            yield return new WaitForSeconds(0.1f);

            // Wait for landing below
            yield return new WaitUntil(() => _mover.IsGrounded);
            _mover.Move(Vector2.zero);
            Debug.Log("[AutoMoverTest] Step 8: Landed below platform");

            Debug.Log("[AutoMoverTest] Scenario complete. All public API methods exercised without keyboard input.");
        }

        /// <summary>Calls Move() with the given direction each frame for the specified duration.</summary>
        private IEnumerator WalkFor(Vector2 direction, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                _mover.Move(direction);
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
    }
}
