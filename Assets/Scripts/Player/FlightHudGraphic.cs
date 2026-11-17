using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Draws the radar rings, contact marks and damage edges directly from current flight data.
namespace Game.Player
{
    // A single canvas mesh for line instruments, avoiding one GameObject/draw call per tick.
    public sealed class FlightHudGraphic : MaskableGraphic
    {
        public bool BracketOnly;
        public float Damage;
        public List<Vector2> Contacts;
        public List<Color> ContactColors;

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Color amber = BracketOnly ? Game.Core.UiChrome.NeonRed : GameHud.Amber;
            if (BracketOnly)
            {
                for (int horizontalSide = -1; horizontalSide <= 1; horizontalSide += 2)
                {
                    for (int verticalSide = -1; verticalSide <= 1; verticalSide += 2)
                    {
                        Vector2 screenPosition = new Vector2(horizontalSide * rectTransform.rect.width * 0.5f, verticalSide * rectTransform.rect.height * 0.5f);
                        Line(vertexHelper, screenPosition, screenPosition - new Vector2(horizontalSide * 18, 0), 2f, amber);
                        Line(vertexHelper, screenPosition, screenPosition - new Vector2(0, verticalSide * 16), 2f, amber);
                    }
                }

                return;
            }

            Rect panelBounds = rectTransform.rect;
            Vector2 centre = panelBounds.center;
            Vector2 radar = new Vector2(centre.x, panelBounds.yMin + 140);
            Color faint = new Color(amber.r, amber.g, amber.b, 0.32f);

            for (int rangeRingIndex = 1; rangeRingIndex <= 4; rangeRingIndex++)
            {
                Arc(vertexHelper, radar, new Vector2(rangeRingIndex * 34, rangeRingIndex * 15), 0, 360, 0.8f, faint);
            }

            Arc(vertexHelper, radar, new Vector2(142, 64), 4, 176, 1.8f, amber);
            Arc(vertexHelper, radar, new Vector2(142, 64), 184, 356, 1.8f, amber);
            Line(vertexHelper, radar - new Vector2(136, 0), radar + new Vector2(136, 0), 0.8f, faint);
            Line(vertexHelper, radar - new Vector2(0, 60), radar + new Vector2(0, 60), 0.8f, faint);

            for (int bearingTickIndex = 0; bearingTickIndex < 24; bearingTickIndex++)
            {
                float angleRadians = bearingTickIndex * Mathf.PI / 12;
                Vector2 screenPosition2 = new Vector2(Mathf.Cos(angleRadians) * 145, Mathf.Sin(angleRadians) * 66);
                Line(vertexHelper, radar + screenPosition2, radar + screenPosition2 * 1.035f, 1f, amber);
            }

            Line(vertexHelper, radar + new Vector2(-5, -4), radar + new Vector2(0, 5), 1.5f, GameHud.Cyan);
            Line(vertexHelper, radar + new Vector2(0, 5), radar + new Vector2(5, -4), 1.5f, GameHud.Cyan);
            if (Contacts != null)
            {
                for (int contactIndex = 0; contactIndex < Contacts.Count; contactIndex++)
                {
                    Vector2 screenPosition3 = radar + Vector2.Scale(Contacts[contactIndex], new Vector2(132, 57));
                    Color tone = ContactColors != null && contactIndex < ContactColors.Count ? ContactColors[contactIndex] : amber;
                    Line(vertexHelper, screenPosition3, screenPosition3 + Vector2.up * 7, 1f, tone);
                    Line(vertexHelper, screenPosition3 + new Vector2(-2, 7), screenPosition3 + new Vector2(2, 7), 3f, tone);
                }
            }

            // Peripheral damage cue does not cover the scene with an opaque flash.
            if (Damage > 0.001f)
            {
                Color damage = new Color(1, 0.2f, 0.08f, Damage);
                Line(vertexHelper, new Vector2(panelBounds.xMin + 18, panelBounds.yMin + 180), new Vector2(panelBounds.xMin + 18, panelBounds.yMax - 180), 3, damage);
                Line(vertexHelper, new Vector2(panelBounds.xMax - 18, panelBounds.yMin + 180), new Vector2(panelBounds.xMax - 18, panelBounds.yMax - 180), 3, damage);
            }
        }

        private static void Arc(VertexHelper vertexHelper, Vector2 centre, Vector2 radius, float from, float endAngle, float width, Color color)
        {
            int count = Mathf.CeilToInt((endAngle - from) / 6f);
            Vector2 previous = centre + Vector2.Scale(new Vector2(Mathf.Cos(from * Mathf.Deg2Rad), Mathf.Sin(from * Mathf.Deg2Rad)), radius);

            for (int segmentIndex = 1; segmentIndex <= count; segmentIndex++)
            {
                float angle = Mathf.Lerp(from, endAngle, (float)segmentIndex / count) * Mathf.Deg2Rad;
                Vector2 next = centre + Vector2.Scale(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), radius);
                Line(vertexHelper, previous, next, width, color);
                previous = next;
            }
        }

        private static void Line(VertexHelper vertexHelper, Vector2 lineStart, Vector2 lineEnd, float width, Color color)
        {
            Vector2 direction = (lineEnd - lineStart).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x) * (width * 0.5f);
            int start = vertexHelper.currentVertCount;
            vertexHelper.AddVert(lineStart - normal, color, Vector2.zero);
            vertexHelper.AddVert(lineStart + normal, color, Vector2.zero);
            vertexHelper.AddVert(lineEnd + normal, color, Vector2.zero);
            vertexHelper.AddVert(lineEnd - normal, color, Vector2.zero);
            vertexHelper.AddTriangle(start, start + 1, start + 2);
            vertexHelper.AddTriangle(start, start + 2, start + 3);
        }
    }
}
