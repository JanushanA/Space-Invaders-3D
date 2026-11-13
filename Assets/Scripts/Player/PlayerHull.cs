using UnityEngine;

// Swaps the player hull visual while retaining the existing gameplay object and its components.
namespace Game.Player
{
    // Replaces only local player visual children, preserving the enemy prefab and gameplay root.
    [DefaultExecutionOrder(-150)]
    public sealed class PlayerHull : MonoBehaviour
    {
        int current = -1;

        void Awake()
        {
            Apply();
            Game.Progression.PilotInventory.Changed += Apply;
        }

        void OnDestroy()
        {
            Game.Progression.PilotInventory.Changed -= Apply;
        }

        void Apply()
        {
            int index = Game.Progression.PilotInventory.State.ship;
            if (index == current)
            {
                return;
            }

            var prefab = Resources.Load<GameObject>("Ships/Player" + index);
            if (prefab == null)
            {
                return;
            }

            var childTransform = transform.Find("Hull");
            if (childTransform != null)
            {
                childTransform.name = "Replaced hull";
                childTransform.gameObject.SetActive(false);
                Destroy(childTransform.gameObject);
            }

            var hull = Instantiate(prefab, transform);
            hull.name = "Hull";
            current = index;
            var collider = GetComponent<BoxCollider>();
            if (collider != null)
            {
                collider.center = Vector3.zero;
                collider.size = new Vector3(3.6f, 1.3f, 4.4f);
            }

            var view = GetComponent<FlightView>();
            if (view != null)
            {
                view.enabled = false;
                view.enabled = true;
            }
        }
    }
}
