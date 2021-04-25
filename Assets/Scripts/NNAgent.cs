using System.Collections;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Policies;

public class NNAgent : Agent
{
    #region Behaviours
    public enum State
    {
        // Three separate "states" an agent can be in.
        // Separate from learning algorithm states/units but acts as a FSM of sorts.
        Generic = 0, Offensive = 1, Defensive = 2
    }
    public State equippedState;
    #endregion
    #region Training Agent vars
    [HideInInspector]
    public enum Team
    {
        // Separates learners into adversarial teams.
        Team0 = 0,
        Team1 = 1
    }
    public Team team;
    [HideInInspector] public Renderer agentRender;
    [HideInInspector] public Material teamColor;

    private NNAgent mlAgent;
    private BehaviorParameters behaviourParams;
    [HideInInspector] public GameObject perceivedObject;
    [HideInInspector] public GameObject homeBase;
    private Rigidbody agentRB;
    private float moveSpeed = 10.0f;
    private float turnSpeed = 110.0f;
    [SerializeField] private int remainingHealth;
    private Transform bulletOrigin;
    [SerializeField] private int remainingAmmo;
    [SerializeField] private int ammoReserve;
    [SerializeField] private bool canFire;
    [SerializeField] private bool reloading;
    [HideInInspector] public GameObject pickups;
    [HideInInspector] public bool pointGiven;
    #endregion
    #region Environment vars
    private TrainingEnvironment area;
    //float existentialEntropy;
    //public float penaltyOverTime;
    [HideInInspector] public bool winning;
    #endregion

    #region Action segments
    private void RotateSpace(int act)
    {
        Vector3 rotDir = Vector3.zero;

        switch (act)
        {
            case 0:
                // Do nothing on next action decision.
                break;
            case 1:
                rotDir = this.transform.up; // Rotate up Y axis for next action.
                break;
            case 2:
                rotDir = -this.transform.up; // Rotate down Y axis for next action.
                break;
        }

        agentRB.transform.Rotate(rotDir, turnSpeed * Time.deltaTime); // Call Rotate to apply chosen rotation action to transform.
    }

    private void MoveSpace(int action)
    {
        Vector3 dir = Vector3.zero;

        if (action == this.GetAction()[0])
        {
            dir = transform.right;
        }
        else
        {
            dir = transform.forward;
            
        }

        switch (action)
        {
            case 0:
                // Do nothing on next action decision.
                break;
            case 1:
                agentRB.AddForce(dir * moveSpeed, ForceMode.VelocityChange); // Move forward on selected axis for next action.
                break;
            case 2:
                agentRB.AddForce(-dir * moveSpeed, ForceMode.VelocityChange); // Move backward on selected axis for next action.
                break;
        }
    }
    
