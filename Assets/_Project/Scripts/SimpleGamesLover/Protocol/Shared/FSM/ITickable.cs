namespace SGL.Protocol.Shared.FSM
{
    /// <summary>
    /// Per-frame update contract. Independent of IState — applicable beyond FSM.
    /// </summary>
    public interface ITickable
    {
        /// <summary>Called every frame by the owner (e.g. FixedUpdate).</summary>
        void Tick(float deltaTime);
    }
}
