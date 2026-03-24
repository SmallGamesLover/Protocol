using NUnit.Framework;
using SGL.Protocol.Shared.FSM;

namespace SGL.Protocol.Tests.Shared.FSM
{
    // NOTE: To run via Unity Test Runner (EditMode), this folder needs an
    // .asmdef with "Test Assemblies" enabled. Create it manually in the Editor:
    // Assets/_Project/Scripts/SimpleGamesLover/Protocol/Tests/ → right-click → Create > Assembly Definition

    public class StateMachineTests
    {
        // Minimal stub states that record lifecycle calls
        private class StateA : IState
        {
            public bool EnteredA;
            public bool ExitedA;

            public void OnEnter() => EnteredA = true;
            public void OnExit() => ExitedA = true;
        }

        private class StateB : IState
        {
            public bool EnteredB;

            public void OnEnter() => EnteredB = true;
            public void OnExit() { }
        }

        [Test]
        public void SetInitialState_SetsCurrentStateAndCallsOnEnter()
        {
            var fsm = new StateMachine<IState>();
            var stateA = new StateA();

            fsm.SetInitialState(stateA);

            Assert.AreEqual(stateA, fsm.CurrentState, "CurrentState should be set to the initial state");
            Assert.IsTrue(stateA.EnteredA, "StateA.OnEnter() should be called by SetInitialState");
        }

        [Test]
        public void AddTransition_DoesNotFire_WhenFromStateDoesNotMatch()
        {
            // Transition is registered from stateB, but current state is stateA.
            // EvaluateTransitions must not fire it.
            var fsm = new StateMachine<IState>();
            var stateA = new StateA();
            var stateB = new StateB();

            fsm.SetInitialState(stateA);
            fsm.AddTransition(stateB, stateA, () => true); // from wrong state

            fsm.EvaluateTransitions();

            Assert.AreEqual(stateA, fsm.CurrentState, "State should not change when From does not match CurrentState");
        }

        [Test]
        public void EvaluateTransitions_SwitchesState_WhenConditionIsTrue()
        {
            var fsm = new StateMachine<IState>();
            var stateA = new StateA();
            var stateB = new StateB();

            fsm.SetInitialState(stateA);
            fsm.AddTransition(stateA, stateB, () => true);

            fsm.EvaluateTransitions();

            Assert.AreEqual(stateB, fsm.CurrentState);
            Assert.IsTrue(stateA.ExitedA, "StateA.OnExit() should have been called");
            Assert.IsTrue(stateB.EnteredB, "StateB.OnEnter() should have been called");
        }

        [Test]
        public void EvaluateTransitions_DoesNotSwitch_WhenConditionIsFalse()
        {
            var fsm = new StateMachine<IState>();
            var stateA = new StateA();
            var stateB = new StateB();

            fsm.SetInitialState(stateA);
            fsm.AddTransition(stateA, stateB, () => false);

            fsm.EvaluateTransitions();

            Assert.AreEqual(stateA, fsm.CurrentState);
            Assert.IsFalse(stateA.ExitedA, "StateA.OnExit() should NOT have been called");
        }

        [Test]
        public void EvaluateTransitions_CallsOnExit_OnPreviousState()
        {
            var fsm = new StateMachine<IState>();
            var stateA = new StateA();
            var stateB = new StateB();

            fsm.SetInitialState(stateA);
            fsm.AddTransition(stateA, stateB, () => true);

            fsm.EvaluateTransitions();

            Assert.IsTrue(stateA.ExitedA, "OnExit() must be called on the state that is being left");
        }
    }
}
