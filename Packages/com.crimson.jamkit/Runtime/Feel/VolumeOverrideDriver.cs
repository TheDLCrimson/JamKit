using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace JamKit
{
    /// <summary>
    /// Drives one float-valued override parameter of a URP/HDRP <see cref="Volume"/> between a min and a max in
    /// response to a 0..1 target, using framerate-independent smoothing. Call <see cref="Initialize{T}"/> once
    /// (naming the <see cref="VolumeComponent"/> and the float parameter to drive), then <see cref="SetTarget01"/>
    /// whenever the driving value changes.
    /// </summary>
    /// <remarks>
    /// Generalized from ProjectFPS's <c>StanceVignette</c>. Two things are deliberate and honest about the scope:
    /// <list type="bullet">
    /// <item><b>Float parameters only.</b> It drives a single <see cref="VolumeParameter{T}"/> of <c>float</c>
    /// (the base of <c>FloatParameter</c>/<c>ClampedFloatParameter</c>). It is not a general multi-parameter or
    /// non-float Volume adapter, and it uses no reflection - the parameter is chosen by a typed selector
    /// delegate the caller supplies (e.g. <c>v => v.intensity</c>).</item>
    /// <item><b>Runtime-instantiated profile.</b> <see cref="Initialize{T}"/> instantiates the source profile and
    /// assigns the instance to the Volume, so it never mutates the shared profile asset (the donor bug where
    /// editing at runtime persisted into the asset).</item>
    /// </list>
    /// </remarks>
    public class VolumeOverrideDriver : MonoBehaviour
    {
        private VolumeParameter<float> _parameter;
        private float _min;
        private float _max;
        private float _response;
        private float _target;

        /// <summary>True once <see cref="Initialize{T}"/> has resolved a parameter to drive.</summary>
        public bool IsInitialized => _parameter != null;

        /// <summary>
        /// Instantiate <paramref name="volume"/>'s profile at runtime, ensure a <typeparamref name="T"/> override
        /// exists on it, and select the float parameter of it to drive via <paramref name="selector"/>. The
        /// driven value is initialized to <paramref name="min"/>.
        /// </summary>
        /// <typeparam name="T">The VolumeComponent that owns the float parameter (e.g. Vignette).</typeparam>
        /// <param name="volume">The Volume whose profile will be replaced with a runtime instance.</param>
        /// <param name="selector">Returns the float parameter to drive from the component (e.g. <c>v => v.intensity</c>).</param>
        /// <param name="min">Value at target 0.</param>
        /// <param name="max">Value at target 1.</param>
        /// <param name="response">Smoothing rate for the 1 - exp(-response*dt) approach.</param>
        public void Initialize<T>(Volume volume, Func<T, VolumeParameter<float>> selector, float min, float max, float response) where T : VolumeComponent, new()
        {
            if (volume == null) { Debug.LogError("VolumeOverrideDriver.Initialize: volume is null."); return; }
            if (selector == null) { Debug.LogError("VolumeOverrideDriver.Initialize: selector is null."); return; }

            _min = min;
            _max = max;
            _response = response;
            _target = min;

            // Instantiate so the shared profile asset is never mutated at runtime (the StanceVignette bug).
            VolumeProfile sourceProfile = volume.sharedProfile != null ? volume.sharedProfile : volume.profile;
            VolumeProfile runtimeProfile = sourceProfile != null ? Instantiate(sourceProfile) : ScriptableObject.CreateInstance<VolumeProfile>();
            volume.profile = runtimeProfile;

            if (!runtimeProfile.TryGet(out T component))
            {
                component = runtimeProfile.Add<T>();
            }

            _parameter = selector(component);

            if (_parameter == null)
            {
                Debug.LogError($"VolumeOverrideDriver.Initialize: selector returned null for {typeof(T).Name}.");
                return;
            }

            _parameter.overrideState = true;
            _parameter.value = min;
        }

        /// <summary>Set the 0..1 target that maps linearly to [min, max]. Clamped.</summary>
        public void SetTarget01(float target01)
        {
            _target = Mathf.Lerp(_min, _max, Mathf.Clamp01(target01));
        }

        private void Update()
        {
            if (_parameter == null) return;

            _parameter.value = MathUtil.Decay(_parameter.value, _target, _response, Time.deltaTime);
        }
    }
}
