using System;
using Game.Core;
using Game.Player;
using UnityEngine;
using UnityEngine.UI;
namespace Game.Campaign
{
    // Presentation only: reads the mission's real target without changing mission state or authority.
    public sealed class CampaignObjectiveGraphic : MaskableGraphic
    {
        public struct Cue
        {
            public bool Valid;
            public Vector3 Position;
            public Vector3 Origin;
            public string Name;
            public string Action;
            public Color Color;
            public float InteractionRange;
        }
        private Func<Cue> source;
        private GameObject objectivePanel;
        private Text label;
        private bool visible;
        private bool edge;
        private Vector2 direction;
        private float nextText;
        private string previousTarget;
        private static readonly Vector2[] Corners = { Vector2.up * 19, Vector2.right * 19, Vector2.down * 19, Vector2.left * 19 };

        public void Bind(Func<Cue> read, GameObject panel)
        {
            source = read;
            objectivePanel = panel;
            raycastTarget = false;
            rectTransform.sizeDelta = new Vector2(48, 48);
            label = UiChrome.MakeText(transform, "Navigation target", "", 17, TextAnchor.UpperCenter, UiChrome.NeonBlue);
            UiChrome.Place(label.rectTransform, Vector2.one * 0.5f, new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(310, 68));
            label.horizontalOverflow = HorizontalWrapMode.Wrap;

            // A subtle dark shadow keeps navigation legible against bright planets and weapon effects.
            var shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = UiChrome.Ink;
            shadow.effectDistance = new Vector2(1, -1);
        }

        private void LateUpdate()
        {
            if (source == null || label == null)
            {
                return;
            }

            bool show = !PlayerSkills.MenuOpen;
            if (objectivePanel != null)
            {
                objectivePanel.SetActive(show);
            }

            Cue objectiveCue = source();
            Camera view = FlightView.Instance != null ? FlightView.Instance.ViewCamera : Camera.main;
            visible = show && objectiveCue.Valid && view != null;
            label.enabled = visible;
            if (!visible)
            {
                SetVerticesDirty();
                return;
            }

            Vector3 viewportPoint = view.WorldToViewportPoint(objectiveCue.Position);
            Vector3 cameraLocalPoint = view.transform.InverseTransformPoint(objectiveCue.Position);

            // Flip targets behind the camera so the edge arrow points toward the turn needed.
            direction = new Vector2(viewportPoint.x - 0.5f, viewportPoint.y - 0.5f) * (viewportPoint.z < 0 ? -1 : 1);
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = Vector2.down;
            }

            direction.Normalize();
            edge = viewportPoint.z <= 0 || viewportPoint.x < 0.08f || viewportPoint.x > 0.92f || viewportPoint.y < 0.32f || viewportPoint.y > 0.8f;
            Vector2 anchor = edge ? EdgePoint(direction) : new Vector2(viewportPoint.x, viewportPoint.y);
            rectTransform.anchorMin = rectTransform.anchorMax = anchor;
            rectTransform.anchoredPosition = Vector2.zero;
            color = objectiveCue.Color;
            label.color = objectiveCue.Color;
            if (Time.unscaledTime >= nextText || previousTarget != objectiveCue.Name)
            {
                nextText = Time.unscaledTime + 0.1f;
                previousTarget = objectiveCue.Name;
                float distance = Vector3.Distance(objectiveCue.Origin, objectiveCue.Position);
                float bearing = Mathf.Atan2(cameraLocalPoint.x, cameraLocalPoint.z) * Mathf.Rad2Deg;
                float elevation = Mathf.Atan2(cameraLocalPoint.y, new Vector2(cameraLocalPoint.x, cameraLocalPoint.z).magnitude) * Mathf.Rad2Deg;
                string guidance = distance <= objectiveCue.InteractionRange ? objectiveCue.Action : viewportPoint.z <= 0 ? "TURN AROUND" : Mathf.Abs(bearing) > 8 ? "TURN " + (bearing < 0 ? "LEFT " : "RIGHT ") + Mathf.Abs(Mathf.RoundToInt(bearing)) + "°" : "AHEAD";
                if (distance > objectiveCue.InteractionRange && Mathf.Abs(elevation) > 12)
                {
                    guidance += elevation > 0 ? "  /  CLIMB" : "  /  DESCEND";
                }

                label.text = objectiveCue.Name + "  /  " + Mathf.CeilToInt(distance) + " m\n" + guidance;
            }

            SetVerticesDirty();
        }

        private static Vector2 EdgePoint(Vector2 direction)
        {
            // Edge beacons stay above the bottom instrument panels and below the top objective/boss cards.
            Vector2 centre = new Vector2(0.5f, 0.56f);
            float horizontalScale = Mathf.Abs(direction.x) > 0.001f ? 0.42f / Mathf.Abs(direction.x) : float.MaxValue;
            float verticalScale = Mathf.Abs(direction.y) > 0.001f ? 0.24f / Mathf.Abs(direction.y) : float.MaxValue;
            return centre + direction * Mathf.Min(horizontalScale, verticalScale);
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (!visible)
            {
                return;
            }

            if (edge)
            {
                Vector2 normal = new Vector2(-direction.y, direction.x);
                Vector2 arrowTip = direction * 20;
                Line(vertexHelper, arrowTip, -direction * 10 + normal * 12, color);
                Line(vertexHelper, arrowTip, -direction * 10 - normal * 12, color);
                Line(vertexHelper, -direction * 3, -direction * 20, color);
            }
            else
            {
                for (int index = 0; index < 4; index++)
                {
                    Line(vertexHelper, Corners[index], Corners[(index + 1) % 4], color);
                }

                Line(vertexHelper, Vector2.left * 4, Vector2.right * 4, color);
                Line(vertexHelper, Vector2.up * 4, Vector2.down * 4, color);
            }
        }

        private static void Line(VertexHelper vertexHelper, Vector2 startPoint, Vector2 endPoint, Color tint)
        {
            Vector2 lineDirection = (endPoint - startPoint).normalized;
            Vector2 halfWidthOffset = new Vector2(-lineDirection.y, lineDirection.x) * 1.5f;
            int firstVertex = vertexHelper.currentVertCount;
            vertexHelper.AddVert(startPoint - halfWidthOffset, tint, Vector2.zero);
            vertexHelper.AddVert(startPoint + halfWidthOffset, tint, Vector2.zero);
            vertexHelper.AddVert(endPoint + halfWidthOffset, tint, Vector2.zero);
            vertexHelper.AddVert(endPoint - halfWidthOffset, tint, Vector2.zero);
            vertexHelper.AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
            vertexHelper.AddTriangle(firstVertex, firstVertex + 2, firstVertex + 3);
        }
    }
}
