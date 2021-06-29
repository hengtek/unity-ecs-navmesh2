using thelebaron.mathematics;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.AI;

[UpdateAfter(typeof(NavMeshQuerySystem))]
public class AgentMoveSystem : SystemBase
{
    private EntityQuery query;
    protected override void OnCreate()
    {
        query = GetEntityQuery(typeof(Agent), typeof(Path));
    }

    protected override void OnUpdate ()
    {

        Dependency = new WaypointJob
        {
            EntityType = GetEntityTypeHandle(),
            AgentType = GetComponentTypeHandle<Agent>(),
            PathType = GetBufferTypeHandle<Path>()
        }.ScheduleParallel(query, 2, Dependency);
        
        Dependency = new MoveJob
        {
            AgentType = GetComponentTypeHandle<Agent>(),
            LocalToWorldType = GetComponentTypeHandle<LocalToWorld>(),
            PathType = GetBufferTypeHandle<Path>(),
            DeltaTime = Time.DeltaTime
        }.ScheduleParallel(query, 3, Dependency);
        
        
        /*Dependency.Complete();
        Entities.ForEach((Entity entity, ref Agent agent) =>
        {
            if(NavMesh.SamplePosition(agent.position, out var navHit, 1 , agent.areaMask))
            {
                agent.position = navHit.position;
            }
        }).Run();*/
    }
    
    [BurstCompile]
    private struct WaypointJob : IJobEntityBatch
    {
        [ReadOnly] public EntityTypeHandle EntityType;
        public ComponentTypeHandle<Agent> AgentType;
        public BufferTypeHandle<Path> PathType;

        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            var entities = batchInChunk.GetNativeArray(EntityType);
            var agents = batchInChunk.GetNativeArray(AgentType);
            var paths = batchInChunk.GetBufferAccessor(PathType);
            
            for (int i = 0; i < agents.Length; i++)
            {
                var entity = entities[i];
                var agent = agents[i];
                var path = paths[i];

                //DetectNextWaypointJob
                if (path.Length > 0 && agent.status == Status.PathQueued)
                {

                    
                    //Debug.Log("PathQueued");
                    agent.status = Status.Moving;
                    agent.nextWaypointIndex = 1; // why 1 not 0?
                    agent.totalWaypoints = path.Length;
                    agent.currentWaypoint = path[0].Corner;
                    agent.remainingDistance = math.distance (agent.position, agent.currentWaypoint);
                    agents[i] = agent;
                    

                }

                if (agent.remainingDistance - agent.stoppingDistance > 0)// || agent.status != AgentStatus.Moving)
                {
                    //Debug.Log("remainingDistance" + (agent.remainingDistance - agent.stoppingDistance));
                    //if(!agent.currentWaypoint.Equals(float3.zero))
                        continue; //jiggle if commented out
                    //agents[i] = agent;
                }
        
                if (agent.nextWaypointIndex != agent.totalWaypoints) 
                {
                    //Debug.Log("nextWaypointIndex");
                    // todo find out how to prevent: there is an occasional out of range error with ijobparallel queries
                    if(agent.nextWaypointIndex>=path.Length)
                        agent.currentWaypoint = path[path.Length-1].Corner;
                    else
                        agent.currentWaypoint = path[agent.nextWaypointIndex].Corner;
                    
                    agent.remainingDistance = math.distance(agent.position, agent.currentWaypoint);
                    agent.nextWaypointIndex++;
                    agents[i] = agent;
                    
                }
                else if (/*NavMeshQuerySystemVersion != agent.queryVersion || */agent.nextWaypointIndex == agent.totalWaypoints)
                {
                    //Debug.Log("iamdone");
                    agent.totalWaypoints = 0;
                    agent.currentWaypoint = 0;
                    agent.status = Status.Idle;
                    agent.nextWaypointIndex = 0;
                        //agent.remainingDistance = 0;
                        path.Clear(); //reintroduce clear as it appears fixed previous bug.
                                      //however need to test for an extended time with burst off to fully ensure this is thread safe
                    agents[i] = agent;
                }
            }
        }
    }
    
    [BurstCompile]
    private struct MoveJob : IJobEntityBatch
    {
        public ComponentTypeHandle<Agent> AgentType;
        public ComponentTypeHandle<LocalToWorld> LocalToWorldType;
        public BufferTypeHandle<Path> PathType;
        public float DeltaTime;

        public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
        {
            var agents = batchInChunk.GetNativeArray(AgentType);
            var localToWorlds = batchInChunk.GetNativeArray(LocalToWorldType);
            var paths = batchInChunk.GetBufferAccessor(PathType);
            
            for (var i = 0; i < agents.Length; i++)
            {
                var agent = agents[i];
                var localToWorld = localToWorlds[i];
                var path = paths[i];
                
                //Draw Debug
                {
                    DrawDebug(agent, path);
                }
                
                if (agent.status != Status.Moving)
                    continue;

                if (agent.remainingDistance > 0)
                {
                    agent.currentMoveSpeed = math.lerp (agent.currentMoveSpeed, agent.moveSpeed, DeltaTime * agent.acceleration);
                    // todo: deceleration
                    if (!agent.nextPosition.x.Equals(math.INFINITY))
                        agent.position = agent.nextPosition;
                    
                    var heading = agent.currentWaypoint - agent.position;
                    
                    agent.remainingDistance = math.length(heading);// heading.magnitude;
                    if (agent.remainingDistance > 0.001f)
                    {
                        var up = math.mul(agent.rotation, maths.up);
                        var targetRotation = Quaternion.LookRotation (heading, up).eulerAngles;
                        //targetRotation.x = targetRotation.z = 0;
                        if (agent.remainingDistance < 1)
                        {
                            agent.rotation = Quaternion.Euler (targetRotation);
                            //agent.rotation = quaternion.Euler(targetRotation);
                        }
                        else
                        {
                            agent.rotation = Quaternion.Slerp (agent.rotation, Quaternion.Euler (targetRotation), DeltaTime * agent.rotationSpeed);
                            //agent.rotation = math.slerp(agent.rotation, quaternion.Euler (targetRotation), DeltaTime * agent.rotationSpeed);
                        }
                        //agent.rotation = quaternion.LookRotationSafe(heading, maths.up);
                    }
                    var forward = math.forward(agent.rotation) * agent.currentMoveSpeed * DeltaTime;
                    agent.nextPosition = agent.position + forward;
                    agents[i] = agent;
                }
                else if (agent.nextWaypointIndex == agent.totalWaypoints)
                {
                    agent.nextPosition = new float3 { x = math.INFINITY, y = math.INFINITY, z = math.INFINITY }; //huh why infinity?
                    agent.status = Status.Idle;
                    agents[i] = agent;
                }
            }
        }

        private static void DrawDebug(Agent agent, DynamicBuffer<Path> path)
        {
            var dir = agent.currentWaypoint - agent.position;
            Debug.DrawRay(agent.position, math.normalize(dir), Color.magenta);
            Debug.DrawRay(agent.currentWaypoint, maths.up * 0.2f, Color.blue);

            if (path.Length > 0)
            {
                for (var p = 0; p < path.Length; p++)
                {
                    Debug.DrawRay(path[p].Corner, maths.up * 0.2f, Color.green);


                    if (path.Length > 1)
                        Debug.DrawLine(path[0].Corner, path[1].Corner, Color.yellow);
                }
            }
        }
    }
    


}
