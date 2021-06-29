using thelebaron.mathematics;
using Unity.Entities;
using Unity.Mathematics;

public class AgentDestinationSystem : SystemBase
{
    private NavMeshQuerySystem querySystem;
    protected override void OnCreate()
    {
        base.OnCreate();
        querySystem = World.GetOrCreateSystem<NavMeshQuerySystem>();
    }

    protected override void OnUpdate()
    {
        Entities.WithNone<AgentRandom>().ForEach((Entity entity, ref Agent agent) =>
        {
            EntityManager.AddComponentData(entity, new AgentRandom{ Rand = new Random((uint)UnityEngine.Random.Range(1,1234))});
        }).WithStructuralChanges().Run();
        
        
        Entities.ForEach((Entity entity, ref Agent agent, ref AgentRandom agentRandom, ref WanderTimer wanderTimer) =>
        {
            if (agent.status != Status.Idle && agent.status != Status.Requested)
                return;
            
            var dest = new float3(agentRandom.Rand.NextFloat(-7,7),agentRandom.Rand.NextFloat(-7,7),agentRandom.Rand.NextFloat(-7,7));
            agent.destination = dest + agent.position;
            agent.status          = Status.Requested;
            agent.pathQueryStatus = PathQueryStatus.Pending;
            wanderTimer.Time      = agentRandom.Rand.NextFloat(0, 5);

        }).Schedule();
    }
    
    /// Used to set an agent destination and start the pathfinding process
    private void SetDestination (Entity entity, Agent agent, float3 destination, int areas = -1)
    {
        agent.status       = Status.PathQueued;
        agent.destination  = destination;
        agent.queryVersion = querySystem.Version;
        EntityManager.SetComponentData(entity, agent);
        querySystem.RequestPath (entity, agent.position, agent.destination, areas);
    }
}