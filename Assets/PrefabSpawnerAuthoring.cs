using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Random = UnityEngine.Random;

public class PrefabSpawnerAuthoring : MonoBehaviour, IConvertGameObjectToEntity, IDeclareReferencedPrefabs
{
    public GameObject prefab;
    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        var prefabSpawner = new PrefabSpawner {Prefab = conversionSystem.TryGetPrimaryEntity(prefab)};
        dstManager.AddComponentData(entity, prefabSpawner);
    }

    public void DeclareReferencedPrefabs(List<GameObject> referencedPrefabs)
    {
        referencedPrefabs.Add(prefab);
    }
}

public struct PrefabSpawner : IComponentData
{
    public Entity Prefab;
}

public class PrefabSpawnerSystem : SystemBase
{
    private int count;
    protected override void OnUpdate()
    {
        if (count >50)
            Enabled = false;
        
        Entities.ForEach((Entity entity, ref PrefabSpawner spawner) =>
        {
            for (int i = 0; i < 100; i++)
            {
                var pos = new float3 {x = Random.Range(-10, 10), y = 0, z = Random.Range(-10, 10)};
                var e = EntityManager.Instantiate(spawner.Prefab);
                EntityManager.SetComponentData(e, new Translation{Value = pos});
            }
            count++;
        }).WithoutBurst().WithStructuralChanges().Run();
    }
}