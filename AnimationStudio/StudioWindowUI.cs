using FurnitureAnimationsMod;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnimationStudio
{
    public class StudioWindowUI
    {
        private readonly StudioSession _session;
        private readonly BezierCurveDrawer _bezierDrawer = new BezierCurveDrawer();

        private Rect _windowRect;
        private Rect _loadWindowRect;
        private Vector2 _timelineScroll;
        private Vector2 _loadListScroll;

        private bool _showLoadPopup;
        private string _loadSearchFilter = string.Empty;
        private string[] _cachedJsonFiles = Array.Empty<string>();
        private readonly Dictionary<string, Texture2D> _loadedThumbnails = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private GUIStyle _cardLabelStyle;

        public bool IsVisible { get; private set; }

        public bool IsMouseOverUI
        {
            get
            {
                if (!IsVisible) return false;
                Vector2 guiMousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (_windowRect.Contains(guiMousePos)) return true;
                if (_showLoadPopup && _loadWindowRect.Contains(guiMousePos)) return true;
                return false;
            }
        }

        public StudioWindowUI(StudioSession session)
        {
            _session = session;
            float startX = AnimationStudioPlugin.WindowX != null ? AnimationStudioPlugin.WindowX.Value : 580f;
            float startY = AnimationStudioPlugin.WindowY != null ? AnimationStudioPlugin.WindowY.Value : Mathf.Max(20f, Screen.height - 270f);
            _windowRect = new Rect(startX, startY, 920f, 240f);
            ClampMainWindowToScreen();
        }

        public void ToggleVisible()
        {
            SetVisible(!IsVisible);
        }

        public void SetVisible(bool visible)
        {
            if (IsVisible == visible) return;

            if (!visible)
            {
                SaveWindowPositionToConfig();
                CloseLoadGallery();
            }

            IsVisible = visible;

            if (IsVisible)
            {
                ClampMainWindowToScreen();
                _session.RefreshActiveCharacter();
            }
        }

        public void SaveWindowPositionToConfig()
        {
            if (AnimationStudioPlugin.WindowX != null && AnimationStudioPlugin.WindowY != null)
            {
                AnimationStudioPlugin.WindowX.Value = _windowRect.x;
                AnimationStudioPlugin.WindowY.Value = _windowRect.y;
            }
        }

        private void ClampMainWindowToScreen()
        {
            _windowRect.x = Mathf.Clamp(_windowRect.x, 10f, Mathf.Max(10f, Screen.width - _windowRect.width - 10f));
            _windowRect.y = Mathf.Clamp(_windowRect.y, 10f, Mathf.Max(10f, Screen.height - _windowRect.height - 10f));
        }

        public void Draw()
        {
            string hotkeyName = AnimationStudioPlugin.ToggleWindowKey != null
                ? AnimationStudioPlugin.ToggleWindowKey.Value.ToString()
                : "F7";

            _windowRect = GUI.Window(94821, _windowRect, DrawWindowContents, $"Animation Studio ({hotkeyName})");
            ClampMainWindowToScreen();

            if (_showLoadPopup)
            {
                float galleryWidth = _windowRect.width;
                float galleryHeight = 460f;
                float galleryY = _windowRect.y - galleryHeight - 6f;
                if (galleryY < 10f)
                {
                    galleryY = Mathf.Min(Screen.height - galleryHeight - 10f, _windowRect.y + _windowRect.height + 6f);
                }

                _loadWindowRect = new Rect(_windowRect.x, galleryY, galleryWidth, galleryHeight);
                _loadWindowRect = GUI.Window(94822, _loadWindowRect, DrawLoadGalleryContents, "Load Animation (JSON + Preview)");
            }

            if (IsMouseOverUI &&
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

            Color oldBg = GUI.backgroundColor;
            if (_showLoadPopup) GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button("Load JSON", GUILayout.Width(85)))
            {
                if (_showLoadPopup)
                    CloseLoadGallery();
                else
                    OpenLoadGallery();
            }
            GUI.backgroundColor = oldBg;

            GUILayout.EndHorizontal();
        }

        private void OpenLoadGallery()
        {
            ClearThumbnails();
            _cachedJsonFiles = _session.GetAvailableJsonFiles();
            _loadListScroll = Vector2.zero;
            LoadThumbnailsForFiles(_cachedJsonFiles);
            _showLoadPopup = true;
        }

        private void CloseLoadGallery()
        {
            _showLoadPopup = false;
            ClearThumbnails();
        }

        private void ClearThumbnails()
        {
            foreach (var kv in _loadedThumbnails)
            {
                if (kv.Value != null)
                {
                    UnityEngine.Object.Destroy(kv.Value);
                }
            }
            _loadedThumbnails.Clear();
        }

        private void LoadThumbnailsForFiles(string[] animNames)
        {
            if (animNames == null) return;

            string poseAnimDir = Path.Combine(BepInEx.Paths.PluginPath, "PoseAnimations");
            string customAnimDir = ConfigManager.CustomAnimsPath;

            string[] searchDirs = new[]
            {
                Path.Combine(poseAnimDir, "icons"),
                poseAnimDir,
                Path.Combine(customAnimDir, "icons"),
                customAnimDir
            };

            foreach (string animName in animNames)
            {
                if (string.IsNullOrEmpty(animName) || _loadedThumbnails.ContainsKey(animName))
                    continue;

                foreach (string dir in searchDirs)
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    string pngPath = Path.Combine(dir, $"{animName}.png");
                    if (File.Exists(pngPath))
                    {
                        try
                        {
                            byte[] bytes = File.ReadAllBytes(pngPath);
                            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                            if (tex.LoadImage(bytes))
                            {
                                tex.name = animName;
                                _loadedThumbnails[animName] = tex;
                                break;
                            }
                            UnityEngine.Object.Destroy(tex);
                        }
                        catch (Exception ex)
                        {
                            AnimationStudioPlugin.Log?.LogWarning($"[AnimationStudio] Failed to load icon '{pngPath}': {ex.Message}");
                        }
                    }
                }
            }
        }

        private void DrawLoadGalleryContents(int id)
        {
            if (_cardLabelStyle == null)
            {
                _cardLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                    fontSize = 11
                };
            }

            // Верхняя панель поиска и закрытия галереи
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("Поиск:", GUILayout.Width(48));
            _loadSearchFilter = GUILayout.TextField(_loadSearchFilter, GUILayout.Width(240));
            if (GUILayout.Button("✕", GUILayout.Width(24)))
            {
                _loadSearchFilter = string.Empty;
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label($"Всего: {_cachedJsonFiles.Length}", GUILayout.Width(80));
            if (GUILayout.Button("Закрыть", GUILayout.Width(80)))
            {
                CloseLoadGallery();
                GUILayout.EndHorizontal();
                return;
            }
            GUILayout.EndHorizontal();

            // Фильтрация списка
            var visibleFiles = new List<string>(_cachedJsonFiles.Length);
            bool hasFilter = !string.IsNullOrWhiteSpace(_loadSearchFilter);
            foreach (string file in _cachedJsonFiles)
            {
                if (!hasFilter || file.IndexOf(_loadSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    visibleFiles.Add(file);
                }
            }

            // Сетка карточек (6 колонок)
            const int columns = 6;
            const float cardWidth = 140f;
            const float cardHeight = 148f;
            const float iconSize = 104f;

            _loadListScroll = GUILayout.BeginScrollView(_loadListScroll, GUILayout.ExpandHeight(true));

            int count = visibleFiles.Count;
            for (int i = 0; i < count; i += columns)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < columns; col++)
                {
                    int idx = i + col;
                    if (idx >= count)
                    {
                        GUILayout.Space(cardWidth + 4f);
                        continue;
                    }

                    string animName = visibleFiles[idx];
                    GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(cardWidth), GUILayout.Height(cardHeight));

                    _loadedThumbnails.TryGetValue(animName, out Texture2D iconTex);

                    bool clicked = iconTex != null
                        ? GUILayout.Button(iconTex, GUILayout.Width(iconSize), GUILayout.Height(iconSize))
                        : GUILayout.Button("No\nPreview", GUILayout.Width(iconSize), GUILayout.Height(iconSize));

                    GUILayout.Label(animName, _cardLabelStyle, GUILayout.Width(cardWidth - 8f), GUILayout.Height(32f));

                    if (clicked)
                    {
                        _session.LoadFromJson(animName);
                        CloseLoadGallery();
                        GUILayout.EndVertical();
                        GUILayout.EndHorizontal();
                        GUILayout.EndScrollView();
                        return;
                    }

                    GUILayout.EndVertical();
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
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

            if (delta.bezier != null && delta.bezier.Length >= 4)
            {
                float p1x = delta.bezier[0];
                float p1y = delta.bezier[1];
                float p2x = delta.bezier[2];
                float p2y = delta.bezier[3];
                GUILayout.Label($"P1({p1x:0.00}, {p1y:0.00}) P2({p2x:0.00}, {p2y:0.00})");
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
