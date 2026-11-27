using FurnitureAnimationsMod;
using System;
using System.Collections;
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
        private GUIStyle _cardButtonStyle;
        private Rect _boneListsWindowRect;
        private Vector2 _boneListsScroll;
        private bool _showBoneListsPopup;
        private GUIStyle _boneListTextStyle;

        public bool IsVisible { get; private set; }

        public bool IsMouseOverUI
        {
            get
            {
                if (!IsVisible) return false;
                Vector2 guiMousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (_windowRect.Contains(guiMousePos)) return true;
                if (_showLoadPopup && _loadWindowRect.Contains(guiMousePos)) return true;
                if (_showBoneListsPopup && _boneListsWindowRect.Contains(guiMousePos)) return true;
                return false;
            }
        }

        public StudioWindowUI(StudioSession session)
        {
            _session = session;
            float startX = AnimationStudioPlugin.WindowX != null ? AnimationStudioPlugin.WindowX.Value : 580f;
            float startY = AnimationStudioPlugin.WindowY != null ? AnimationStudioPlugin.WindowY.Value : Mathf.Max(20f, Screen.height - 270f);
            _windowRect = new Rect(startX, startY, 1098f, 240f);
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
                _showBoneListsPopup = false;
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
                float galleryHeight = Mathf.Min(580f, Screen.height - 60f);
                float galleryY = _windowRect.y - galleryHeight - 6f;
                if (galleryY < 10f)
                {
                    galleryY = Mathf.Min(Screen.height - galleryHeight - 10f, _windowRect.y + _windowRect.height + 6f);
                }

                _loadWindowRect = new Rect(_windowRect.x, galleryY, galleryWidth, galleryHeight);
                _loadWindowRect = GUI.Window(94822, _loadWindowRect, DrawLoadGalleryContents, "Load Animation (JSON + Preview)");
            }

            if (_showBoneListsPopup)
            {
                float popupWidth = _windowRect.width;
                float popupHeight = Mathf.Min(580f, Screen.height - 60f);
                float popupY = _windowRect.y - popupHeight - 6f;
                if (popupY < 10f)
                {
                    popupY = Mathf.Min(Screen.height - popupHeight - 10f, _windowRect.y + _windowRect.height + 6f);
                }

                _boneListsWindowRect = new Rect(_windowRect.x, popupY, popupWidth, popupHeight);
                _boneListsWindowRect = GUI.Window(94823, _boneListsWindowRect, DrawBoneListsContents, "Bone Lists (Grey Groups Filter)");
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
            _session.AnimationName = GUILayout.TextField(_session.AnimationName, GUILayout.Width(145));

            string targetName = _session.ActiveCharacter != null ? _session.ActiveCharacter.name : "None";
            GUILayout.Label($"Target: {targetName}", GUILayout.Width(135));

            _session.Loop = GUILayout.Toggle(_session.Loop, "Loop", GUILayout.Width(52));
            _session.Reverse = GUILayout.Toggle(_session.Reverse, "Reverse", GUILayout.Width(65));

            GUILayout.Label("Rate:", GUILayout.Width(35));
            if (float.TryParse(GUILayout.TextField(_session.Rate.ToString("0.0000"), GUILayout.Width(52)), out float newRate))
                _session.Rate = Mathf.Max(0.001f, newRate);

            GUILayout.FlexibleSpace();

            if (!_session.IsPlaying)
            {
                if (GUILayout.Button("▶ Play", GUILayout.Width(68))) _session.PlayPreview();
            }
            else
            {
                if (GUILayout.Button("■ Stop", GUILayout.Width(68))) _session.StopPreview();
            }

            Color prevAllBg = GUI.backgroundColor;
            if (_session.IsAnyPlaying) GUI.backgroundColor = new Color(0.6f, 1f, 0.7f);
            if (!_session.IsAnyPlaying)
            {
                if (GUILayout.Button("▶ All ▶", GUILayout.Width(70))) _session.PlayAllPreviews();
            }
            else
            {
                if (GUILayout.Button("■ All ■", GUILayout.Width(70))) _session.StopAllPreviews();
            }
            GUI.backgroundColor = prevAllBg;

            if (GUILayout.Button("Save JSON", GUILayout.Width(82)))
            {
                _session.SaveToJson();
            }

            Color oldBg = GUI.backgroundColor;
            if (_showLoadPopup) GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button("Load JSON", GUILayout.Width(82)))
            {
                if (_showLoadPopup)
                {
                    CloseLoadGallery();
                }
                else
                {
                    _showBoneListsPopup = false;
                    OpenLoadGallery();
                }
            }
            GUI.backgroundColor = oldBg;

            Color oldBg2 = GUI.backgroundColor;
            if (_showBoneListsPopup) GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button("Bone Lists", GUILayout.Width(82)))
            {
                if (_showBoneListsPopup)
                {
                    _showBoneListsPopup = false;
                }
                else
                {
                    CloseLoadGallery();
                    _boneListsScroll = Vector2.zero;
                    _showBoneListsPopup = true;
                }
            }
            GUI.backgroundColor = oldBg2;

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
                    fontSize = 12,
                    padding = new RectOffset(2, 2, 2, 2)
                };
            }
            if (_cardButtonStyle == null)
            {
                _cardButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    padding = new RectOffset(4, 4, 4, 4),
                    margin = new RectOffset(3, 3, 3, 3),
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12
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
            // Сетка карточек: 6 столбцов, квадратное превью во всю ширину карточки + подпись снизу
            const int columns = 6;
            const float cardWidth = 172f;
            const float iconSize = 164f;
            const float cardHeight = 204f;
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
                        GUILayout.Space(cardWidth + 6f);
                        continue;
                    }
                    string animName = visibleFiles[idx];
                    _loadedThumbnails.TryGetValue(animName, out Texture2D iconTex);
                    Rect cardRect = GUILayoutUtility.GetRect(cardWidth, cardHeight, GUILayout.Width(cardWidth), GUILayout.Height(cardHeight));
                    bool clicked = GUI.Button(cardRect, GUIContent.none, _cardButtonStyle);
                    Rect imgRect = new Rect(cardRect.x + 4f, cardRect.y + 4f, iconSize, iconSize);
                    if (iconTex != null)
                    {
                        GUI.DrawTexture(imgRect, iconTex, ScaleMode.ScaleToFit);
                    }
                    else
                    {
                        GUI.Label(imgRect, "No Preview", _cardLabelStyle);
                    }
                    Rect labelRect = new Rect(cardRect.x + 4f, imgRect.yMax + 2f, iconSize, cardHeight - iconSize - 8f);
                    GUI.Label(labelRect, animName, _cardLabelStyle);
                    if (clicked)
                    {
                        _session.LoadFromJson(animName);
                        CloseLoadGallery();
                        GUILayout.EndHorizontal();
                        GUILayout.EndScrollView();
                        return;
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }
        private void DrawBoneListsContents(int id)
        {
            if (_boneListTextStyle == null)
            {
                _boneListTextStyle = new GUIStyle(GUI.skin.label)
                {
                    wordWrap = true,
                    fontSize = 12,
                    padding = new RectOffset(4, 4, 2, 2)
                };
            }
            var profile = _session.FilterConfig.CurrentProfile;
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label($"Profile: {profile.Name} — check the bone groups to be included at Save JSON:", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Save Config", GUILayout.Width(95)))
            {
                _session.FilterConfig.Save();
            }
            if (GUILayout.Button("Reset Defaults", GUILayout.Width(105)))
            {
                _session.FilterConfig.ResetCurrentToDefault();
            }
            if (GUILayout.Button("Закрыть", GUILayout.Width(80)))
            {
                _showBoneListsPopup = false;
                GUILayout.EndHorizontal();
                return;
            }
            GUILayout.EndHorizontal();
            _boneListsScroll = GUILayout.BeginScrollView(_boneListsScroll, GUILayout.ExpandHeight(true));
            if (profile.GreyGroups != null)
            {
                foreach (var group in profile.GreyGroups)
                {
                    if (group == null) continue;
                    GUILayout.BeginHorizontal(GUI.skin.box);
                    Color prevColor = GUI.contentColor;
                    GUI.contentColor = group.Enabled ? new Color(0.55f, 1f, 0.65f) : new Color(0.75f, 0.75f, 0.75f);
                    bool newEnabled = GUILayout.Toggle(
                    group.Enabled,
                    $" {group.GroupName} ({group.Bones?.Count ?? 0})",
                    GUILayout.Width(190)
                    );
                    if (newEnabled != group.Enabled)
                    {
                        group.Enabled = newEnabled;
                        _session.FilterConfig.Save();
                    }
                    string bonesJoined = (group.Bones != null && group.Bones.Count > 0)
                    ? string.Join(", ", group.Bones)
                    : "(empty)";
                    GUILayout.Label(bonesJoined, _boneListTextStyle, GUILayout.ExpandWidth(true));
                    GUI.contentColor = prevColor;
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
        }
        private void DrawTimelinePanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(718), GUILayout.ExpandHeight(true));
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

            // Счётчик длительности анимации
            int totalFrames = 0;
            for (int i = 1; i < _session.Keyframes.Count; i++)
            {
                totalFrames += Mathf.Max(1, _session.Keyframes[i].frames);
            }
            float totalSeconds = totalFrames * _session.Rate;
            string reverseInfo = _session.Reverse ? $" | ⇄ {totalSeconds * 2f:0.00}s" : "";

            GUILayout.FlexibleSpace();
            GUILayout.Label($"Duration: {totalSeconds:0.00}s ({totalFrames}f){reverseInfo}");

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