using UnityEngine;
using SGL.Protocol.Runtime.Movement;

namespace SGL.Protocol.Runtime.Core
{
    /// <summary>
    /// Composition Root for the player entity.
    /// Owns all serialized config references and wires dependencies
    /// to player components via their Initialize() methods.
    /// </summary>
    public class PlayerCompositionRoot : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private WalkingConfig WalkingConfig;
        [SerializeField] private DodgeConfig DodgeConfig;

        private void Awake()
        {
            var mover = GetComponent<CharacterMover2D>();
            mover.Initialize(WalkingConfig, DodgeConfig);
        }
    }
}
