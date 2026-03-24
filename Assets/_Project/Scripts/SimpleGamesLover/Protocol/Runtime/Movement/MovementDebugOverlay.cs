using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace SGL.Protocol.Runtime.Movement
{
    /// <summary>
    /// Runtime debug visualization for <see cref="CharacterMover2D"/>.
    /// Displays an OnGUI dashboard (FSM state, physics, timers, flags) and a velocity Gizmo.
    /// The class shell exists in all builds to prevent "Missing script" errors on the GameObject.
    /// All logic is wrapped in <c>#if UNITY_EDITOR</c> — empty MonoBehaviour in builds, no overhead.
    /// </summary>
    public class MovementDebugOverlay : MonoBehaviour
    {
        [SerializeField] private CharacterMover2D _mover;

        /// <summary>
        /// WalkingConfig reference used to display CoyoteTime and JumpBufferTime max values.
        /// Assign the same asset instance used by CharacterMover2D.
        /// </summary>
        [SerializeField] private WalkingConfig WalkingConfig;

        /// <summary>Toggle the OnGUI text dashboard. Can be controlled independently in the Inspector.</summary>
        [SerializeField] public bool ShowOverlay = true;

        /// <summary>Toggle the velocity line Gizmo in the Scene View. Can be controlled independently in the Inspector.</summary>
        [SerializeField] public bool ShowVelocityGizmo = true;

        /// <summary>Multiplier applied to the velocity vector length when drawing the Gizmo.</summary>
        [SerializeField] public float VelocityGizmoScale = 0.5f;

#if UNITY_EDITOR
        private GUIStyle _labelStyle;

        private void Awake()
        {
            if (_mover == null)
                _mover = GetComponent<CharacterMover2D>();
        }

        private void Update()
        {
            // F1 — master toggle: flips both ShowOverlay and ShowVelocityGizmo simultaneously.
            if (Keyboard.current.f1Key.wasPressedThisFrame)
            {
                ShowOverlay = !ShowOverlay;
                ShowVelocityGizmo = !ShowVelocityGizmo;
            }
        }

        private void OnGUI()
        {
            if (!ShowOverlay || _mover == null) return;

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.normal.textColor = Color.white;
                _labelStyle.fontSize = 12;
            }

            Vector2 vel = _mover.Velocity;
            float coyoteMax = WalkingConfig != null ? WalkingConfig.CoyoteTime : 0f;
            float bufferMax = WalkingConfig != null ? WalkingConfig.JumpBufferTime : 0f;

            // Input Y is always 0 — CharacterMover2D stores only direction.x; direction.y is reserved for FlyingState.
            string[] lines =
            {
                $"[FSM]     {_mover.DebugStateName}",
                $"[Physics] Vel ({vel.x:F1}, {vel.y:F1})  Speed {vel.magnitude:F1}  Grounded {_mover.IsGrounded}  Ceiling {_mover.IsCeiling}",
                $"[Timers]  Coyote {_mover.CoyoteTimer:F2} / {coyoteMax:F2}  JumpBuffer {_mover.JumpBufferTimer:F2} / {bufferMax:F2}",
                $"[Flags]   JumpReq {_mover.IsJumpRequested}  JumpHeld {_mover.IsJumpHeld}  DodgeReq {_mover.IsDodgeRequested}  RunReq {_mover.IsRunRequested}  DropThru {_mover.DebugIsDropThroughActive}  InX {_mover.HorizontalInput:F1}  InY 0",
            };

            const float lineH = 18f;
            const float pad = 6f;
            const float width = 580f;
            float height = lines.Length * lineH + pad * 2f;
            float originX = 8f, originY = 8f;

            // Semi-transparent black background.
            Color prevColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(originX, originY, width, height), Texture2D.whiteTexture);
            GUI.color = prevColor;

            for (int i = 0; i < lines.Length; i++)
                GUI.Label(new Rect(originX + pad, originY + pad + i * lineH, width - pad * 2f, lineH), lines[i], _labelStyle);
        }

        private void OnDrawGizmos()
        {
            // OnDrawGizmos is called even on disabled MonoBehaviours — guard explicitly.
            if (!enabled || !ShowVelocityGizmo || _mover == null) return;

            Vector2 origin = transform.position;
            Vector2 velocity = _mover.Velocity * VelocityGizmoScale;
            Vector2 resolvedVelocity = _mover.ResolvedVelocity * VelocityGizmoScale;

            if (velocity.sqrMagnitude < 0.0001f) return;

            Gizmos.color = Color.yellow;
            Vector2 tip = origin + velocity;
            Gizmos.DrawLine(origin, tip);

            // Arrowhead: two short lines angled back from the tip.
            Vector2 dir = velocity.normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            const float arrowSize = 0.12f;
            Gizmos.DrawLine(tip, tip - dir * arrowSize + perp * (arrowSize * 0.5f));
            Gizmos.DrawLine(tip, tip - dir * arrowSize - perp * (arrowSize * 0.5f));

            if (resolvedVelocity.sqrMagnitude < 0.0001f) return;

            Gizmos.color = Color.cyan;
            tip = origin + resolvedVelocity;
            Gizmos.DrawLine(origin, tip);

            // Arrowhead: two short lines angled back from the tip.
            dir = resolvedVelocity.normalized;
            perp = new Vector2(-dir.y, dir.x);
            Gizmos.DrawLine(tip, tip - dir * arrowSize + perp * (arrowSize * 0.5f));
            Gizmos.DrawLine(tip, tip - dir * arrowSize - perp * (arrowSize * 0.5f));
        }
#endif
    }
}