    private void ShootSpace(int shootAction)
    {
        var layerMask = 1 << LayerMask.NameToLayer("Actors") | 1 << LayerMask.NameToLayer("Environment");
        Vector3 direction = transform.forward;
        switch (shootAction)
        {
            case 0:
                // Move forward on selected axis for next action.
                break;
            case 1:
                // Declare var for if the agent's raycast "bullet" collides with anything.
                RaycastHit hit;
                // Shooting logic for both teams for opponent/wall ray collisions.
                if (Physics.Raycast(bulletOrigin.position, direction, out hit, 24.5f, layerMask) && canFire)
                {
                    if (hit.transform.gameObject.tag == "Red" && this.team == Team.Team1)
                    {
                        hit.transform.GetComponent<NNAgent>().TakenHit(30, this);
                        Debug.DrawRay(bulletOrigin.position, direction * hit.distance, Color.black, 0.1f);
                    }
                    else if (hit.transform.gameObject.tag == "Blue" && this.team == Team.Team0)
                    {
                        hit.transform.GetComponent<NNAgent>().TakenHit(30, this);
                        Debug.DrawRay(bulletOrigin.position, direction * hit.distance, Color.black, 0.1f);
                    }
                    else if (hit.transform.gameObject.tag == "Wall")
                    {
                        Debug.DrawRay(this.bulletOrigin.position, direction * hit.distance, Color.black, 0.1f);
                    }
                }
                else
                {
                    Debug.DrawRay(this.bulletOrigin.position, direction * 24.25f, Color.black, 0.1f);
                }
                this.remainingAmmo--; // Deduct ammo for each shot taken.
               
                // Throw agent into reload coroutine when its out of ammo.
                if (this.remainingAmmo <= 0 && this.ammoReserve > 0)
                {
                    this.remainingAmmo = 0;
                    this.StartCoroutine(ReloadWeapon());
                }
                break;
            case 2:
                // Give agent opportunity to manually reload when there are rounds left in their magazine.
                if ((this.remainingAmmo < 20 && this.remainingAmmo > 0) && this.ammoReserve > 0)
                {
                    this.StartCoroutine(ReloadWeapon());
                }                
                break;
        }


    }
    #endregion
    public static GameObject RayPerceptions(RayPerceptionInput input, Rigidbody rB, bool frontSensors)
    {
        RaycastHit detectorRay = new RaycastHit(); // Ray for casting in direction of ray sensors.

        // If this method is called for an agent's "cone-of-vision" front-facing sensors:
        if (frontSensors)
        {
            // For every ray sensor:
            for (int i = 0; i < 13; i++)
            {
                Vector3 rayEnd = input.RayExtents(i).EndPositionWorld; // Find its end pos in world space.
                Vector3 rayStart = input.RayExtents(i).StartPositionWorld; // Also find its start pos in world space.
                float distToObject = Vector3.Distance(rayStart, rayEnd); // Determine distance between a ray sensor's start and end points.
                Vector3 dirToObject = (rayEnd - rayStart).normalized; // Determine direction of perceieved object from this game object.

                // Perform a SweepTest of this object's Rigidbody component using each RayPerceptionSensor3D (RPS3D) ray as origin/end point for current array instance.
                // If a ray touches something, the perceived game object will be returned:
                if (rB.SweepTest(dirToObject * distToObject, out detectorRay, 25.0f))
                {
                    return detectorRay.collider.transform.gameObject; // Return what was hit.
                }
            }
        }
        // Else this method is being called for an agent's child "flank/blind spot" side and back-facing sensors:
        else
        {
            // For every ray sensor:
            for (int i = 0; i < 13; i++)
            {
                Vector3 rayEnd = input.RayExtents(i).EndPositionWorld; // Find its end pos in world space.
                Vector3 rayStart = input.RayExtents(i).StartPositionWorld; // Also find its start pos in world space.
                float distToObject = Vector3.Distance(rayStart, rayEnd); // Determine distance between a ray sensor's start and end points.
                Vector3 dirToObject = (rayEnd - rayStart).normalized; // Determine direction of perceieved object from this game object.

                // Perform a SweepTest of this object's Rigidbody component using each RayPerceptionSensor3D (RPS3D) ray as origin/end point for current array instance.
                // If a ray touches something, the perceived game object will be returned:
                if (rB.SweepTest(dirToObject * distToObject, out detectorRay, 15.0f))
                {
                    return detectorRay.collider.transform.gameObject; // Return what was hit.
                }
            }
        }


        return null; // If none of the ray sensors hit anything return null for now.
    }

