    using thelebaron.mathematics;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;
    using Unity.Entities;
    using Unity.Jobs;
    using UnityEngine.Experimental.AI;

    struct BatchQueryJob : IJobEntityBatch
    {
        private const int MaxIterations = 1024;
        private const int MaxPathSize = 1024;
        [ReadOnly] public EntityTypeHandle EntityType;
        public ComponentTypeHandle<Agent> NavAgentType;
        [NativeDisableParallelForRestriction] public BufferTypeHandle<Path> PathType;
        public NativeArray<UnsafeNavMeshQuery> UnsafeNavMeshQueryArray;
        public NativeReference<int> JobIterations; // cap at 3 per worker thread?
        
        public unsafe void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            var batchIterations = 0;
            var entities = batchInChunk.GetNativeArray(EntityType);
            var paths = batchInChunk.GetBufferAccessor(PathType);
            var navAgents = batchInChunk.GetNativeArray(NavAgentType);
            
            for (int i = 0; i < entities.Length; i++)
            {
                if(batchIterations>3)
                    break;
                
                var agent = navAgents[i];
                var path = paths[i];

                if (agent.query.QueryStatus != Agent.PathQueryStatus.Requested)
                    continue;

                if (agent.query.QueryStatus == Agent.PathQueryStatus.Requested)
                {
                    var pathQuery = agent.query;
                    var navMeshQueryPointer = UnsafeNavMeshQueryArray[batchIndex];
                    UnsafeUtility.CopyPtrToStructure(navMeshQueryPointer.Ptr, out NavMeshQuery navMeshQuery);
                    
                    batchIterations++;
                    
                    var from = navMeshQuery.MapLocation(pathQuery.From, maths.one * 10, 0);
                    var to = navMeshQuery.MapLocation(pathQuery.To, maths.one * 10, 0);
                    if (!navMeshQuery.IsValid(from) || !navMeshQuery.IsValid(to))
                    {
                        pathQuery.QueryStatus = Agent.PathQueryStatus.Invalid;
                        agent.query = pathQuery;
                        navAgents[i] = agent;
                        continue;
                    }

                    var status = navMeshQuery.BeginFindPath(from, to, pathQuery.AreaMask);
                    
                    if (status == UnityEngine.Experimental.AI.PathQueryStatus.InProgress || status == UnityEngine.Experimental.AI.PathQueryStatus.Success)
                    {
                        status = navMeshQuery.UpdateFindPath(MaxIterations, out int performed);

                        if (status == UnityEngine.Experimental.AI.PathQueryStatus.InProgress | status == (UnityEngine.Experimental.AI.PathQueryStatus.InProgress | UnityEngine.Experimental.AI.PathQueryStatus.OutOfNodes))
                        {
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
                                //Debug.LogWarning (pathStatus);

                                if (pathStatus == UnityEngine.Experimental.AI.PathQueryStatus.Success)
                                {
                                    path.Clear();
                                    
                                    for (int k = 0; k < cornerCount; k++) // cornerCount is path node count, if use navMeshLoc length it gives the max size set at allocation
                                    {
                                        path.Add(navMeshLocations[k].position);
                                    }
                                    
                                    pathQuery.QueryStatus = Agent.PathQueryStatus.Complete;
                                    agent.query = pathQuery;
                                    navAgents[i] = agent;
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    
    [BurstCompile]
    public struct UpdateQueryStatusJob : IJob
    {
        public NavMeshQuery NavMeshQuery;
        public Agent.PathQuery PathQuery;
        public int MaxIterations;
        public int MaxPathSize;
        public int Index;
        [NativeDisableParallelForRestriction] public NativeArray<int> StatusArray;
        [NativeDisableParallelForRestriction] public NativeArray<NavMeshLocation> ResultsArray;

        public void Execute()
        {
            var status = NavMeshQuery.UpdateFindPath(MaxIterations, out int performed);

            if (status == UnityEngine.Experimental.AI.PathQueryStatus.InProgress | status == (UnityEngine.Experimental.AI.PathQueryStatus.InProgress | UnityEngine.Experimental.AI.PathQueryStatus.OutOfNodes))
            {
                StatusArray[0] = 0;
                return;
            }

            StatusArray[0] = 1;

            if (status == UnityEngine.Experimental.AI.PathQueryStatus.Success)
            {
                var endStatus = NavMeshQuery.EndFindPath(out int polySize);
                if (endStatus == UnityEngine.Experimental.AI.PathQueryStatus.Success)
                {
                    var polygons = new NativeArray<PolygonId>(polySize, Allocator.Temp);
                    NavMeshQuery.GetPathResult(polygons);
                    var straightPathFlags = new NativeArray<StraightPathFlags>(MaxPathSize, Allocator.Temp);
                    var vertexSide = new NativeArray<float>(MaxPathSize, Allocator.Temp);
                    var cornerCount = 0;
                    var pathStatus = PathUtils.FindStraightPath(
                        NavMeshQuery,
                        PathQuery.From,
                        PathQuery.To,
                        polygons,
                        polySize,
                        ref ResultsArray,
                        ref straightPathFlags,
                        ref vertexSide,
                        ref cornerCount,
                        MaxPathSize
                    );
                    //Debug.LogWarning (pathStatus);

                    if (pathStatus == UnityEngine.Experimental.AI.PathQueryStatus.Success)
                    {
                        StatusArray[1] = 1;
                        StatusArray[2] = cornerCount;
                        //Debug.LogWarning (pathStatus);
                    }
                    else
                    {
                        //Debug.LogWarning (pathStatus);
                        StatusArray[0] = 1;
                        StatusArray[1] = 2;
                    }

                    polygons.Dispose();
                    straightPathFlags.Dispose();
                    vertexSide.Dispose();
                }
                else
                {
                    //Debug.LogWarning (endStatus);
                    StatusArray[0] = 1;
                    StatusArray[1] = 2;
                }
            }
            else
            {
                StatusArray[0] = 1;
                StatusArray[1] = 3;
            }
        }
    }