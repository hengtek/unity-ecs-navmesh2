using System.Collections.Concurrent;
using System.Collections.Generic;
using thelebaron.mathematics;
using Unity.Burst;
using UnityEngine;
using UnityEngine.Experimental.AI;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine.Profiling;

public unsafe struct UnsafeNavMeshQuery
{
    [NativeDisableUnsafePtrRestriction] public void* Ptr;
}

public enum PathfindingFailedReason
{
    InvalidToOrFromLocation,
    FailedToBegin,
    FailedToResolve,
}

public enum PathQueryStatus
{
    Pending,
    Success,
    Failed
}

public unsafe class NavMeshQuerySystem : SystemBase
{
    private EntityQuery query;
    public int MaxQueries = 256;
    public int MaxPathSize = 1024;
    public int MaxIterations = 1024;
    public int MaxMapWidth = 10000;
    public int Version = 0;

    private NavMeshWorld navMeshWorld;
    private NavMeshQuery locationQuery;
    private NativeQueue<Agent.PathQuery> pathQueryQueue;
    private NativeList<Agent.PathQuery> progressQueue;
    private NativeQueue<int> availableSlots;
    
    private NativeArray<Agent.PathQuery> pathQueries;
    private bool UseCache = false;
    private ConcurrentDictionary<int, NativeArray<float3>> cachedPaths = new ConcurrentDictionary<int, NativeArray<float3>>();
    private NativeArray<UnsafeNavMeshQuery> UnsafeNavMeshQueryArray;
    private List<NavMeshQuery> NavMeshQueryDisposeList;
    private NativeArray<Agent.PathQuery> PathQueryArray;
    