    private void BehaviourState(State currentBehaviour, int moveX, int moveZ, int rotY, int shootVec)
    {
        switch (currentBehaviour)
        {
            // Wander through map, find game objects for ray sensors to see.
            // Generic State cannot shoot, only move and/or rotate.
            case State.Generic:
                MoveSpace(moveX); 
                MoveSpace(moveZ);
                RotateSpace(rotY);

                // Red team/Team 0
                if (mlAgent.team == Team.Team0)
                {
                    // Create two RPS3D var for passing to custom perceptions method, one for cone-of-vision & one for flanks and iterate through both of them.
                    RayPerceptionSensorComponent3D coneOfVision0 = this.gameObject.GetComponent<RayPerceptionSensorComponent3D>();
                    RayPerceptionSensorComponent3D coneOfVision1 = this.transform.Find("ChildRays").GetComponent<RayPerceptionSensorComponent3D>();
                    for (int i = 0; i < coneOfVision0.RaysPerDirection; i++)
                    {
                        // Create new game object var for each iteration.
                        GameObject canSeeObject0 = RayPerceptions(coneOfVision0.GetRayPerceptionInput(), mlAgent.agentRB, true);
                        
                        // If perception sensors pass over/collide with another game object:
                        if (canSeeObject0 != null)
                        {
                            // If it's an opponent:
                            if (canSeeObject0.tag == "Blue")
                            {
                                this.perceivedObject = canSeeObject0;
                                equippedState = State.Offensive; // Switch to an offensive state and chase/shoot opponent(s).
                            }
                        }
                    }
                    for (int i = 0; i < coneOfVision1.RaysPerDirection; i++)
                    {
                        // Create new game object var for each iteration.
                        GameObject canSeeObject1 = RayPerceptions(coneOfVision1.GetRayPerceptionInput(), mlAgent.agentRB, false);

                        // If perception sensors pass over/collide with another game object:
                        if (canSeeObject1 != null)
                        {
                            // If it's an opponent:
                            if (canSeeObject1.tag == "Blue")
                            {
                                this.perceivedObject = canSeeObject1;
                                equippedState = State.Offensive; // Switch to an offensive state and chase/shoot opponent(s).
                            }
                        }
                    }
                }
                // Blue team/Team 1
                else if (mlAgent.team == Team.Team1)
                {
                    // Create two RPS3D var for passing to custom perceptions method, one for cone-of-vision & one for flanks and iterate through both of them.
                    RayPerceptionSensorComponent3D coneOfVision0 = this.gameObject.GetComponent<RayPerceptionSensorComponent3D>();
                    RayPerceptionSensorComponent3D coneOfVision1 = this.transform.Find("ChildRays").GetComponent<RayPerceptionSensorComponent3D>();

                    for (int i = 0; i < coneOfVision0.RaysPerDirection; i++)
                    {
                        // Create new game object var for each iteration.
                        GameObject canSeeObject0 = RayPerceptions(coneOfVision0.GetRayPerceptionInput(), mlAgent.agentRB, true);

                        // If perception sensors pass over/collide with another game object:
                        if (canSeeObject0 != null)
                        {
                            // If it's an opponent:
                            if (canSeeObject0.tag == "Red")
                            {
                                this.perceivedObject = canSeeObject0;
                                equippedState = State.Offensive; // Switch to an offensive state and chase/shoot opponent(s).
                            }
                        }
                    }
                    for (int i = 0; i < coneOfVision1.RaysPerDirection; i++)
                    {
                        // Create new game object var for each iteration.
                        GameObject canSeeObject1 = RayPerceptions(coneOfVision1.GetRayPerceptionInput(), mlAgent.agentRB, false);

                        // If perception sensors pass over/collide with another game object:
                        if (canSeeObject1 != null)
                        {
                            // If it's an opponent:
                            if (canSeeObject1.tag == "Red")
                            {
                                this.perceivedObject = canSeeObject1;
                                equippedState = State.Offensive; // Switch to an offensive state and chase/shoot opponent(s).
                            }
                        }
                    }
                }
                break;
            // Offensive agents can move and/or rotate, shoot and chase other agents.
            case State.Offensive:
                MoveSpace(moveX);
                MoveSpace(moveZ);
                RotateSpace(rotY);                

                // Red team/Team 0
                if (mlAgent.team == Team.Team0)
                {
                    // Create two RPS3D var for passing to custom perceptions method, one for cone-of-vision & one for flanks and iterate through both of them.
                    RayPerceptionSensorComponent3D coneOfVision0 = this.gameObject.GetComponent<RayPerceptionSensorComponent3D>();
                    RayPerceptionSensorComponent3D coneOfVision1 = this.transform.Find("ChildRays").GetComponent<RayPerceptionSensorComponent3D>();

                    for (int i = 0; i < coneOfVision0.RaysPerDirection; i++)
                    {
                        // Create new game object var for each iteration.
                        GameObject canSeeObject0 = RayPerceptions(coneOfVision0.GetRayPerceptionInput(), mlAgent.agentRB, true);

                        // If perception sensors pass over/collide with another game object:
                        if (canSeeObject0 != null)
                        {
                            // If it's an opponent:
                            if (canSeeObject0.tag == "Blue")
                            {
                                this.perceivedObject = canSeeObject0;                              
                                ChaseOrAvoid(canSeeObject0, 0.01f, true); // Chase opponent(s).
                                ShootSpace(shootVec); // Agent can aim/shoot at opponent(s).

                                // If the agent has low health (only when looking at opponent):
                                if (this.remainingHealth <= 10)
                                {
                                    float rand = Random.Range(-1, 1);

                                    // Random chance for agent to switch to offensive state.
                                    if (rand > 0)
                                    {
                                        equippedState = State.Defensive;
                                    }
                                }
                            }
                        }
                    }
                    for (int i = 0; i < coneOfVision1.RaysPerDirection; i++)
                    {
                        // Create new game object var for each iteration.
                        GameObject canSeeObject1 = RayPerceptions(coneOfVision1.GetRayPerceptionInput(), mlAgent.agentRB, false);

                        // If perception sensors pass over/collide with another game object:
                        if (canSeeObject1 != null)
                        {
                            // If it's an opponent:
                            if (canSeeObject1.tag == "Blue")
                            {
                                this.perceivedObject = canSeeObject1;
                                agentRB.MoveRotation(Quaternion.LookRotation(new Vector3(canSeeObject1.transform.localPosition.x, 0, canSeeObject1.transform.localPosition.z))); // Rotate to face opponent.
                                agentRB.AddForce(-(canSeeObject1.transform.localPosition - this.transform.localPosition).normalized * (moveSpeed * 0.5f), ForceMode.VelocityChange); // Propel agent away from opponent slightly.
                            }
                        }
                    }
                }
                // Blue team/Team 1
                else if (mlAgent.team == Team.Team1)
                {
                    // Create two RPS3D var for passing to custom perceptions method, one for cone-of-vision & one for flanks and iterate through both of them.
                    RayPerceptionSensorComponent3D coneOfVision0 = this.gameObject.GetComponent<RayPerceptionSensorComponent3D>();
                    RayPerceptionSensorComponent3D coneOfVision1 = this.transform.Find("ChildRays").GetComponent<RayPerceptionSensorComponent3D>();

                    for (int i = 0; i < coneOfVision0.RaysPerDirection; i++)
                    {
                        // Create new game object var for each iteration.
                        GameObject canSeeObject0 = RayPerceptions(coneOfVision0.GetRayPerceptionInput(), mlAgent.agentRB, true);

                        // If perception sensors pass over/collide with another game object:
                        if (canSeeObject0 != null)
                        {
                            // If it's an opponent:
                            if (canSeeObject0.tag == "Red")
                            {
                                this.perceivedObject = canSeeObject0;
                                ChaseOrAvoid(canSeeObject0, 0.01f, true); // Chase opponent(s).
                                ShootSpace(shootVec); // Agent can aim/shoot at opponent(s).

                                // If the agent has low health (only when looking at opponent):
                                if (this.remainingHealth <= 10)
                                {
                                    float rand = Random.Range(-1, 1);

                                    // Random chance for agent to switch to offensive state.
                                    if (rand > 0)
                                    {
                                        equippedState = State.Defensive;
                                    }
                                }
                            }
                        }
                    }

                    for (int i = 0; i < coneOfVision1.RaysPerDirection; i++)
                    {
                        // Create new game object var for each iteration.
                        GameObject canSeeObject1 = RayPerceptions(coneOfVision1.GetRayPerceptionInput(), mlAgent.agentRB, false);

                        // If perception sensors pass over/collide with another game object:
                        if (canSeeObject1 != null)
                        {
                            // If it's an opponent:
                            if (canSeeObject1.tag == "Red")
                            {
                                this.perceivedObject = canSeeObject1;
                                agentRB.MoveRotation(Quaternion.LookRotation(new Vector3(canSeeObject1.transform.localPosition.x, 0, canSeeObject1.transform.localPosition.z))); // Rotate to face opponent.
                                agentRB.AddForce(-(canSeeObject1.transform.localPosition - this.transform.localPosition).normalized * (moveSpeed * 0.5f), ForceMode.VelocityChange); // Propel agent away from opponent slightly.
                            }
                        }
                    }
                }
                break;
            // Defensive agents can move and/or rotate, shoot and retreat away from other agents.
            case State.Defensive:
                MoveSpace(moveX);
                MoveSpace(moveZ);
                RotateSpace(rotY);

                // Red team/Team 0
                if (mlAgent.team == Team.Team0)
                {
                    // Create two RPS3D var for passing to custom perceptions method, one for cone-of-vision & one for flanks and iterate through both of them.
                    RayPerceptionSensorComponent3D coneOfVision0 = this.gameObject.GetComponent<RayPerceptionSensorComponent3D>();
                    RayPerceptionSensorComponent3D coneOfVision1 = this.transform.Find("ChildRays").GetComponent<RayPerceptionSensorComponent3D>();
                    for (int i = 0; i < coneOfVision0.RaysPerDirection; i++)
                    {
                        // Create new game object var for each iteration.
                        GameObject canSeeObject0 = RayPerceptions(coneOfVision0.GetRayPerceptionInput(), mlAgent.agentRB, true);

                        // If perception sensors pass over/collide with another game object:
                        if (canSeeObject0 != null)
                        {
                            // If it's an opponent:
                            if (canSeeObject0.tag == "Blue")
                            {
                                this.perceivedObject = canSeeObject0;
                                ChaseOrAvoid(canSeeObject0, -0.001f, false); // Evade opponent(s).
                                ShootSpace(shootVec); // Agent can aim/shoot at opponent(s).
                                
                                // If the agent has low health:
                                if (this.remainingHealth <= 25)
                                {
                                    float rand = Random.Range(-1, 1);
                                    // Random chance for agent to switch to offensive state.
                                    if (rand > 0)
                                    {
                                        equippedState = State.Offensive;
                                    }
                                }
                            }
                        }
                    }

                    for (int i = 0; i < coneOfVision1.RaysPerDirection; i++)
                    {
                        // Create new game object var for each iteration.
                        GameObject canSeeObject1 = RayPerceptions(coneOfVision1.GetRayPerceptionInput(), mlAgent.agentRB, false);

                        // If perception sensors pass over/collide with another game object:
                        if (canSeeObject1 != null)
                        {
                            // If it's an opponent:
                            if (canSeeObject1.tag == "Blue")
                            {
                                this.perceivedObject = canSeeObject1;
                                ChaseOrAvoid(canSeeObject1, -0.001f, false); // Evade opponent(s).

                                // If the agent has low health:
                                if (this.remainingHealth <= 25)
                                {
                                    float rand = Random.Range(-1, 1);

                                    // Random chance for agent to switch to offensive state.
                                    if (rand > 0)
                                    {
                                        equippedState = State.Offensive;
                                    }
                                }
                            }
                        }
                    }
                }
                // Blue team/team 1
                else if (mlAgent.team == Team.Team1)
                {
                    // Create two RPS3D var for passing to custom perceptions method, one for cone-of-vision & one for flanks and iterate through both of them.
                    RayPerceptionSensorComponent3D coneOfVision0 = this.gameObject.GetComponent<RayPerceptionSensorComponent3D>();
                    RayPerceptionSensorComponent3D coneOfVision1 = this.transform.Find("ChildRays").GetComponent<RayPerceptionSensorComponent3D>();

                    for (int i = 0; i < coneOfVision0.RaysPerDirection; i++)
                    {
                        // Create new game object var for each iteration.
                        GameObject canSeeObject0 = RayPerceptions(coneOfVision0.GetRayPerceptionInput(), mlAgent.agentRB, true);

                        // If perception sensors pass over/collide with another game object:
                        if (canSeeObject0 != null)
                        {
                            // If it's an opponent:
                            if (canSeeObject0.tag == "Red")
                            {
                                this.perceivedObject = canSeeObject0;
                                ChaseOrAvoid(canSeeObject0, -0.001f, false); // Evade opponent(s).
                                ShootSpace(shootVec); // Agent can aim/shoot at opponent(s).

                                // If the agent has low health
                                if (this.remainingHealth <= 25)
                                {
                                    float rand = Random.Range(-1, 1);

                                    // Random chance for agent to switch to offensive state.
                                    if (rand > 0)
                                    {
                                        equippedState = State.Offensive;
                                    }
                                }
                            }
                        }
                    }

                    for (int i = 0; i < coneOfVision1.RaysPerDirection; i++)
                    {
                        // Create new game object var for each iteration.
                        GameObject canSeeObject1 = RayPerceptions(coneOfVision1.GetRayPerceptionInput(), mlAgent.agentRB, false);

                        // If perception sensors pass over/collide with another game object:
                        if (canSeeObject1 != null)
                        {
                            // If it's an opponent:
                            if (canSeeObject1.tag == "Red")
                            {
                                this.perceivedObject = canSeeObject1;
                                ChaseOrAvoid(canSeeObject1, -0.001f, false); // Evade opponent(s).

                                // If the agent has low health
                                if (this.remainingHealth <= 25)
                                {
                                    float rand = Random.Range(-1, 1);

                                    // Random chance for agent to switch to offensive state.
                                    if (rand > 0)
                                    {
                                        equippedState = State.Offensive;
                                    }
                                }
                            }
                        }
                    }
                }
                break;
        }
    }

