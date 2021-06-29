using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

/// <summary>
/// Syncs the transform matrix from the nav agent to a TransformMatrix component
/// </summary>
[UpdateAfter (typeof (AgentMoveSystem))]
public class AgentTransformSystem : SystemBase
{
    private EntityQuery query;
    protected override void OnCreate()
    {
        query = GetEntityQuery(ComponentType.ReadWrite<Agent>());
    }
    
    [BurstCompile]
    struct AgentTransformJob : IJobEntityBatch
    {
        public ComponentTypeHandle<Agent> AgentType;
        public ComponentTypeHandle<LocalToWorld> LocalToWorldType;
        public ComponentTypeHandle<Translation> TranslationType;
        public ComponentTypeHandle<Rotation> RotationType;
        [ReadOnly] public ComponentTypeHandle<CopyPositionToNavAgent> CopyPositionToNavAgentType;
        [ReadOnly] public ComponentTypeHandle<CopyPositionFromNavAgent> CopyPositionFromNavAgentType;
        [ReadOnly] public ComponentTypeHandle<CopyRotationToNavAgent> CopyRotationToNavAgentType;
        [ReadOnly] public ComponentTypeHandle<CopyRotationFromNavAgent> CopyRotationFromNavAgentType;

        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            var agents = batchInChunk.GetNativeArray(AgentType);
            var localToWorlds = batchInChunk.GetNativeArray(LocalToWorldType);
            var hasLocalToWorld = batchInChunk.Has(LocalToWorldType);
            var hasTranslation = batchInChunk.Has(TranslationType);
            var hasRotation = batchInChunk.Has(RotationType);
            var translations = batchInChunk.GetNativeArray(TranslationType);
            var rotations = batchInChunk.GetNativeArray(RotationType);
            
            var hasCopyPositionToNav = batchInChunk.Has(CopyPositionToNavAgentType);
            var hasCopyPositionFromNav = batchInChunk.Has(CopyPositionFromNavAgentType);
            var hasCopyRotationToNav = batchInChunk.Has(CopyRotationToNavAgentType);
            var hasCopyRotationFromNav = batchInChunk.Has(CopyRotationFromNavAgentType);

            if (!hasTranslation && !hasRotation && hasLocalToWorld)
            {
                for (int i = 0; i < agents.Length; i++)
                {
                    var navAgent = agents[i];
                    var localToWorld = localToWorlds[i];
                    //localToWorld.Value =  float4x4.TRS (navAgent.position, navAgent.rotation, new float3(1,1,1)); //replace with maths
                    //localToWorlds[i] = localToWorld;
                }
            }

            if (hasCopyPositionToNav && hasTranslation)
            {
                for (int i = 0; i < agents.Length; i++)
                {
                    var navAgent = agents[i];
                    var translation = translations[i];
                    navAgent.position = translation.Value;
                    agents[i] = navAgent;
                }
            }
            if (hasCopyPositionFromNav && hasTranslation)
            {
                for (int i = 0; i < agents.Length; i++)
                {
                    var navAgent = agents[i];
                    var translation = translations[i];
                    translation.Value = navAgent.position;
                    translations[i] = translation;
                }
            }
            
            if (hasCopyRotationToNav && hasRotation)
            {
                for (int i = 0; i < agents.Length; i++)
                {
                    var navAgent = agents[i];
                    var rotation = rotations[i];
                    navAgent.rotation = rotation.Value;
                    agents[i] = navAgent;
                }
            }
            if (hasCopyRotationFromNav && hasRotation)
            {
                for (int i = 0; i < agents.Length; i++)
                {
                    var navAgent = agents[i];
                    var rotation = rotations[i];
                    rotation.Value = navAgent.rotation;
                    rotations[i] = rotation;
                }
            }
        }
    }
    
    protected override void OnUpdate()
    {
        Dependency = new AgentTransformJob
        {
            AgentType = GetComponentTypeHandle<Agent>(),
            LocalToWorldType = GetComponentTypeHandle<LocalToWorld>(),
            TranslationType = GetComponentTypeHandle<Translation>(),
            RotationType = GetComponentTypeHandle<Rotation>(),
            CopyPositionToNavAgentType = GetComponentTypeHandle<CopyPositionToNavAgent>(true),
            CopyPositionFromNavAgentType = GetComponentTypeHandle<CopyPositionFromNavAgent>(true),
            CopyRotationToNavAgentType = GetComponentTypeHandle<CopyRotationToNavAgent>(true),
            CopyRotationFromNavAgentType = GetComponentTypeHandle<CopyRotationFromNavAgent>(true)
        }.ScheduleParallel(query, 4, Dependency);
    }
}
