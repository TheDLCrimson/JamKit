using System;
using System.Collections;
using UnityEngine;

namespace JamKit
{
    /// <summary>
    /// Breaks this object's child hierarchy into loose rigidbody parts with an explosion impulse, an optional
    /// effect prefab, and an optional sound - a universal "any prop shatters into pieces" effect. Trigger with
    /// <see cref="Explode"/> (or the heavier, downward <see cref="Crash"/> preset).
    /// </summary>
    /// <remarks>
    /// Extracted from CarGoesAround's <c>CarExplosionHandler</c>, keeping only the hierarchy-breakup routine.
    /// The donor's collision detection, EventBus fire, and CarMovement notify are cut, and its two ~90%
    /// identical coroutines (ExplodeCarParts / CrashCarParts) are merged into one parameterized
    /// <see cref="BreakApart"/> driven by a <see cref="BreakupProfile"/>.
    /// </remarks>
    public class PartExploder : MonoBehaviour
    {
        /// <summary>Tunable set for one breakup flavour (explode vs crash).</summary>
        [Serializable]
        public struct BreakupProfile
        {
            [Tooltip("AddExplosionForce magnitude.")] public float force;
            [Tooltip("AddExplosionForce radius.")] public float radius;
            [Tooltip("AddExplosionForce upwards modifier (raises the apparent origin so parts fly up more).")] public float upwardsModifier;
            [Tooltip("Mass assigned to a part that has no Rigidbody yet.")] public float partMass;
            [Tooltip("Random torque impulse range applied to each part (+/- this on each axis).")] public float torqueRange;
            [Tooltip("Random per-part lifetime range before it is destroyed (seconds).")] public Vector2 partLifetime;
            [Tooltip("Add a bounds-fitted BoxCollider to any part missing a collider (stops clipping through the ground).")] public bool addCollidersIfMissing;
        }

        [Header("Explode preset (outward, light)")]
        [SerializeField] private BreakupProfile _explodeProfile = new BreakupProfile
        {
            force = 500f, radius = 5f, upwardsModifier = 3f, partMass = 0.5f, torqueRange = 10f,
            partLifetime = new Vector2(3f, 8f), addCollidersIfMissing = false
        };

        [Header("Crash preset (heavier, adds colliders)")]
        [SerializeField] private BreakupProfile _crashProfile = new BreakupProfile
        {
            force = 200f, radius = 3f, upwardsModifier = 2f, partMass = 0.8f, torqueRange = 5f,
            partLifetime = new Vector2(3f, 7f), addCollidersIfMissing = true
        };

        [Header("Optional feedback")]
        [SerializeField] private GameObject _effectPrefab;
        [SerializeField] private AudioClip _sound;

        [Tooltip("Delay before the (now empty) root object destroys itself, in seconds.")]
        [SerializeField] private float _selfDestructDelay = 0.5f;

        private bool _hasExploded;

        /// <summary>True once a breakup has been triggered (breakup only runs once).</summary>
        public bool HasExploded => _hasExploded;

        /// <summary>Break the hierarchy apart with the outward "explode" preset.</summary>
        public void Explode() => TriggerBreakup(_explodeProfile);

        /// <summary>Break the hierarchy apart with the heavier "crash" preset.</summary>
        public void Crash() => TriggerBreakup(_crashProfile);

        private void TriggerBreakup(BreakupProfile profile)
        {
            if (_hasExploded) return;
            _hasExploded = true;

            if (_effectPrefab != null)
            {
                Instantiate(_effectPrefab, transform.position, Quaternion.identity);
            }

            if (_sound != null)
            {
                AudioSource.PlayClipAtPoint(_sound, transform.position);
            }

            StartCoroutine(BreakApart(profile));
        }

        private IEnumerator BreakApart(BreakupProfile profile)
        {
            Vector3 origin = transform.position - Vector3.up * 1f;
            Transform[] parts = GetComponentsInChildren<Transform>();

            foreach (Transform part in parts)
            {
                if (part == transform) continue; // skip the root

                Rigidbody partRb = part.GetComponent<Rigidbody>();
                if (partRb == null)
                {
                    partRb = part.gameObject.AddComponent<Rigidbody>();
                    partRb.mass = profile.partMass;
                }

                if (profile.addCollidersIfMissing && part.GetComponent<Collider>() == null)
                {
                    BoxCollider box = part.gameObject.AddComponent<BoxCollider>();
                    Renderer renderer = part.GetComponent<Renderer>();
                    if (renderer != null) box.size = WorldSizeToLocalSize(renderer.bounds.size, part.lossyScale);
                }

                part.SetParent(null);

                partRb.AddExplosionForce(profile.force, origin, profile.radius, profile.upwardsModifier);
                partRb.AddTorque(new Vector3(
                    UnityEngine.Random.Range(-profile.torqueRange, profile.torqueRange),
                    UnityEngine.Random.Range(-profile.torqueRange, profile.torqueRange),
                    UnityEngine.Random.Range(-profile.torqueRange, profile.torqueRange)), ForceMode.Impulse);

                Destroy(part.gameObject, UnityEngine.Random.Range(profile.partLifetime.x, profile.partLifetime.y));
            }

            yield return null;
            Destroy(gameObject, _selfDestructDelay);
        }

        // Renderer.bounds.size is world-space (already scale-inclusive); BoxCollider.size is local-space
        // and gets multiplied by lossyScale again by Unity at the physics level. Assigning the former
        // straight to the latter doubled (or halved) the auto-added collider's footprint on any scaled
        // part. Dividing out the scale here recovers the correct local-space size; a zero-scale axis falls
        // back to the raw world size rather than dividing by zero (a degenerate case with no correct answer
        // anyway, since a zero-scale axis has no meaningful "local size" to recover).
        private static Vector3 WorldSizeToLocalSize(Vector3 worldSize, Vector3 lossyScale)
        {
            return new Vector3(
                Mathf.Approximately(lossyScale.x, 0f) ? worldSize.x : worldSize.x / lossyScale.x,
                Mathf.Approximately(lossyScale.y, 0f) ? worldSize.y : worldSize.y / lossyScale.y,
                Mathf.Approximately(lossyScale.z, 0f) ? worldSize.z : worldSize.z / lossyScale.z);
        }
    }
}
