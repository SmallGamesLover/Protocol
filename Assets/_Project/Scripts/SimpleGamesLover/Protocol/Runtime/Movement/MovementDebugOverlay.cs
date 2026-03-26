using UnityEngine;
#if UNITY_EDITOR
using System.Text;
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
        /// <summary>Toggle the OnGUI text dashboard. Can be controlled independently in the Inspector.</summary>
        [SerializeField] private bool ShowOverlay = true;

        /// <summary>Toggle the velocity line Gizmo in the Scene View. Can be controlled independently in the Inspector.</summary>
        [SerializeField] private bool ShowVelocityGizmo = true;

        /// <summary>Multiplier applied to the velocity vector length when drawing the Gizmo.</summary>
        [SerializeField, Range(0.01f, 5f)] private float VelocityGizmoScale = 0.5f;

        private bool _initialized;

        /// <summary>
        /// Wires the mover and config dependencies. Called by the Composition Root.
        /// </summary>
        /// <param name="mover">The CharacterMover2D this overlay will visualize.</param>
        /// <param name="walkingConfig">Used to display CoyoteTime and JumpBufferTime max values.</param>
        public void Initialize(CharacterMover2D mover, WalkingConfig walkingConfig)
        {
#if UNITY_EDITOR
            _mover = mover;
            _walkingConfig = walkingConfig;
            _initialized = true;
            // Populate lines immediately so OnGUI never reads null strings
            // (OnGUI Layout-pass can fire before the first Update).
            RebuildOverlayLines();
#endif
        }

#if UNITY_EDITOR
        private CharacterMover2D _mover;
        private WalkingConfig _walkingConfig;
        private GUIStyle _labelStyle;
        private readonly string[] _lines = { "", "", "", "" };
        private readonly StringBuilder _sb = new StringBuilder(256);

        private void Update()
        {
            if (!_initialized) return;

            // F1 — master toggle: flips both ShowOverlay and ShowVelocityGizmo simultaneously.
            if (Keyboard.current.f1Key.wasPressedThisFrame)
            {
                ShowOverlay = !ShowOverlay;
                ShowVelocityGizmo = !ShowVelocityGizmo;
            }

            // Rebuild once per frame so OnGUI (fired per event) reads cached strings.
            if (ShowOverlay)
                RebuildOverlayLines();
        }

        private void OnGUI()
        {
            if (!_initialized || !ShowOverlay || _mover == null) return;

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.normal.textColor = Color.white;
                _labelStyle.fontSize = 12;
            }

            const float lineH = 18f;
            const float pad = 6f;
            const float width = 580f;
            float height = _lines.Length * lineH + pad * 2f;
            float originX = 8f, originY = 8f;

            // Semi-transparent black background.
            Color prevColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(originX, originY, width, height), Texture2D.whiteTexture);
            GUI.color = prevColor;

            for (int i = 0; i < _lines.Length; i++)
                GUI.Label(new Rect(originX + pad, originY + pad + i * lineH, width - pad * 2f, lineH), _lines[i], _labelStyle);
        }

        // Input Y is always 0 — CharacterMover2D stores only direction.x; direction.y is reserved for FlyingState.
        private void RebuildOverlayLines()
        {
            if (_mover == null) return;

            Vector2 vel = _mover.Velocity;
            float coyoteMax = _walkingConfig != null ? _walkingConfig.CoyoteTime : 0f;
            float bufferMax = _walkingConfig != null ? _walkingConfig.JumpBufferTime : 0f;

            _sb.Clear();
            _sb.Append("[FSM]     ").Append(_mover.DebugStateName);
            _lines[0] = _sb.ToString();

            _sb.Clear();
            _sb.Append("[Physics] Vel (")
               .Append(vel.x.ToString("F1")).Append(", ").Append(vel.y.ToString("F1"))
               .Append(")  Speed ").Append(vel.magnitude.ToString("F1"))
               .Append("  Grounded ").Append(_mover.IsGrounded)
               .Append("  Ceiling ").Append(_mover.IsCeiling);
            _lines[1] = _sb.ToString();

            _sb.Clear();
            _sb.Append("[Timers]  Coyote ")
               .Append(_mover.CoyoteTimer.ToString("F2")).Append(" / ").Append(coyoteMax.ToString("F2"))
               .Append("  JumpBuffer ").Append(_mover.JumpBufferTimer.ToString("F2"))
               .Append(" / ").Append(bufferMax.ToString("F2"));
            _lines[2] = _sb.ToString();

            _sb.Clear();
            _sb.Append("[Flags]   JumpReq ").Append(_mover.IsJumpRequested)
               .Append("  JumpHeld ").Append(_mover.IsJumpHeld)
               .Append("  DodgeReq ").Append(_mover.IsDodgeRequested)
               .Append("  RunReq ").Append(_mover.IsRunRequested)
               .Append("  DropThru ").Append(_mover.DebugIsDropThroughActive)
               .Append("  InX ").Append(_mover.HorizontalInput.ToString("F1"))
               .Append("  InY 0");
            _lines[3] = _sb.ToString();
        }

        private void OnDrawGizmos()
        {
            // OnDrawGizmos is called even on disabled MonoBehaviours — guard explicitly.
            if (!enabled || !_initialized || !ShowVelocityGizmo || _mover == null) return;

            Vector2 origin = transform.position;
            Vector2 velocity = _mover.Velocity * VelocityGizmoScale;
            Vector2 resolvedVelocity = _mover.ResolvedVelocity * VelocityGizmoScale;

            if (velocity.sqrMagnitude < 0.0001f) return;
            DrawArrow(origin, velocity, Color.yellow);

            if (resolvedVelocity.sqrMagnitude < 0.0001f) return;
            DrawArrow(origin, resolvedVelocity, Color.cyan);
        }

        private static void DrawArrow(Vector2 origin, Vector2 velocity, Color color)
        {
            const float arrowSize = 0.12f;
            Gizmos.color = color;
            Vector2 tip = origin + velocity;
            Gizmos.DrawLine(origin, tip);

            Vector2 dir = velocity.normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            Gizmos.DrawLine(tip, tip - dir * arrowSize + perp * (arrowSize * 0.5f));
            Gizmos.DrawLine(tip, tip - dir * arrowSize - perp * (arrowSize * 0.5f));
        }
#endif
    }
}
