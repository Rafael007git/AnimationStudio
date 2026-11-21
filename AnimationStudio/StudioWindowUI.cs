using FurnitureAnimationsMod;
using System;
using UnityEngine;

namespace AnimationStudio
{
    public class StudioWindowUI
    {
        private readonly StudioSession _session;
        private readonly BezierCurveDrawer _bezierDrawer = new BezierCurveDrawer();

        private Rect _windowRect = new Rect(40f, Screen.height - 270f, 920f, 240f);
        private Vector2 _timelineScroll;
        private Vector2 _loadListScroll;
        private bool _showLoadPopup;
        private string[] _cachedJsonFiles = new string[0];

        public bool IsVisible { get; private set; }
        public bool IsMouseOverUI
        {
            get
            {
                if (!IsVisible) return false;
                Vector2 guiMousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                return _windowRect.Contains(guiMousePos);
            }
        }

        public StudioWindowUI(StudioSession session)
        {
            _session = session;
        }

        public void ToggleVisible()
        {
            IsVisible = !IsVisible;
            if (IsVisible)
            {
                _windowRect.y = Mathf.Clamp(_windowRect.y, 10f, Screen.height - _windowRect.height - 10f);
                _session.RefreshActiveCharacter();
            }
        }

        public void Draw()
        {
            _windowRect = GUI.Window(94821, _windowRect, DrawWindowContents, "Animation Studio (F8)");

            // Блокируем прокликивание сцены и вращение камеры под окном редактора
            if (_windowRect.Contains(Event.current.mousePosition) &&
                (Event.current.type == EventType.MouseDown || Event.current.type == EventType.ScrollWheel))
            {
                Input.ResetInputAxes();
            }
        }

