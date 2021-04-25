using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;

public class TrainingEnvironment : MonoBehaviour
{
    public List<NNAgent> agentsInParent = new List<NNAgent>(); // Tracks every agent in each prefab in the scene.
    public Academy session; // Training session var for making calls to Academy for the elapsed time of a training session, etc...
    public float roundTimer = 210.0f;
    public int base_maxHealth = 100;
    public int base_maxAmmo = 20;
    public int maxAmmoReserve = 120;
    public float base_Damage = 30.0f;
    public float reloadTime = 3.0f;
    public int scoreSystem = 0;
    public int stepsBetweenShots = 50;

    private void Awake()
    {
        ForEachAgent(); // Add all child agents to parent's list.
        session = Academy.Instance; // Initialize training session var.
    }

    void ForEachAgent()
    {
        foreach (Transform child in transform)
        {
            if (child.tag == "Red" || child.tag == "Blue")
            {
                agentsInParent.Add(child.GetComponent<NNAgent>());
            }

        }
    }

    public IEnumerator HidePickup(GameObject gameObj)
    {
        // Triggers when an agent enters a trigger box on a health/ammo "crate" game objects.
        gameObj.SetActive(false);
        yield return new WaitForSeconds(20.0f);
        gameObj.SetActive(true);
    }

    private void Update()
    {
        // Handles mid-round logic for which team is winning:
        if (roundTimer > 0)
        {
            // Deduct from round timer using delta time due to faster timescale being utilized.
            roundTimer -= Time.deltaTime;

            // If the score system is greater than zero then Red team/Team 0 are winning and Blue team/Team 1 are losing:
            if (scoreSystem > 0)
            {
                foreach (NNAgent na in agentsInParent)
                {
                    if (na.team == NNAgent.Team.Team0)
                    {
                        na.winning = true;
                    }
                    else if (na.team == NNAgent.Team.Team1)
                    {
                        na.winning = false;
                    }
                }
            }
            // Else if the score system is less than zero then Blue team/Team 1 are winning and Red team/Team 0 are losing:
            else if (scoreSystem < 0)
            {
                foreach (NNAgent na in agentsInParent)
                {
                    if (na.team == NNAgent.Team.Team0)
                    {
                        na.winning = false;
                    }
                    else if (na.team == NNAgent.Team.Team1)
                    {
                        na.winning = true;
                    }
                }
            }
            // Special if condition for when the score system is at a flat zero and neither team is winning:
            if (scoreSystem == 0)
            {
                foreach (NNAgent na in agentsInParent)
                {
                    if (na.team == NNAgent.Team.Team0 || na.team == NNAgent.Team.Team0)
                    {
                        na.winning = false;
                    }
                }
            }

        }
        // Handles end-of-round logic for which team won:
        if (roundTimer <= 0)
        {
            // If the score system is greater than zero then Red team/Team 0 have won and Blue team/Team 1 have lost:
            if (scoreSystem > 0)
            {
                foreach(NNAgent na in agentsInParent)
                {
                    if (na.team == NNAgent.Team.Team0)
                    {
                        na.AddReward(1.0f);
                    }
                    else if (na.team == NNAgent.Team.Team1)
                    {
                        na.AddReward(-1.0f);
                    }
                }
            }
            // Else if the score system is less than zero then Blue team/Team 1 have won and Red team/Team 0 have lost:
            else if (scoreSystem < 0)
            {
                foreach (NNAgent na in agentsInParent)
                {
                    if (na.team == NNAgent.Team.Team1)
                    {
                        na.AddReward(1.0f);
                    }
                    else if (na.team == NNAgent.Team.Team0)
                    {
                        na.AddReward(-1.0f);
                    }
                }
            }
            // Special if condition for when the score system is at a flat zero in which case neither team has won:
            if (scoreSystem == 0)
            {
                foreach (NNAgent na in agentsInParent)
                {
                    if (na.team == NNAgent.Team.Team0)
                    {
                        na.AddReward(0.0f);
                    }
                    else if (na.team == NNAgent.Team.Team1)
                    {
                        na.AddReward(0.0f);
                    }
                }


            }
            // End the episode so a new one can begin.
            foreach (NNAgent agent in agentsInParent)
            {
                agent.EndEpisode();
            }
            roundTimer = 210.0f; // Reset round timer.
            scoreSystem = 0; // Reset score counter.
        }
    }
}