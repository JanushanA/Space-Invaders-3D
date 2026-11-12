using UnityEngine;

// Measures ship bounds so enemies and shots can be placed without starting inside another hull.
namespace Game.Combat
{
    // Keeps hulls from occupying the same spot on the playfield (XZ, Y is up).
    // Keeps ships separated so they do not overlap during close combat.
    public static class ShipClearance
    {
        public const float FighterRange = 12f;
        public const float FighterHeight = 8f;
        public const float BossRange = 18f;
        public const float BossRamRange = 13f;
        public const float BossHeight = 12f;

        public static bool PushAway(
            Transform self,
            Transform other,
            float minRange,
            float maxHeightDelta)
        {
            // Ignores missing transforms because a destroyed ship can still appear in a frame's lookup list.
            if (self == null || other == null)
            {
                return false;
            }

            Vector3 selfPos = self.position;
            Vector3 otherPos = other.position;
            if (Mathf.Abs(selfPos.y - otherPos.y) > maxHeightDelta)
            {
                // Treats ships far apart vertically as separate, allowing players to fly above one another.
                return false;
            }

            Vector3 delta = selfPos - otherPos;
            delta.y = 0f;

            // Removes height before measuring distance so clearance uses a flat XZ cylinder around each ship.
            float dist = delta.magnitude;
            if (dist >= minRange)
            {
                return false;
            }

            if (dist < 0.01f)
            {
                // Uses a fallback direction when both ships occupy the exact same point and have no direction vector.
                delta = self.forward;
                delta.y = 0f;
                if (delta.sqrMagnitude < 0.01f)
                {
                    delta = Vector3.right;
                }

                dist = 0.01f;
            }

            Vector3 pushed = otherPos + (delta / dist) * minRange;

            // Normalises the separation vector, then places this ship exactly on the minimum allowed radius.
            pushed.y = selfPos.y;
            Game.World.SweptMovement.Move(self, pushed - selfPos, self.GetComponent<Game.Enemies.MotherShipBoss>() != null ? 6f : 2.4f);
            return true;
        }

        public static void PushAway(
            Transform self,
            Transform other,
            float minRange,
            float maxHeightDelta,
            ref Vector3 velocity)
        {
            // Reuses positional separation, then also removes velocity that would push the ship back into the hull.
            if (!PushAway(self, other, minRange, maxHeightDelta))
            {
                return;
            }

            Vector3 away = self.position - other.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
            {
                return;
            }

            away.Normalize();
            float into = Vector3.Dot(velocity, -away);

            // The dot product measures how much velocity points toward the other ship.
            if (into > 0f)
            {
                velocity += away * into;
            }
        }

        public static bool PlanarReach(Vector3 startPosition, Vector3 endPosition, float reach, float maxHeightDelta)
        {
            // Tests vertical difference first so abilities cannot hit targets on unrelated flight levels.
            if (Mathf.Abs(startPosition.y - endPosition.y) > maxHeightDelta)
            {
                return false;
            }

            startPosition.y = 0f;
            endPosition.y = 0f;
            float range = reach;
            return (startPosition - endPosition).sqrMagnitude <= range * range;

            // Compares squared distances to avoid the extra square-root calculation from Vector3.Distance.
        }
    }
}