        private void DrawWindowContents(int id)
        {
            DrawTopBar();
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            DrawTimelinePanel();
            GUILayout.Space(8);
            DrawInspectorPanel();
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 22f));
        }

        private void DrawTopBar()
        {
            GUILayout.BeginHorizontal(GUI.skin.box);

            GUILayout.Label("Имя:", GUILayout.Width(35));
            _session.AnimationName = GUILayout.TextField(_session.AnimationName, GUILayout.Width(160));
            string targetName = _session.ActiveCharacter != null ? _session.ActiveCharacter.name : "None";
            GUILayout.Label($"Target: {targetName}", GUILayout.Width(160));


            _session.Loop = GUILayout.Toggle(_session.Loop, "Loop", GUILayout.Width(55));
            _session.Reverse = GUILayout.Toggle(_session.Reverse, "Reverse", GUILayout.Width(68));

            GUILayout.Label("Rate:", GUILayout.Width(35));
            if (float.TryParse(GUILayout.TextField(_session.Rate.ToString("0.0000"), GUILayout.Width(55)), out float newRate))
                _session.Rate = Mathf.Max(0.001f, newRate);

            GUILayout.FlexibleSpace();

            if (!_session.IsPlaying)
            {
                if (GUILayout.Button("▶ Play", GUILayout.Width(70))) _session.PlayPreview();
            }
            else
            {
                if (GUILayout.Button("■ Stop", GUILayout.Width(70))) _session.StopPreview();
            }

            if (GUILayout.Button("Save JSON", GUILayout.Width(85)))
            {
                _session.SaveToJson();
            }

            if (GUILayout.Button("Load JSON", GUILayout.Width(85)))
            {
                _cachedJsonFiles = _session.GetAvailableJsonFiles();
                _showLoadPopup = !_showLoadPopup;
            }

            GUILayout.EndHorizontal();

            if (_showLoadPopup)
            {
                GUILayout.BeginVertical(GUI.skin.box, GUILayout.Height(90));
                _loadListScroll = GUILayout.BeginScrollView(_loadListScroll);
                foreach (var file in _cachedJsonFiles)
                {
                    if (GUILayout.Button(file))
                    {
                        _session.LoadFromJson(file);
                        _showLoadPopup = false;
                    }
                }
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
            }
        }

        private void DrawTimelinePanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(540), GUILayout.ExpandHeight(true));

            // Кнопки управления кадрами
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Capture Frame", GUILayout.Width(120)))
                _session.CaptureNewFrame();

            GUI.enabled = _session.SelectedIndex >= 0;
            if (GUILayout.Button("Insert After", GUILayout.Width(90)))
                _session.CaptureNewFrame(_session.SelectedIndex);
            if (GUILayout.Button("Update Pose", GUILayout.Width(90)))
                _session.UpdateSelectedFrame();
            if (GUILayout.Button("Delete", GUILayout.Width(65)))
                _session.DeleteSelectedFrame();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Горизонтальная лента кадров
            _timelineScroll = GUILayout.BeginScrollView(_timelineScroll, GUILayout.Height(125));
            GUILayout.BeginHorizontal();

            for (int i = 0; i < _session.Keyframes.Count; i++)
            {
                var kf = _session.Keyframes[i];
                bool isSelected = i == _session.SelectedIndex;
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = isSelected ? Color.cyan : Color.white;

                string title = i == 0
                    ? "Frame 0\n(Start)"
                    : $"Delta {i}\n({kf.frames}f)\n{(kf.bezier != null ? "Bezier" : (kf.easeMode?.ToString() ?? "Inherit"))}";

                if (GUILayout.Button(title, GUILayout.Width(82), GUILayout.Height(95)))
                {
                    _session.SelectAndSnap(i);
                }

                GUI.backgroundColor = oldBg;
            }

            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawInspectorPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            int idx = _session.SelectedIndex;
            if (idx <= 0 || idx >= _session.Keyframes.Count)
            {
                GUILayout.Label(idx == 0
                    ? "Frame 0 — стартовая поза.\nВыберите Delta 1..N для настройки перехода."
                    : "Нажмите '+ Capture Frame', чтобы записать стартовую позу.");
                GUILayout.EndVertical();
                return;
            }

            var delta = _session.Keyframes[idx];
            GUILayout.BeginHorizontal();

            // Левая колонка инспектора: длительность и режим сглаживания
            GUILayout.BeginVertical(GUILayout.Width(190));
            GUILayout.Label($"Переход: Delta {idx}");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Frames:", GUILayout.Width(55));
            if (int.TryParse(GUILayout.TextField(delta.frames.ToString(), GUILayout.Width(50)), out int f))
                delta.frames = Mathf.Clamp(f, 1, 600);
            GUILayout.EndHorizontal();

            bool useBezier = delta.bezier != null;
            bool newUseBezier = GUILayout.Toggle(useBezier, "Custom Bezier");
            if (newUseBezier != useBezier)
            {
                delta.bezier = newUseBezier ? new[] { 0.42f, 0.0f, 0.58f, 1.0f } : null;
            }

            if (delta.bezier != null)
            {
                GUILayout.Label($"P1({delta.bezier[0]:0.00}, {delta.bezier[1]:0.00}) P2({delta.bezier[2]:0.00}, {delta.bezier[3]:0.00})");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Lin")) delta.bezier = new[] { 0f, 0f, 1f, 1f };
                if (GUILayout.Button("Smooth")) delta.bezier = new[] { 0.42f, 0f, 0.58f, 1f };
                if (GUILayout.Button("Out")) delta.bezier = new[] { 0f, 0f, 0.2f, 1f };
                if (GUILayout.Button("Bounce")) delta.bezier = new[] { 0.34f, 1.35f, 0.64f, 1f };
                GUILayout.EndHorizontal();
            }
            else
            {
                if (GUILayout.Button($"Ease: {delta.easeMode?.ToString() ?? "Inherit"}"))
                {
                    if (delta.easeMode == null) delta.easeMode = EaseMode.Linear;
                    else if (delta.easeMode == EaseMode.Linear) delta.easeMode = EaseMode.Global;
                    else if (delta.easeMode == EaseMode.Global) delta.easeMode = EaseMode.PerFrame;
                    else delta.easeMode = null;
                }
            }
            GUILayout.EndVertical();

            // Правая колонка инспектора: квадрат кривой Безье 140x140
            if (delta.bezier != null)
            {
                Rect bezierRect = GUILayoutUtility.GetRect(140f, 140f, GUILayout.Width(140f), GUILayout.Height(140f));
                _bezierDrawer.Draw(bezierRect, delta.bezier);
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

    }
}
