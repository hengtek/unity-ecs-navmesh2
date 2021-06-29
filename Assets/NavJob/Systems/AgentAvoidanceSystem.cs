using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Experimental.AI;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using thelebaron.mathematics;

[DisableAutoCreation]
public class AgentAvoidanceSystem : SystemBase
{
    NavMeshQuery navMeshQuery;
    
    protected override void OnCreate()
    {
        query = GetEntityQuery(typeof(NavMeshAgent), typeof(AgentAvoidance));
        navMeshQuery = new NavMeshQuery (NavMeshWorld.GetDefaultWorld (), Allocator.Persistent, 128);
    }
    
    [BurstCompile]
    struct AvoidanceJob : IJobNativeMultiHashMapMergedSharedKeyIndices
    {
        public NativeArray<Agent> agents;
        public NativeArray<AgentAvoidance> avoidances;
        [ReadOnly] public NavMeshQuery navMeshQuery;
        public float DeltaTime;
        public void ExecuteFirst (int index) { }

        public void ExecuteNext (int firstIndex, int index)
        {
            var agent = agents[index];
            var avoidance = avoidances[index];
            var move = maths.left;
            if (index % 2 == 1)
            {
                move = maths.right;
            }
            float3 drift = agent.rotation * (maths.forward + move) * agent.currentMoveSpeed * DeltaTime;
            if (agent.nextWaypointIndex != agent.totalWaypoints)
            {
                var offsetWaypoint = agent.currentWaypoint + drift;
                var waypointInfo = navMeshQuery.MapLocation (offsetWaypoint, Vector3.one * 3f, 0, agent.areaMask);
                if (navMeshQuery.IsValid (waypointInfo))
                {
                    agent.currentWaypoint = waypointInfo.position;
                }
            }
            agent.currentMoveSpeed = Mathf.Max (agent.currentMoveSpeed / 2f, 0.5f);
            var positionInfo = navMeshQuery.MapLocation (agent.position + drift, Vector3.one * 3f, 0, agent.areaMask);
            if (navMeshQuery.IsValid (positionInfo))
            {
                agent.nextPosition = positionInfo.position;
            }
            else
            {
                agent.nextPosition = agent.position;
            }
            agents[index] = agent;
        }
    }

    [BurstCompile]
    struct HashPositionsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Agent> agents;
        public NativeArray<AgentAvoidance> avoidances;
        public NativeMultiHashMap<int, int> indexMap;
        public NativeMultiHashMap<int, float3>.ParallelWriter nextPositionMap;
        public int mapSize;

        public void Execute (int index)
        {
            var agent = agents[index];
            var avoidance = avoidances[index];
            var hash = Hash (agent.position, avoidance.radius);
            indexMap.Add(hash, index);
            nextPositionMap.Add(hash, agent.nextPosition);
            avoidance.partition = hash;
            avoidances[index] = avoidance;
        }
        public int Hash (float3 position, float radius)
        {
            int ix = Mathf.RoundToInt ((position.x / radius) * radius);
            int iz = Mathf.RoundToInt ((position.z / radius) * radius);
            return ix * mapSize + iz;
        }
    }
    
    NavMeshQuerySystem querySystem;
    private EntityQuery query;
    
    protected override void OnUpdate()
    {
        var length = query.CalculateEntityCount();
        if (length <= 0) 
            return;
        
        var hashMap = new NativeMultiHashMap<int, int> (100 * 1024, Allocator.TempJob);
        var nextPositionMap = new NativeMultiHashMap<int, float3> (100 * 1024, Allocator.TempJob).AsParallelWriter();
        var navAgents = query.ToComponentDataArray<Agent>(Allocator.TempJob);
        var navAgentAvoidances = query.ToComponentDataArray<AgentAvoidance>(Allocator.TempJob);
        var deltaTime = Time.DeltaTime;
            
        Dependency = new HashPositionsJob
        {
            mapSize = querySystem.MaxMapWidth,
            agents = navAgents,
            avoidances = navAgentAvoidances,
            indexMap = hashMap,
            nextPositionMap = nextPositionMap
        }.Schedule (length, 64, Dependency);
        
        Dependency = new AvoidanceJob
        {
            DeltaTime = deltaTime,
            agents = navAgents,
            avoidances = navAgentAvoidances,
            navMeshQuery = navMeshQuery
        }.Schedule(hashMap, 64, Dependency);
    }

    protected override void OnDestroy()
    {
        navMeshQuery.Dispose ();
    }
}
