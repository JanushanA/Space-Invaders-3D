using System;
using UnityEngine;
namespace Game.AI
{
    public static class FormationSolver
    {
        // Find the assignment with the smallest total squared travel distance.
        // With at most eight ships, checking all subsets of occupied slots stays inexpensive.
        public static int[] Assign(Vector3[] positions, Vector3[] slots)
        {
            int memberCount = positions.Length;
            if (memberCount != slots.Length || memberCount > 8)
            {
                throw new ArgumentException("Formation must have equal counts <= 8");
            }

            // Each bit identifies one occupied slot. State zero has no assigned ships.
            int assignmentStateCount = 1 << memberCount;
            var minimumCosts = new float[assignmentStateCount];
            var previousStates = new int[assignmentStateCount];
            var chosenSlots = new int[assignmentStateCount];

            for (int occupiedSlots = 1; occupiedSlots < assignmentStateCount; occupiedSlots++)
            {
                minimumCosts[occupiedSlots] = float.PositiveInfinity;
            }

            for (int occupiedSlots = 0; occupiedSlots < assignmentStateCount; occupiedSlots++)
            {
                // Ships are assigned in order, so the number of occupied slots identifies the next ship.
                int memberIndex = 0;

                for (int remainingSlotBits = occupiedSlots; remainingSlotBits != 0; remainingSlotBits >>= 1)
                {
                    memberIndex += remainingSlotBits & 1;
                }

                if (memberIndex >= memberCount)
                {
                    continue;
                }

                for (int slotIndex = 0; slotIndex < memberCount; slotIndex++)
                {
                    if ((occupiedSlots & (1 << slotIndex)) == 0)
                    {
                        int nextOccupiedSlots = occupiedSlots | (1 << slotIndex);
                        float assignmentCost = minimumCosts[occupiedSlots]
                            + (positions[memberIndex] - slots[slotIndex]).sqrMagnitude;
                        if (assignmentCost < minimumCosts[nextOccupiedSlots])
                        {
                            minimumCosts[nextOccupiedSlots] = assignmentCost;
                            previousStates[nextOccupiedSlots] = occupiedSlots;
                            chosenSlots[nextOccupiedSlots] = slotIndex;
                        }
                    }
                }
            }

            // Follow the cheapest path backward to recover one slot for each ship.
            var assignment = new int[memberCount];
            int assignmentState = assignmentStateCount - 1;

            for (int memberIndex = memberCount - 1; memberIndex >= 0; memberIndex--)
            {
                assignment[memberIndex] = chosenSlots[assignmentState];
                assignmentState = previousStates[assignmentState];
            }

            return assignment;
        }
    }

    public static class AvoidanceSolver
    {
        public static float CollisionRisk(Vector3 relativePosition, Vector3 relativeVelocity, float radius, float horizon)
        {
            // Evaluate the closest approach within the prediction window, assuming constant velocities.
            float relativeSpeedSquared = relativeVelocity.sqrMagnitude;
            float timeToClosestApproach = relativeSpeedSquared > 0.0001f
                ? Mathf.Clamp(-Vector3.Dot(relativePosition, relativeVelocity) / relativeSpeedSquared, 0, horizon)
                : 0;
            float closestDistance = (relativePosition + relativeVelocity * timeToClosestApproach).magnitude;
            return closestDistance >= radius
                ? 0
                : (radius - closestDistance) / radius * (1 + (horizon - timeToClosestApproach) / horizon);
        }

        public static Vector3 Limit(Vector3 current, Vector3 desired, float acceleration, float turnDegrees, float deltaTime)
        {
            Vector3 turnedVelocity = current.sqrMagnitude > 0.01f
                ? Vector3.RotateTowards(current, desired, turnDegrees * Mathf.Deg2Rad * deltaTime, acceleration * deltaTime)
                : desired;
            return Vector3.MoveTowards(current, turnedVelocity, acceleration * deltaTime);
        }

        // Compare a finite set of velocities; obstacles and neighbors do not reciprocate this choice.
        public static Vector3 Choose(Vector3 position, Vector3 current, Vector3 desired, float deltaTime,
            SquadronMember owner, SquadronMember[] neighbors, Collider[] obstacles, int obstacleCount)
        {
            Vector3 bestVelocity = Limit(current, Vector3.zero, 12, 95, deltaTime);
            float bestCost = float.PositiveInfinity;

            for (int candidateIndex = 0; candidateIndex < 18; candidateIndex++)
            {
                // Try the desired direction, stopping, then headings spaced around the horizontal plane.
                Vector3 candidateDesiredVelocity = candidateIndex == 0
                    ? desired
                    : candidateIndex == 1
                        ? Vector3.zero
                        : Quaternion.AngleAxis((candidateIndex - 2) * 22.5f, Vector3.up) * desired;
                Vector3 candidateVelocity = Limit(current, candidateDesiredVelocity, 12, 95, deltaTime);
                float candidateCost = (candidateVelocity - desired).sqrMagnitude * 0.025f;
                if (neighbors != null)
                {
                    foreach (var neighbor in neighbors)
                    {
                        if (neighbor != null && neighbor != owner && neighbor.isActiveAndEnabled)
                        {
                            candidateCost += 60 * CollisionRisk(
                                neighbor.transform.position - position,
                                neighbor.Velocity - candidateVelocity, 3.5f, 3);
                        }
                    }
                }

                for (int obstacleIndex = 0; obstacleIndex < obstacleCount; obstacleIndex++)
                {
                    Collider obstacle = obstacles[obstacleIndex];
                    if (obstacle == null || obstacle.isTrigger)
                    {
                        continue;
                    }

                    Vector3 closestPoint = obstacle.ClosestPoint(position);
                    Vector3 relativePosition = closestPoint - position;
                    candidateCost += 100 * CollisionRisk(relativePosition, -candidateVelocity, 2.8f, 3);
                }

                if (candidateCost < bestCost)
                {
                    bestCost = candidateCost;
                    bestVelocity = candidateVelocity;
                }
            }

            return bestVelocity;
        }
    }
}
