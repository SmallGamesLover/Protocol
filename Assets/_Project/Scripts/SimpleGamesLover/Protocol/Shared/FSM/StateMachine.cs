using System;
using System.Collections.Generic;

namespace SGL.Protocol.Shared.FSM
{
    /// <summary>
    /// Generic FSM responsible only for state transitions.
    /// Ticking is the caller's responsibility — this class has no Tick method.
    /// Used at both the top level (WalkingState / DodgeState) and
    /// inside WalkingState for sub-states (Idle / Walk / Run / Jump / Fall).
    /// </summary>
    public class StateMachine<TState> where TState : class, IState
    {
        private struct Transition
        {
            public TState From;
            public TState To;
            public Func<bool> Condition;
        }

        private readonly List<Transition> _transitions = new();

        /// <summary>The currently active state. Null until SetInitialState is called.</summary>
        public TState CurrentState { get; private set; }

        /// <summary>
        /// Sets the starting state and calls its OnEnter.
        /// Must be called before EvaluateTransitions.
        /// </summary>
        public void SetInitialState(TState state)
        {
            CurrentState = state;
            state.OnEnter();
        }

        /// <summary>
        /// Registers a transition from <paramref name="from"/> to <paramref name="to"/>
        /// that fires when <paramref name="condition"/> returns true.
        /// </summary>
        public void AddTransition(TState from, TState to, Func<bool> condition)
        {
            _transitions.Add(new Transition { From = from, To = to, Condition = condition });
        }

        /// <summary>
        /// Checks all registered transitions in order.
        /// Fires the first one whose From matches CurrentState and Condition is true.
        /// Calls OnExit on the old state and OnEnter on the new state.
        /// Returns immediately after the first match.
        /// </summary>
        public void EvaluateTransitions()
        {
            foreach (var t in _transitions)
            {
                if (t.From == CurrentState && t.Condition())
                {
                    CurrentState.OnExit();
                    CurrentState = t.To;
                    CurrentState.OnEnter();
                    return;
                }
            }
        }
    }
}
