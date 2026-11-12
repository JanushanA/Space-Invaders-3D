using System.Collections.Generic;
using Game.Core;
using UnityEngine;

// Registers visible ships, missiles and salvage in one shared spatial index used by radar and weapons.
namespace Game.Combat
{
    [System.Flags]
    public enum RadarKind
    {
        Hostile = 1, Missile = 2, Salvage = 4, Friendly = 8, All = 15
    }

    public sealed class RadarContact : MonoBehaviour
    {
        RadarKind kind;

        public Health Health
        {
            get; private set;
        }

        public Projectile Projectile
        {
            get; private set;
        }

        public RadarKind Kind => Health != null ? (gameObject.layer == LayerMask.NameToLayer(GameLayers.Enemy) ? RadarKind.Hostile : gameObject.layer == LayerMask.NameToLayer(GameLayers.Player) ? RadarKind.Friendly : 0) : kind;

        public bool Live => isActiveAndEnabled && (Health == null || Health.IsAlive) && (Projectile == null || Projectile.InFlight && Projectile.Source == DamageSource.EnemyProjectile);

        public static RadarContact Track(GameObject host, RadarKind kind)
        {
            var contact = host.GetComponent<RadarContact>();
            if (contact == null)
            {
                contact = host.AddComponent<RadarContact>();
            }

            contact.kind = kind;
            contact.Health = host.GetComponent<Health>();
            contact.Projectile = host.GetComponent<Projectile>();
            if (host.activeInHierarchy)
            {
                RadarIndex.Register(contact);
            }

            return contact;
        }

        void OnEnable()
        {
            RadarIndex.Register(this);
        }

        void OnDisable()
        {
            RadarIndex.Remove(this);
        }

        void OnDestroy()
        {
            RadarIndex.Remove(this);
        }
    }

    public static class RadarIndex
    {
        static SpatialOctree<RadarContact> index = new SpatialOctree<RadarContact>(Vector3.zero);
        static readonly HashSet<RadarContact> contacts = new HashSet<RadarContact>();
        static readonly List<RadarContact> stale = new List<RadarContact>();
        static readonly List<RadarContact> queried = new List<RadarContact>();
        static int updated = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            contacts.Clear();
            index = new SpatialOctree<RadarContact>(Vector3.zero);
            updated = -1;
        }

        public static void Register(RadarContact contact)
        {
            contacts.Add(contact);
            index.Upsert(contact, contact.transform.position);
        }

        public static void Remove(RadarContact contact)
        {
            contacts.Remove(contact);
            index.Remove(contact);
        }

        // One transform update per frame shared by all sensors and firing weapons, not one scene scan per shot.
        public static void Synchronize(bool force = false)
        {
            if (!force && updated == Time.frameCount)
            {
                return;
            }

            updated = Time.frameCount;
            stale.Clear();

            foreach (var contact in contacts)
            {
                if (contact == null)
                {
                    stale.Add(contact);
                    continue;
                }

                if (contact.Live)
                {
                    index.Upsert(contact, contact.transform.position);
                }
                else
                {
                    index.Remove(contact);
                }
            }

            foreach (var contact in stale)
            {
                contacts.Remove(contact);
                index.Remove(contact);
            }
        }

        public static void Query(Vector3 origin, float range, RadarKind kinds, List<RadarContact> result)
        {
            Synchronize();
            queried.Clear();
            index.Query(origin, range, queried);

            foreach (var contact in queried)
            {
                if (contact != null && contact.Live && (contact.Kind & kinds) != 0 && (contact.transform.position - origin).sqrMagnitude <= range * range)
                {
                    result.Add(contact);
                }
            }
        }

        public static bool LineOfSight(Vector3 origin, RadarContact target)
        {
            Vector3 delta = target.transform.position - origin;
            return !Physics.Raycast(origin, delta.normalized, delta.magnitude, LayerMask.GetMask(GameLayers.LevelGeometry), QueryTriggerInteraction.Ignore);
        }

        // Floating origin runs after movement; invalidate any earlier queries in this frame.
        public static void OriginShifted(Vector3 delta)
        {
            index.Shift(delta);
            updated = -1;
        }
    }
}
