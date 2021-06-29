using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;

public struct Path : IBufferElementData
{
    public float3 Corner;
    
    public static implicit operator float3(Path e) { return e.Corner; }
    public static implicit operator Path(float3 e) { return new Path { Corner = e }; }
    public static implicit operator Path(Vector3 e) { return new Path { Corner = e }; }
}

/// <summary>
/// this doesnt actually do anything as of yet
/// </summary>
public struct WanderTimer  : IComponentData
{
    public float Time;
}

/// <summary>
/// just used for getting a random destination for the example
/// </summary>
public struct AgentRandom  : IComponentData
{
    public Random Rand;
}

[SelectionBase]
public class AgentAuthoring : MonoBehaviour, IConvertGameObjectToEntity
{
    public static int Count = 1000;
    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        var agent = new Agent(transform.position, transform.rotation);
        dstManager.AddComponentData(entity, agent);
        dstManager.AddComponentData(entity, new AgentAvoidance(1));
        dstManager.AddComponentData(entity, new CopyPositionFromNavAgent());
        dstManager.AddComponentData(entity, new CopyRotationFromNavAgent());
        dstManager.AddComponentData(entity, new WanderTimer{Time = 15});
        dstManager.AddBuffer<Path>(entity);

    }
}