    // Called to declare action vars and calls BehaviourState() to place agent actions within context of agent's current situation.
    private void PolicyAction(ActionSegment<int> vecActs)
    {
        // Convert any actions and discrete action segments into numerics to be represented by learning algorithm.
        int xAxis = (int)vecActs[0]; // Move down/up x axis.
        int zAxis = (int)vecActs[1]; // Move down/up z axis.
        int yAxis = (int)vecActs[2]; // Rotate down/up y axis.
        int shootAxis = (int)vecActs[3]; // Shoot projectile.

        // Let algorithm choose next behaviour and action an agent should execute.
        BehaviourState(equippedState, xAxis, zAxis, yAxis, shootAxis);
        
        if (this.agentRB.velocity.sqrMagnitude < 1.0f)
        {
            this.AddReward(-0.0001f); // Punish agents for slowing down too much.
        }

        if (agentRB.velocity.sqrMagnitude > 5f)
        {
            agentRB.velocity *= 0.025f; // Truncate agent speed when it gets too high.
        }

        if (this.winning)
        {
            this.AddReward(0.01f); // Reward agents for being on winning side during round.
        }
    }

    public void ChaseOrAvoid(GameObject opponent, float reward, bool chase)
    {
        // Switchable between offensive and defensive states.        
        if (chase == false)
        {
            agentRB.MoveRotation(Quaternion.LookRotation(-new Vector3(opponent.transform.localPosition.x, 0, opponent.transform.localPosition.z)));
            agentRB.AddForce(-(opponent.transform.localPosition - this.transform.localPosition).normalized * (moveSpeed * 0.5f), ForceMode.VelocityChange);            
        }
        else
        {
            agentRB.MoveRotation(Quaternion.LookRotation(new Vector3(opponent.transform.localPosition.x, 0, opponent.transform.localPosition.z), Vector3.up));
            agentRB.AddForce((opponent.transform.localPosition - this.transform.localPosition).normalized * (moveSpeed * 0.5f), ForceMode.VelocityChange);
        }

        this.AddReward(reward); // Inverse reward depending on whether agent is chasing or fleeing an opponent agent.
    }

