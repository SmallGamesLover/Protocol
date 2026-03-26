using UnityEngine;

namespace SGL.Protocol.Runtime.Core
{
    /// <summary>
    /// Composition Root for the player entity.
    /// Owns all serialized config references and wires dependencies
    /// to player components via their Initialize() methods.
    /// </summary>
    public class PlayerCompositionRoot : MonoBehaviour
    {
        private void Awake()
        {
        }
    }
}
