using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace JamKit
{
    /// <summary>
    /// Thin prefab-keyed convenience wrapper over <see cref="UnityEngine.Pool.ObjectPool{T}"/>.
    /// One pool is created lazily per prefab the first time it is requested.
    /// </summary>
    public static class Pools
    {
        private static readonly Dictionary<GameObject, ObjectPool<GameObject>> _pools = new();

        /// <summary>
        /// Get an instance of <paramref name="prefab"/> from its pool, creating the pool on first use.
        /// </summary>
        public static GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            ObjectPool<GameObject> pool = GetOrCreatePool(prefab);
            GameObject instance = pool.Get();
            Transform instanceTransform = instance.transform;
            instanceTransform.SetParent(parent);
            instanceTransform.SetPositionAndRotation(position, rotation);
            return instance;
        }

        /// <summary>
        /// Return an instance previously obtained via <see cref="Get"/> for <paramref name="prefab"/> to its pool.
        /// </summary>
        public static void Release(GameObject prefab, GameObject instance)
        {
            GetOrCreatePool(prefab).Release(instance);
        }

        private static ObjectPool<GameObject> GetOrCreatePool(GameObject prefab)
        {
            if (!_pools.TryGetValue(prefab, out ObjectPool<GameObject> pool))
            {
                pool = new ObjectPool<GameObject>(
                    createFunc: () => Object.Instantiate(prefab),
                    actionOnGet: instance => instance.SetActive(true),
                    actionOnRelease: instance => instance.SetActive(false),
                    actionOnDestroy: Object.Destroy);
                _pools[prefab] = pool;
            }

            return pool;
        }
    }
}
