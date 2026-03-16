namespace SGL.Protocol.Shared.FSM
{
    /// <summary>
    /// Lifecycle contract for FSM states.
    /// </summary>
    public interface IState
    {
        /// <summary>Called when this state becomes active.</summary>
        void OnEnter();

        /// <summary>Called when this state is deactivated.</summary>
        void OnExit();
    }
}
