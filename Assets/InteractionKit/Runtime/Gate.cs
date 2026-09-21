using System.Collections.Generic;
using UnityEngine;

namespace JamKit.InteractionKit
{
    /// <summary>
    /// Counts how many of its inputs are on and turns on once enough of them are.
    /// Threshold 1 behaves as "any", threshold == input count behaves as "all", and Invert covers
    /// "not". That is why there is no mode enum: one int and one bool already span the set.
    /// </summary>
    /// <remarks>
    /// IMPORTANT - this is the one node that PULLS. Every other node pushes to its Outputs, but a
    /// Gate reads an authored input list instead. That asymmetry is deliberate and load-bearing:
    /// a Gate usually lives at scene level while its inputs live inside room prefabs, and a
    /// reference pointing OUT of a prefab does not survive being applied to the prefab asset.
    /// Reading inward works; being pushed to from inside would not. Do not "fix" this.
    /// </remarks>
    [AddComponentMenu("JamKit/Interaction/Gate")]
    public sealed class Gate : SignalNode
    {
        [Tooltip("Nodes this gate reads. Order does not matter. An empty row is a wiring mistake, not a no-op: the project validator flags it.")]
        [SerializeField] private List<SignalNode> _inputs = new List<SignalNode>();

        [Tooltip("How many inputs must be on. 1 behaves as Any; set it to the input count for All.")]
        [SerializeField] private int _threshold = 1;

        [Tooltip("Flip the result, so the gate is on while the condition is NOT met.")]
        [SerializeField] private bool _invert;

        [Tooltip("Read-only. How many inputs are currently on; updates live during play.")]
        [SerializeField] private int _satisfiedCount;

        /// <summary>How many inputs are currently on.</summary>
        public int SatisfiedCount => _satisfiedCount;

        /// <summary>How many inputs are wired, including any empty rows.</summary>
        public int InputCount => _inputs.Count;

        /// <summary>How many inputs must be on for this gate to satisfy.</summary>
        public int Threshold => _threshold;

        /// <summary>Whether the result is flipped.</summary>
        public bool Invert => _invert;

        /// <summary>The input at <paramref name="index"/>, or null for an empty row. Used by the validator and the editor.</summary>
        public SignalNode InputAt(int index) =>
            index >= 0 && index < _inputs.Count ? _inputs[index] : null;

        /// <summary>Code-built setup (demos, tests and runtime-authored rooms); in the editor set the serialized fields.</summary>
        public void Configure(int threshold, bool invert, params SignalNode[] inputs)
        {
            _threshold = threshold;
            _invert = invert;
            _inputs.Clear();
            if (inputs != null)
                _inputs.AddRange(inputs);
        }

        private void Update() => Evaluate();

        /// <summary>
        /// Recounts the inputs and applies the result. Called every frame; public so a test or a
        /// code-built room can settle a gate without waiting for a frame to elapse.
        /// </summary>
        public void Evaluate()
        {
            int count = 0;
            for (int i = 0; i < _inputs.Count; i++)
            {
                SignalNode input = _inputs[i];
                if (input != null && input != this && input.State)
                    count++;
            }

            _satisfiedCount = count;

            bool satisfied = count >= Mathf.Max(1, _threshold);
            SetState(_invert ? !satisfied : satisfied);
        }

        protected override void DrawNodeGizmos()
        {
            Gizmos.color = new Color(0.35f, 0.7f, 1f);
            for (int i = 0; i < _inputs.Count; i++)
            {
                SignalNode input = _inputs[i];
                if (input == null || input == this)
                    continue;
                Gizmos.DrawLine(input.transform.position, transform.position);
            }
        }
    }
}