    public override void Initialize()
    {
        // Initialize agent vars        
        pickups = transform.parent.Find("Pickups").gameObject;
        mlAgent = this;
        agentRender = this.GetComponent<Renderer>();
        teamColor = agentRender.material;
        mlAgent.equippedState = State.Generic;
        area = transform.parent.GetComponent<TrainingEnvironment>();

        //existentialEntropy = 1f / MaxStep;
        agentRB = this.GetComponent<Rigidbody>();
        bulletOrigin = gameObject.transform.GetChild(0).transform;
        behaviourParams = gameObject.GetComponent<BehaviorParameters>();

        // Assign team colors.
        if (behaviourParams.TeamId == (int)Team.Team0)
        {
            mlAgent.team = Team.Team0;
            this.teamColor = Resources.Load("Materials/Actors/RedTeam", typeof(Material)) as Material;
            agentRender.material = this.teamColor;
        }
        else if (behaviourParams.TeamId == (int)Team.Team1)
        {
            mlAgent.team = Team.Team1;
            this.teamColor = Resources.Load("Materials/Actors/BlueTeam", typeof(Material)) as Material;
            agentRender.material = this.teamColor;
        }
    }
    public override void OnEpisodeBegin()
    {
        // Activate pickups if the training session has progressed long enough:
        if (area.session.TotalStepCount / 4 >= 12000)
        {
            pickups.SetActive(true);
        }
        else
        {
            pickups.SetActive(false);
        }

        this.Spawn(); // Reset agents' vars and spawn them into the map.
        perceivedObject = null; // Reset perceived game object var.
        pointGiven = false; // No points earned for current round yet.
    }
    private void Spawn()
    {
        // Spawn Red team/Team 0 at their respective home base:
        if (this.team == Team.Team0)
        {
            this.transform.localPosition = new Vector3(Random.Range(-3, 3), -0.5f, Random.Range(-3, 3)) + transform.parent.Find("RedBase").transform.localPosition;
            this.transform.rotation = Quaternion.Euler(new Vector3(0, 180, 0));
        }
        // Spawn Blue team/Team 1 at their respective home base:
        else if (this.team == Team.Team1)
        {
            this.transform.localPosition = new Vector3(Random.Range(-3, 3), -0.5f, Random.Range(-3, 3)) + transform.parent.Find("BlueBase").transform.localPosition;
            this.transform.rotation = Quaternion.Euler(Vector3.zero);
        }

        this.equippedState = State.Generic; // Reset agent state to generic.
        
        this.agentRB.velocity = Vector3.zero; // Reset agent velocity (x, y, z) to zero.
        this.remainingHealth = area.base_maxHealth; // Reset agent health to maximum.
        this.reloading = false; // Reset so agent is not reloading at beginning of episode.
        this.ammoReserve = area.maxAmmoReserve; // Reset agent ammunition reserve to maximum.
        this.remainingAmmo = 20; // Reset agent magazine ammunition to maximum.

        // not working, have to hard code to value above.
        //this.remainingAmmo = area.base_maxAmmo;

        // If an agent is eliminated mid-round and calls Spawn() then update the score system:
        if (this.remainingHealth <= 0)
        {
            if (this.team == Team.Team0)
            {
                area.scoreSystem -= 1;
                pointGiven = true;
            }
            else if (this.team == Team.Team1)
            {
                area.scoreSystem += 1;
                pointGiven = true;
            }
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(transform.InverseTransformDirection(this.agentRB.velocity)); // Observe an agent's velocity.
        sensor.AddObservation((int)equippedState); // Observe which state of behaviour an agent is in.
        sensor.AddOneHotObservation(System.Convert.ToInt32(winning), 1); // Observe if an agent's team is currently winning.
    }

    public override void OnActionReceived(ActionBuffers vAct)
    {
        PolicyAction(vAct.DiscreteActions); // Call to PolicyAction() to convert action segements into discrete action space.
    }
    
    
    public override void Heuristic(in ActionBuffers actsOut)
    {
        // Manually control/test action space.
        var actOut = actsOut.DiscreteActions;
        actOut[0] = 0;
        actOut[1] = 0;
        actOut[2] = 0;
        actOut[3] = 0;

        // MoveSpacement +z
        if (Input.GetKey(KeyCode.W))
        {
            actOut[1] = 1;
        }
        // MoveSpacement -x
        if (Input.GetKey(KeyCode.A))
        {
            actOut[0] = 2;
        }
        // MoveSpacement -z
        if (Input.GetKey(KeyCode.S))
        {
            actOut[1] = 2;
        }
        if (Input.GetKey(KeyCode.D))
        {
            actOut[0] = 1;
        }
        if (Input.GetKey(KeyCode.LeftArrow))
        {
            actOut[2] = 2;
        }
        if (Input.GetKey(KeyCode.RightArrow))
        {
            actOut[2] = 1;
        }
        if (Input.GetKey(KeyCode.Mouse0) && canFire)
        {
            actOut[3] = 1;
        }
        else if (Input.GetKey(KeyCode.R) && !reloading)
        {
            actOut[3] = 2;
        }
        else
        {
            actOut[3] = 0;
        }
    }
    #region Health methods
    private void TakenHit(int dmg, NNAgent shooter)
    {
        DecreaseHealth(dmg, shooter);
    }
    private void DecreaseHealth(int amount, NNAgent shooter)
    {
        this.remainingHealth -= amount;

        if (this.remainingHealth <= 0)
        {
            shooter.AddReward(0.5f);
            this.AddReward(-0.5f);
            this.pointGiven = true;
            this.Spawn();
        }
    }
    #endregion

    private void Update()
    {

        if (reloading)
        {
            canFire = false;
            return;
        }
        else
        {
            canFire = true;
        }
        if (this.remainingAmmo <= 0)
        {
            canFire = false;
            reloading = false;
        }
    }

    IEnumerator ReloadWeapon()
    {
        this.reloading = true;
        this.canFire = false;
        yield return new WaitForSeconds(3.0f);

        // Triggers if ammunition reserves have been introduced:
        if (area.session.TotalStepCount / 4 >= 12000)
        {
            if (this.remainingAmmo > 0)
            {
                int output = 20 - this.remainingAmmo;
                this.ammoReserve -= output;
            }
            else if (this.remainingAmmo == 0)
            {

                this.ammoReserve -= 20;
            }


            if (this.ammoReserve >= 20)
            {
                this.remainingAmmo = 20;
            }
            else
            {
                this.remainingAmmo = this.ammoReserve;
            }
        }
        // Else set magazine ammunition to maximum if ammunition reserves have not been introduced:
        else
        {
            this.remainingAmmo = 20;
        }



        this.reloading = false;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.transform.tag == "Wall")
        {
            this.AddReward(-0.00001f);
        }        
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.tag == "Health" && remainingHealth < area.base_maxHealth)
        {
            remainingHealth += 40;
            this.AddReward(0.01f);

            if (remainingHealth > area.base_maxHealth)
            {
                remainingHealth = area.base_maxHealth;
            }else if (remainingHealth <= 40)
            {
                this.AddReward(1.0f);
            }
            StartCoroutine(area.HidePickup(other.gameObject));
        }

        if (other.gameObject.tag == "Ammo" && ammoReserve < area.maxAmmoReserve)
        {
            ammoReserve += 30;
            this.AddReward(0.01f);

            if (ammoReserve > area.maxAmmoReserve)
            {
                ammoReserve = area.maxAmmoReserve;
            }
            else if (ammoReserve <= 60)
            {
                this.AddReward(1.0f);
            }
            StartCoroutine(area.HidePickup(other.gameObject));
        }
    }
}




