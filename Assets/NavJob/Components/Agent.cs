using thelebaron.mathematics;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.AI;

public class dummy
{
    public NavMeshAgent NavMeshAgent;
}
    public enum Status
    {
        Idle = 0,
        PathQueued = 1,
        Moving = 2,
        Paused = 4,
        Requested = 8,
    }

    [System.Serializable]
    public struct Agent : IComponentData
    {
        public float stoppingDistance;
        public float moveSpeed;
        public float acceleration;
        public float rotationSpeed;
        public float height;
        public int areaMask;
        
        public float3 destination;
        public Status status;
        
        public float currentMoveSpeed;
        public int queryVersion;
        public float3 position;
        public float3 nextPosition;
        public Quaternion rotation;
        public float remainingDistance;
        public float3 currentWaypoint;
        public int nextWaypointIndex;
        public int totalWaypoints;
        public PathQuery query;
        public global::PathQueryStatus pathQueryStatus;
        
        public Agent (
            float3 position,
            Quaternion rotation,
            float stoppingDistance = 1f,
            float moveSpeed = 4f,
            float acceleration = 1f,
            float rotationSpeed = 10f,
            float height = 2,
            int areaMask = -1
        )
        {
            this.stoppingDistance = stoppingDistance;
            this.moveSpeed = moveSpeed;
            this.acceleration = acceleration;
            this.rotationSpeed = rotationSpeed;
            this.height = height;
            this.areaMask = areaMask;
            destination = float3.zero;
            currentMoveSpeed = 0;
            queryVersion = 0;
            status = Status.Idle;
            this.position = position;
            this.rotation = rotation;
            nextPosition = new float3 (math.INFINITY, math.INFINITY, math.INFINITY);
            remainingDistance = 0;
            currentWaypoint = float3.zero;
            nextWaypointIndex = 0;
            totalWaypoints = 0;
            query = default;
            pathQueryStatus = default;
        }
        
        
        [System.Serializable]
        public struct PathQuery
        {
            public Entity Entity;
            public int PositionKey;
            public float3 From;
            public float3 To;
            public int AreaMask;
            public PathQueryStatus QueryStatus;
        }

        public enum PathQueryStatus
        {
            Requested, Pending, Complete,Invalid, Error,
        }
        
    }


    [System.Serializable]
    public struct AgentAvoidance : IComponentData
    {
        public float  radius;
        public float3 partition { get; set; }

        public AgentAvoidance (float radius = 1f)
        {
            this.radius    = radius;
            this.partition = new float3 (0);
        }
    }
            
    /// <summary>
    /// Transform components
    /// </summary>
    public struct CopyPositionFromNavAgent : IComponentData { }
    public struct CopyRotationToNavAgent : IComponentData { }

    public struct CopyRotationFromNavAgent : IComponentData { }

    public struct CopyPositionToNavAgent : IComponentData { }
    //public class NavAgentComponent : ComponentDataWrapper<NavAgent> { }
