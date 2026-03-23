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
            StartCoroutine(RunAllTests());
        }

        private IEnumerator RunAllTests()
        {
            yield return StartCoroutine(RunScenario());
            yield return new WaitForSeconds(1f);
            yield return StartCoroutine(RunEdgeCaseTests());
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

        /// <summary>
        /// Phase 6B — Input value edge cases.
        /// Verifies Move() magnitude clamping, direction.y isolation, and full-height jump with IsJumpHeld=true.
        /// Observe Console output during Play Mode to confirm expected behavior.
        /// </summary>
        private IEnumerator RunEdgeCaseTests()
        {
            // Test 6B-87: Move() with non-normalized input — magnitude must not affect speed
            Debug.Log("[AutoMoverTest] 6B-87: Move(5f, 0f) for 0.5s — expect WalkSpeed, NOT 5x WalkSpeed");
            float startX = transform.position.x;
            yield return WalkFor(new Vector2(5f, 0f), 0.5f);
            float observedSpeed = Mathf.Abs(transform.position.x - startX) / 0.5f;
            Debug.Log($"[AutoMoverTest] 6B-87 result: observed avg speed ~{observedSpeed:F2} u/s. " +
                      "Expected: <= WalkSpeed (default ~5 u/s). If you see ~25+ u/s, Apply() is using raw magnitude.");
            _mover.Move(Vector2.zero);
            yield return new WaitForSeconds(0.5f);

            // Test 6B-88: Move() with direction.y != 0 — vertical velocity must not change
            Debug.Log("[AutoMoverTest] 6B-88: Move(1f, 1f) — expect direction.y ignored, Velocity.y unchanged");
            float velYBefore = _mover.Velocity.y;
            // Provide the non-zero y-component input for several frames
            for (int i = 0; i < 5; i++)
            {
                _mover.Move(new Vector2(1f, 1f));
                yield return null;
            }
            float velYAfter = _mover.Velocity.y;
            Debug.Log($"[AutoMoverTest] 6B-88 result: Velocity.y before={velYBefore:F4}, after={velYAfter:F4}. " +
                      "Expected: no change caused by direction.y=1. Any delta is gravity-only (character is grounded, should be ~0).");
            _mover.Move(Vector2.zero);
            yield return new WaitForSeconds(0.5f);

            // Test 6B-89: IsJumpHeld=true permanently → LowJumpMultiplier never activates → full-height jump
            Debug.Log("[AutoMoverTest] 6B-89: IsJumpHeld=true permanently, Jump() once — expect full-height jump");
            float startY = transform.position.y;
            float maxY = startY;
            _mover.IsJumpHeld = true;
            _mover.Jump();

            yield return new WaitUntil(() => !_mover.IsGrounded);

            // Track max Y until apex (velocity.y turns negative)
            while (_mover.Velocity.y > 0f)
            {
                if (transform.position.y > maxY)
                    maxY = transform.position.y;
                yield return null;
            }

            float reachedHeight = maxY - startY;
            Debug.Log($"[AutoMoverTest] 6B-89 result: max height = {reachedHeight:F3} units. " +
                      "Expected: matches WalkingConfig.JumpHeight. IsJumpHeld=true = LowJumpMultiplier never applied. " +
                      "This is the expected default behavior for AI callers that never 'release' the button.");

            // Wait for landing, then clean up
            yield return new WaitUntil(() => _mover.IsGrounded);
            _mover.IsJumpHeld = false;
            _mover.Move(Vector2.zero);

            Debug.Log("[AutoMoverTest] 6B edge case tests complete.");
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
