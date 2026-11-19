using System;
using UnityEngine;

namespace AnimationStudio
{
    public class BezierCurveDrawer
    {
        private static Texture2D _whiteTex;
        private int _draggingPoint = 0; // 0 = нет, 1 = P1, 2 = P2

        private static Texture2D WhiteTex
        {
            get
            {
                if (_whiteTex == null)
                {
                    _whiteTex = new Texture2D(1, 1);
                    _whiteTex.SetPixel(0, 0, Color.white);
                    _whiteTex.Apply();
                }
                return _whiteTex;
            }
        }

        public void Draw(Rect boxRect, float[] bezier)
        {
            GUI.Box(boxRect, GUIContent.none);

            // Рабочая область с отступом сверху/снизу для Overshoot (y < 0 или y > 1)
            float padX = 12f;
            float padY = 22f;
            Rect graph = new Rect(boxRect.x + padX, boxRect.y + padY, boxRect.width - padX * 2f, boxRect.height - padY * 2f);

            // Сетка 0..1
            DrawLine(new Vector2(graph.xMin, graph.yMax), new Vector2(graph.xMax, graph.yMax), new Color(1, 1, 1, 0.25f), 1f);
            DrawLine(new Vector2(graph.xMin, graph.yMin), new Vector2(graph.xMax, graph.yMin), new Color(1, 1, 1, 0.25f), 1f);
            DrawLine(new Vector2(graph.xMin, graph.yMin), new Vector2(graph.xMin, graph.yMax), new Color(1, 1, 1, 0.25f), 1f);
            DrawLine(new Vector2(graph.xMax, graph.yMin), new Vector2(graph.xMax, graph.yMax), new Color(1, 1, 1, 0.25f), 1f);

            Vector2 p0 = ToScreen(graph, 0f, 0f);
            Vector2 p3 = ToScreen(graph, 1f, 1f);
            Vector2 p1 = ToScreen(graph, bezier[0], bezier[1]);
            Vector2 p2 = ToScreen(graph, bezier[2], bezier[3]);

            // Касательные
            DrawLine(p0, p1, new Color(0.4f, 0.8f, 1f, 0.6f), 1.5f);
            DrawLine(p3, p2, new Color(1f, 0.6f, 0.4f, 0.6f), 1.5f);

            // Кривая Безье (24 сегмента)
            Vector2 prev = p0;
            for (int i = 1; i <= 24; i++)
            {
                float t = i / 24f;
                float u = 1f - t;
                float bx = 3f * u * u * t * bezier[0] + 3f * u * t * t * bezier[2] + t * t * t;
                float by = 3f * u * u * t * bezier[1] + 3f * u * t * t * bezier[3] + t * t * t;
                Vector2 curr = ToScreen(graph, bx, by);
                DrawLine(prev, curr, Color.green, 2f);
                prev = curr;
            }

            // Контрольные точки и обработка мыши
            HandleMouse(boxRect, graph, bezier, p1, p2);
            DrawHandle(p1, new Color(0.2f, 0.8f, 1f));
            DrawHandle(p2, new Color(1f, 0.5f, 0.2f));
        }

        private void HandleMouse(Rect boxRect, Rect graph, float[] bezier, Vector2 p1, Vector2 p2)
        {
            Event e = Event.current;
            Vector2 m = e.mousePosition;

            if (e.type == EventType.MouseDown && e.button == 0 && boxRect.Contains(m))
            {
                if (Vector2.Distance(m, p1) <= 10f) { _draggingPoint = 1; e.Use(); }
                else if (Vector2.Distance(m, p2) <= 10f) { _draggingPoint = 2; e.Use(); }
            }
            else if (e.type == EventType.MouseDrag && _draggingPoint != 0)
            {
                float nx = Mathf.Clamp01((m.x - graph.xMin) / graph.width);
                float ny = Mathf.Clamp((graph.yMax - m.y) / graph.height, -0.5f, 1.5f);

                if (_draggingPoint == 1) { bezier[0] = nx; bezier[1] = ny; }
                else { bezier[2] = nx; bezier[3] = ny; }
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _draggingPoint != 0)
            {
                _draggingPoint = 0;
                e.Use();
            }
        }

        private static Vector2 ToScreen(Rect g, float x, float y) =>
            new Vector2(g.xMin + x * g.width, g.yMax - y * g.height);

        private static void DrawHandle(Vector2 center, Color col)
        {
            Color old = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(new Rect(center.x - 5f, center.y - 5f, 10f, 10f), WhiteTex);
            GUI.color = old;
        }

        private static void DrawLine(Vector2 a, Vector2 b, Color col, float width)
        {
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            float len = Vector2.Distance(a, b);
            GUI.color = col;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), WhiteTex);
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }
    }
}