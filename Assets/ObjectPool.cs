using UnityEngine;
using System.Collections.Generic;

public static class ObjectPool
{
    private static readonly Dictionary<GameObject, Queue<GameObject>> pool = new();
    private const int MaxPoolSize = 50; // 메모리 초과 방지

    public static GameObject GetOrCreate(GameObject prefab)
    {
        if (!pool.ContainsKey(prefab))
            pool[prefab] = new Queue<GameObject>();

        if (pool[prefab].Count > 0)
        {
            var obj = pool[prefab].Dequeue();
            obj.SetActive(true);
            return obj;
        }

        if (pool[prefab].Count >= MaxPoolSize)
            return null;

        return GameObject.Instantiate(prefab);
    }

    public static void Return(GameObject prefab, GameObject instance)
    {
        if (pool[prefab].Count >= MaxPoolSize)
        {
            GameObject.Destroy(instance);
            return;
        }
        instance.SetActive(false);
        pool[prefab].Enqueue(instance);
    }
}