    protected override void OnCreate()
    {
        query = GetEntityQuery(typeof(Agent), typeof(Path), typeof(AgentRandom));
        navMeshWorld = NavMeshWorld.GetDefaultWorld();
        locationQuery = new NavMeshQuery(navMeshWorld, Allocator.Persistent);
        availableSlots = new NativeQueue<int>(Allocator.Persistent);
        progressQueue = new NativeList<Agent.PathQuery>(MaxQueries, Allocator.Persistent);
        pathQueries = new NativeArray<Agent.PathQuery>(MaxQueries, Allocator.Persistent);
        
        pathQueryQueue = new NativeQueue<Agent.PathQuery>(Allocator.Persistent);
        for (int i = 0; i < MaxQueries; i++)
        {
            availableSlots.Enqueue(i);
        }

        NavMeshQueryDisposeList = new List<NavMeshQuery>();
        var jobWorkerCount = JobsUtility.MaxJobThreadCount * 64;
        PathQueryArray = new NativeArray<Agent.PathQuery>(jobWorkerCount, Allocator.Persistent);
        UnsafeNavMeshQueryArray = new NativeArray<UnsafeNavMeshQuery>(jobWorkerCount, Allocator.Persistent);
        for (var index = 0; index < UnsafeNavMeshQueryArray.Length; index++)
        {
            UnsafeNavMeshQueryArray[index] = new UnsafeNavMeshQuery {
                Ptr = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<NavMeshQuery>(), UnsafeUtility.AlignOf<NavMeshQuery>(), Allocator.Persistent)
            };
            
            var navMeshQuery = new NavMeshQuery(navMeshWorld, Allocator.Persistent, MaxPathSize);
            NavMeshQueryDisposeList.Add(navMeshQuery);
            UnsafeUtility.CopyStructureToPtr(ref navMeshQuery, UnsafeNavMeshQueryArray[index].Ptr);
        }
    }

    private void PurgeCache ()
    {
        Debug.Log("clearing cache");
        Version++;
        cachedPaths.Clear ();
    }

    [BurstCompile]
    struct AgentQueueJob : IJobEntityBatch
    {
        public int MaxMapWidth;
        [ReadOnly] public EntityTypeHandle EntityType;
        public ComponentTypeHandle<Agent>    NavAgentType;
        [NativeDisableParallelForRestriction] public NativeQueue<Agent.PathQuery>.ParallelWriter PathQueries;
        
        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            var entities = batchInChunk.GetNativeArray(EntityType);
            var agents  = batchInChunk.GetNativeArray(NavAgentType);
            var entitylist = new NativeList<Entity>(Allocator.Temp);
            for (int i = 0; i < agents.Length; i++)
            {
                var entity = entities[i];
                var agent  = agents[i];
                
                if (agent.status == Status.Requested) 
                {
                    //Debug.Log("Requested > PathQueued");
                    agent.status = Status.PathQueued;
                    var hash = HashKey((int3)agent.position, (int3)agent.destination);
                    
                    var pathQuery = new Agent.PathQuery
                    {
                        Entity = entity, From = agent.position, To = agent.destination, AreaMask = agent.areaMask, PositionKey = hash
                    };
                    if (!entitylist.Contains(entity))
                    {
                        entitylist.Add(entity);
                        PathQueries.Enqueue(pathQuery);
                    }
                    agents[i]         = agent;// todo should this be inside above conditional??
                }
            }
        }
        
        private int HashKey(int3 from, int3 to)
        {
            var hashPos = (int) math.hash(new int3(math.floor(from + to))); // was /
            return MaxMapWidth * hashPos; //todo fix this
        }
    }
    
    [BurstCompile]
    struct MergeQueriesJob : IJob
    {
        //public NativeQueue<Agent.PathQuery> NewQueries;
        
        public NativeQueue<Agent.PathQuery> PathQueries;
        public NativeQueue<int> AvailableSlots;
        public NativeArray<Agent.PathQuery> PathQueryArray;
        public NativeArray<UnsafeNavMeshQuery> UnsafeNavMeshQueryArray;
        
        public void Execute()
        {
            //while (NewQueries.Count>0)
            //{
                //var query = NewQueries.Dequeue();
                //PathQueries.Enqueue(query);
            //}
            
            var entityList = new NativeList<Entity>(Allocator.Temp);
            for (int i = 0; i < UnsafeNavMeshQueryArray.Length; i++)
            {
                if(PathQueries.TryDequeue(out var pathQuery))
                {
                    if (!entityList.Contains(pathQuery.Entity))
                    {
                        entityList.Add(pathQuery.Entity);
                        PathQueryArray[i] = pathQuery;
                    }
                    else
                    {
                        PathQueryArray[i] = default;
                    }
                }
                else
                {
                    PathQueryArray[i] = default;
                }
                if(AvailableSlots.Count<=256)
                    AvailableSlots.Enqueue(1);
            }
        }
    }
    [BurstCompile]
    private struct PathQueryJob : IJobParallelFor
    {
        private const int MaxIterations = 1024;
        private const int MaxPathSize = 1024;
        public NativeArray<Agent.PathQuery> PathQueryArray;
        public NativeArray<UnsafeNavMeshQuery> UnsafeNavMeshQueryArray;
        [NativeDisableParallelForRestriction] public BufferFromEntity<Path> PathFromEntity;
        [NativeDisableParallelForRestriction] public ComponentDataFromEntity<Agent> AgentFromEntity;
        
        public void Execute(int index)
        {
            var pathQuery = PathQueryArray[index];
            if(pathQuery.Entity.Equals(Entity.Null))
                return;
            
            var navMeshQueryPointer = UnsafeNavMeshQueryArray[index];
            UnsafeUtility.CopyPtrToStructure(navMeshQueryPointer.Ptr, out NavMeshQuery navMeshQuery);

            var entity = pathQuery.Entity;
            var from = navMeshQuery.MapLocation(pathQuery.From, maths.one * 10, 0);
            var to = navMeshQuery.MapLocation(pathQuery.To, maths.one * 10, 0);
            if (!navMeshQuery.IsValid(from) || !navMeshQuery.IsValid(to))
            {
                var agent = AgentFromEntity[entity];
                agent.pathQueryStatus = PathQueryStatus.Failed;
                AgentFromEntity[entity] = agent;
                return;
            }

            var status = navMeshQuery.BeginFindPath(from, to, pathQuery.AreaMask);
            
            if (status == UnityEngine.Experimental.AI.PathQueryStatus.InProgress || status == UnityEngine.Experimental.AI.PathQueryStatus.Success)
            {
                status = navMeshQuery.UpdateFindPath(MaxIterations, out int performed);

                if (status == UnityEngine.Experimental.AI.PathQueryStatus.InProgress | status == (UnityEngine.Experimental.AI.PathQueryStatus.InProgress | UnityEngine.Experimental.AI.PathQueryStatus.OutOfNodes))
                {
                    var agent = AgentFromEntity[entity];
                    agent.pathQueryStatus       = PathQueryStatus.Failed;
                    AgentFromEntity[entity] = agent;
                    //todo store path status in agent to view debug
                    return;
                }
                if (status == UnityEngine.Experimental.AI.PathQueryStatus.Success)
                {
                    var endStatus = navMeshQuery.EndFindPath(out int polySize);
                    if (endStatus == UnityEngine.Experimental.AI.PathQueryStatus.Success)
                    {
                        var polygons = new NativeArray<PolygonId>(polySize, Allocator.Temp);
                        navMeshQuery.GetPathResult(polygons);
                        var straightPathFlags = new NativeArray<StraightPathFlags>(MaxPathSize, Allocator.Temp);
                        var vertexSide = new NativeArray<float>(MaxPathSize, Allocator.Temp);
                        var cornerCount = 0;
                        var navMeshLocations = new NativeArray<NavMeshLocation>(MaxPathSize, Allocator.Temp);
                        
                        var pathStatus = PathUtils.FindStraightPath(
                            navMeshQuery,
                            pathQuery.From,
                            pathQuery.To,
                            polygons,
                            polySize,
                            ref navMeshLocations,
                            ref straightPathFlags,
                            ref vertexSide,
                            ref cornerCount,
                            MaxPathSize
                        );

                        if (pathStatus == UnityEngine.Experimental.AI.PathQueryStatus.Success)
                        {
                            var pathBuffer = PathFromEntity[entity];
                            pathBuffer.Clear();
                            
                            for (int k = 0; k < cornerCount; k++) // cornerCount is path node count, if use navMeshLoc length it gives the max size set at allocation
                            {
                                pathBuffer.Add(navMeshLocations[k].position);
                            }
                            var agent = AgentFromEntity[entity];
                            agent.pathQueryStatus       = PathQueryStatus.Success;
                            AgentFromEntity[entity] = agent;
                            
                        }
                    }
                }
            }
            else
            {
                var agent = AgentFromEntity[entity];
                agent.pathQueryStatus       = PathQueryStatus.Failed;
                AgentFromEntity[entity] = agent;
                
            }
        }
    }
    
    
    protected override void OnUpdate()
    {
        if (cachedPaths.Count > 50000)
            PurgeCache();

        Dependency = new AgentQueueJob
        {
            MaxMapWidth  = 1024,
            EntityType   = GetEntityTypeHandle(),
            NavAgentType = GetComponentTypeHandle<Agent>(),
            PathQueries = pathQueryQueue.AsParallelWriter()
        }.ScheduleParallel(query, 2, Dependency);
        
        Dependency = new MergeQueriesJob
        {
            //NewQueries = newQueries,
            PathQueries             = pathQueryQueue,
            AvailableSlots          = availableSlots,
            PathQueryArray          = PathQueryArray,
            UnsafeNavMeshQueryArray = UnsafeNavMeshQueryArray
        }.Schedule(Dependency);

        Dependency = new PathQueryJob
        {
            PathQueryArray          = PathQueryArray,
            UnsafeNavMeshQueryArray = UnsafeNavMeshQueryArray,
            PathFromEntity          = GetBufferFromEntity<Path>(),
            AgentFromEntity         = GetComponentDataFromEntity<Agent>()
        }.Schedule(UnsafeNavMeshQueryArray.Length, 1, Dependency);
        navMeshWorld.AddDependency(Dependency);
    }
    
    protected override void OnDestroy()
    {
        availableSlots.Dispose();
        progressQueue.Dispose();
        pathQueryQueue.Dispose();
        locationQuery.Dispose();
        pathQueries.Dispose();
        
        foreach (var keyPair in cachedPaths)
        {
            keyPair.Value.Dispose();
        }

        foreach (var navMeshQuery in NavMeshQueryDisposeList) 
            navMeshQuery.Dispose();
        
        foreach (var unsafeNavMeshQuery in UnsafeNavMeshQueryArray) UnsafeUtility.Free(unsafeNavMeshQuery.Ptr, Allocator.Persistent);
        
        UnsafeNavMeshQueryArray.Dispose();
        PathQueryArray.Dispose();
    }

    private int GetHashKey(int3 from, int3 to)
    {
        var hashPos = (int) math.hash(new int3(math.floor(from + to))); // was /
        return MaxMapWidth * hashPos;
    }

    /// Request a path. The ID is for you to identify the path
    public void RequestPath(Entity entity, float3 from, float3 to, int areaMask = -1)
    {
        var hash = GetHashKey((int3)from, (int3)to);
        var data = new Agent.PathQuery
        {
            Entity = entity, From = from, To = to, AreaMask = areaMask, PositionKey = hash
        };
        
        if (UseCache)
        {
            if (cachedPaths.TryGetValue(hash, out NativeArray<float3> path))
            {
                var buffer = EntityManager.GetBuffer<Path>(entity);
                buffer.Clear();
                buffer.CopyFrom(path.Reinterpret<Path>());
                return;
            }
        }
        
        //duplicate check
        for (var i = 0; i < pathQueryQueue.Count; i++)
        {
            if (data.Entity.Equals(pathQueryQueue.Peek().Entity))
            {
                //Debug.LogError("entity has two queries");
                pathQueryQueue.Dequeue();
            }
        }
        
        pathQueryQueue.Enqueue(data);
    }

    public JobHandle GetDependency()
    {
        return Dependency;
    }
}
