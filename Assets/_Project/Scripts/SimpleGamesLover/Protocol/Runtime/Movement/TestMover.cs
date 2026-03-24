using UnityEngine;
using UnityEngine.InputSystem;
using SGL.Protocol.Runtime.Movement;

/// <summary>
/// Minimal test controller for verifying CollideAndSlide.
/// WASD moves in all 4 directions at constant speed. No gravity, no FSM.
/// Attach to a GameObject with Kinematic Rigidbody2D + BoxCollider2D.
/// </summary>
public class TestMover : MonoBehaviour
{
    [SerializeField] private float Speed = 8f;
    [SerializeField] private LayerMask CollisionLayers;

    private Rigidbody2D _rigidbody;
    private BoxCollider2D _boxCollider;
    private ContactFilter2D _contactFilter;
    private CollisionSlideResolver2D _collisionResolver;

    // Debug
    [Header("Debug")]
    [SerializeField] private float DebugVisualScale = 0.5f;

    private Vector2 _debugDesiredVelocity;
    private Vector2 _debugResolvedVelocity;
    private bool _debugHasCollision;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody2D>();
        _boxCollider = GetComponent<BoxCollider2D>();
        _collisionResolver = new CollisionSlideResolver2D(_rigidbody);

        _contactFilter = new ContactFilter2D();
        _contactFilter.useLayerMask = true;
        _contactFilter.layerMask = CollisionLayers;
        _contactFilter.useTriggers = false;
    }

    private void FixedUpdate()
    {
        Vector2 input = ReadInput();

        _debugHasCollision = false;

        if (input == Vector2.zero) return;

        Vector2 velocity = input * Speed * Time.fixedDeltaTime;
        Vector2 resolved = _collisionResolver.CollideAndSlide(velocity, _contactFilter);

        // Debug: compare input vs output to detect collision.
        // Both are scaled to per-second so gizmos are visible and proportional.
        _debugDesiredVelocity = input * Speed;
        _debugResolvedVelocity = resolved / Time.fixedDeltaTime;
        _debugHasCollision = (resolved - velocity).sqrMagnitude > 1e-3f;

        _rigidbody.MovePosition(_rigidbody.position + resolved);
    }

    private Vector2 ReadInput()
    {
        var kb = Keyboard.current;
        if (kb == null) return Vector2.zero;

        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed) input.y += 1f;
        if (kb.sKey.isPressed) input.y -= 1f;
        if (kb.aKey.isPressed) input.x -= 1f;
        if (kb.dKey.isPressed) input.x += 1f;

        if (input.sqrMagnitude > 1f)
            input.Normalize();

        return input;
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || !_debugHasCollision)
            return;

        Vector2 origin = _rigidbody.position;
        Vector2 boxSize = _boxCollider.size * (Vector2)transform.lossyScale;
        Vector2 boxOffset = _boxCollider.offset;

        // Red: desired movement (where the character wanted to go)
        float desiredDrawLength = _debugDesiredVelocity.magnitude * DebugVisualScale;
        Vector2 desiredEnd = origin + _debugDesiredVelocity.normalized * desiredDrawLength;

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.4f);
        Gizmos.DrawLine(origin, desiredEnd);
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
        Gizmos.DrawWireCube(desiredEnd + boxOffset, boxSize);

        // Cyan: resolved movement (where the character actually goes after slide)
        if (_debugResolvedVelocity.sqrMagnitude > 1e-3f)
        {
            Vector2 resolvedDir = _debugResolvedVelocity.normalized;
            float resolvedDrawLength = _debugResolvedVelocity.magnitude * DebugVisualScale;
            Vector2 resolvedEnd = origin + resolvedDir * resolvedDrawLength;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(origin, resolvedEnd);
            Gizmos.DrawWireCube(resolvedEnd + boxOffset, boxSize);
        }
    }
}
