using BepInEx;
using FurnitureAnimationsMod;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEngine;

namespace AnimationStudio
{
    public class KeyframeEntry
    {
        public int frames = 30;
        public EaseMode? easeMode = null;
        public float[] bezier = null; // [x1, y1, x2, y2]
        public Vector3 rootPos;
        public Vector3 rootRot;
        public Dictionary<string, BonePose> bones = new Dictionary<string, BonePose>();
    }

    public struct BonePose
    {
        public Vector3 pos;
        public Vector3 rot;
    }

    public class StudioSession
    {
        public string AnimationName = "New_Animation";
        public float Rate = 0.025f;
        public bool Loop = true;
        public bool Reverse = false;

        public List<KeyframeEntry> Keyframes = new List<KeyframeEntry>();
        public int SelectedIndex = -1;
        public bool IsPlaying = false;
        public CharacterCustomization ActiveCharacter { get; private set; }
        public BoneFilterConfig FilterConfig { get; private set; }
        public StudioSession()
        {
            FilterConfig = BoneFilterConfig.Load();
        }
        public BoneFilterProfile ActiveFilter => FilterConfig.CurrentProfile;
        private Vector3 _anchorPos;
        private Vector3 _anchorRot;
        private bool _hasAnchor;
        private readonly AnchorGizmoMarker _anchorMarker = new AnchorGizmoMarker();
        private string SaveFolder => Path.Combine(Paths.PluginPath, "PoseAnimations");
        private UIFreePose _cachedFreePoseUI;

        public void RefreshActiveCharacter()
        {
            CharacterCustomization target = null;

            // Priority 1: Directly from game's Global.code.uiFreePose singleton
            if (Global.code != null && Global.code.uiFreePose != null && Global.code.uiFreePose.gameObject.activeInHierarchy)
            {
                if (Global.code.uiFreePose.selectedCharacter != null)
                {
                    target = Global.code.uiFreePose.selectedCharacter.GetComponent<CharacterCustomization>();
                }
            }

            // Priority 2: Fallback if Free Pose is closed
            if (target == null)
            {
                if (ActiveCharacter != null && ActiveCharacter.gameObject.activeInHierarchy)
                    return;

                if (Player.code != null)
                {
                    target = Player.code.GetComponent<CharacterCustomization>();
                }
            }

            if (target != null && target != ActiveCharacter)
            {
                if (ActiveCharacter != null)
                {
                    FurnitureAnimAPI.StopCompanionAnimation(ActiveCharacter);
                    IsPlaying = false;
                }

                ActiveCharacter = target;
                _anchorPos = ActiveCharacter.transform.localPosition;
                _anchorRot = ActiveCharacter.transform.localEulerAngles;
                _hasAnchor = true;

                AnimationStudioPlugin.Log?.LogInfo($"[AnimationStudio] Target character switched to: {ActiveCharacter.name}");

                if (Keyframes.Count > 0 && SelectedIndex >= 0 && SelectedIndex < Keyframes.Count)
                {
                    SelectAndSnap(SelectedIndex);
                }
            }
        }

        public void ReleaseCharacter()
        {
            if (ActiveCharacter == null) return;

            FurnitureAnimAPI.StopCompanionAnimation(ActiveCharacter);
            IsPlaying = false;

            AnimationStudioPlugin.Log?.LogInfo($"[AnimationStudio] Released character control: {ActiveCharacter.name}");
            ActiveCharacter = null;
            _hasAnchor = false;
        }

        public void UpdateAnchorAndGizmo(bool isStudioVisible)
        {
            if (!isStudioVisible || ActiveCharacter == null || !ActiveCharacter.gameObject.activeInHierarchy)
            {
                _anchorMarker.UpdateTransform(Vector3.zero, Quaternion.identity, false);
                return;
            }

            if (!_hasAnchor)
            {
                _anchorPos = ActiveCharacter.transform.localPosition;
                _anchorRot = ActiveCharacter.transform.localEulerAngles;
                _hasAnchor = true;
            }

            // На Кадре 0 (или когда кадров еще нет) и не на воспроизведении — крест автоматически следует за куклой
            if (!IsPlaying && SelectedIndex <= 0)
            {
                _anchorPos = ActiveCharacter.transform.localPosition;
                _anchorRot = ActiveCharacter.transform.localEulerAngles;
            }

            // Отрисовываем крест в мировых координатах (учитывая возможного родителя персонажа)
            Transform parent = ActiveCharacter.transform.parent;
            Vector3 worldPos = parent != null ? parent.TransformPoint(_anchorPos) : _anchorPos;
            Quaternion worldRot = parent != null ? parent.rotation * Quaternion.Euler(_anchorRot) : Quaternion.Euler(_anchorRot);

            _anchorMarker.UpdateTransform(worldPos, worldRot, true);
        }

        public void CaptureNewFrame(int insertAfterIndex = -1)
        {
            RefreshActiveCharacter();
            if (ActiveCharacter == null) return;

            if (Keyframes.Count == 0)
            {
                _anchorPos = ActiveCharacter.transform.localPosition;
                _anchorRot = ActiveCharacter.transform.localEulerAngles;
                _hasAnchor = true;
            }

            var entry = ReadCurrentSkeleton(ActiveCharacter);

            if (insertAfterIndex >= 0 && insertAfterIndex < Keyframes.Count)
            {
                Keyframes.Insert(insertAfterIndex + 1, entry);
                SelectedIndex = insertAfterIndex + 1;
            }
            else
            {
                Keyframes.Add(entry);
                SelectedIndex = Keyframes.Count - 1;
            }
        }

        public void UpdateSelectedFrame()
        {
            RefreshActiveCharacter();
            if (ActiveCharacter == null || SelectedIndex < 0 || SelectedIndex >= Keyframes.Count) return;

            var prev = Keyframes[SelectedIndex];

            if (SelectedIndex == 0)
            {
                _anchorPos = ActiveCharacter.transform.localPosition;
                _anchorRot = ActiveCharacter.transform.localEulerAngles;
                _hasAnchor = true;
            }

            var updated = ReadCurrentSkeleton(ActiveCharacter);
            updated.frames = prev.frames;
            updated.easeMode = prev.easeMode;
            updated.bezier = prev.bezier != null ? (float[])prev.bezier.Clone() : null;

            Keyframes[SelectedIndex] = updated;
            SelectAndSnap(SelectedIndex);
        }

        public void DeleteSelectedFrame()
        {
            if (SelectedIndex < 0 || SelectedIndex >= Keyframes.Count) return;
            Keyframes.RemoveAt(SelectedIndex);
            SelectedIndex = Mathf.Clamp(SelectedIndex - 1, -1, Keyframes.Count - 1);
        }

        public void PlayPreview()
        {
            RefreshActiveCharacter();
            if (ActiveCharacter == null || Keyframes.Count < 2) return;

            var data = BuildPoseAnimationData(ActiveFilter, bypassFilter: true);

            if (_hasAnchor)
            {
                data.startPos = ToArr(_anchorPos);
                data.startRot = ToArr(_anchorRot);
            }

            FurnitureAnimAPI.StartPreview(ActiveCharacter, data, 1.0f);
            IsPlaying = true;
        }

        public void StopPreview()
        {
            RefreshActiveCharacter();
            if (ActiveCharacter != null)
            {
                FurnitureAnimAPI.StopPreview(ActiveCharacter);
            }
            IsPlaying = false;
        }

        public void SaveToJson()
        {
            if (Keyframes.Count < 2) return;
            Directory.CreateDirectory(SaveFolder);

            var data = BuildPoseAnimationData(ActiveFilter, bypassFilter: false);
            if (_hasAnchor)
            {
                data.startPos = ToArr(_anchorPos);
                data.startRot = ToArr(_anchorRot);
            }

            string safeName = string.IsNullOrWhiteSpace(AnimationName) ? "CustomAnim" : AnimationName.Trim();
            string path = Path.Combine(SaveFolder, safeName + ".json");

            string json = JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
            File.WriteAllText(path, json);
            FurnitureAnimAPI.ReloadAnimationsFromDisk();
            AnimationStudioPlugin.Log.LogInfo($"[AnimationStudio] Saved: {path}");
        }

        public void LoadFromJson(string fileNameWithoutExt)
        {
            string path = Path.Combine(SaveFolder, fileNameWithoutExt + ".json");
            if (!File.Exists(path)) return;

            var data = JsonConvert.DeserializeObject<PoseAnimationData>(File.ReadAllText(path));
            if (data == null) return;

            AnimationName = data.name ?? fileNameWithoutExt;
            Rate = data.rate;
            Loop = data.loop;
            Reverse = data.reverse;
            Keyframes.Clear();

            Vector3 baseRootPos = FromArr(data.startPos);
            Vector3 baseRootRot = FromArr(data.startRot);

            // Frame 0 (всегда в начале локальных координат якоря)
            var f0 = new KeyframeEntry { rootPos = Vector3.zero, rootRot = Vector3.zero };
            if (data.boneStartDict != null)
            {
                foreach (var kv in data.boneStartDict)
                {
                    f0.bones[kv.Key] = new BonePose
                    {
                        pos = FromArr(kv.Value.endPos),
                        rot = FromArr(kv.Value.endRot)
                    };
                }
            }
            Keyframes.Add(f0);

            // Deltas -> Keyframes 1..N (локальные смещения относительно якоря)
            if (data.deltas != null)
            {
                foreach (var d in data.deltas)
                {
                    var kf = new KeyframeEntry
                    {
                        frames = Mathf.Max(1, d.frames),
                        easeMode = d.easeMode,
                        bezier = d.bezier != null && d.bezier.Length == 4 ? (float[])d.bezier.Clone() : null,
                        rootPos = FromArr(d.endPosDelta),
                        rootRot = FromArr(d.endRotDelta)
                    };

                    if (d.boneDatas != null)
                    {
                        foreach (var kv in d.boneDatas)
                        {
                            kf.bones[kv.Key] = new BonePose
                            {
                                pos = FromArr(kv.Value.endPos),
                                rot = FromArr(kv.Value.endRot)
                            };
                        }
                    }
                    Keyframes.Add(kf);
                }
            }

            // Выравниваем пропущенные кости и углы по всей ленте кадров
            InflateLoadedKeyframes();

            SelectedIndex = Keyframes.Count > 0 ? 0 : -1;
            RefreshActiveCharacter();
            if (ActiveCharacter != null)
            {
                _anchorPos = ActiveCharacter.transform.localPosition;
                _anchorRot = ActiveCharacter.transform.localEulerAngles;
                _hasAnchor = true;
            }
            if (SelectedIndex == 0) SelectAndSnap(0);

            AnimationStudioPlugin.Log.LogInfo($"[AnimationStudio] Загружена анимация: {path} (кадров: {Keyframes.Count})");
        }

        public string[] GetAvailableJsonFiles()
        {
            if (!Directory.Exists(SaveFolder)) return Array.Empty<string>();
            var files = Directory.GetFiles(SaveFolder, "*.json");
            for (int i = 0; i < files.Length; i++)
                files[i] = Path.GetFileNameWithoutExtension(files[i]);
            return files;
        }

        private static readonly HashSet<string> NativeIgnoreBones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "makeup loc (1)",
            "lgroup",
            "rgroup",
            "head target"
        };

        private static string FixBoneName(string name)
        {
            if (name.EndsWith("eyesRoot")) return "eyesRoot";
            if (name.EndsWith("head parent")) return "head parent";
            if (name.EndsWith("head target")) return "head target";
            return name;
        }

        private KeyframeEntry ReadCurrentSkeleton(CharacterCustomization character)
        {
            var entry = new KeyframeEntry
            {
                rootPos = _hasAnchor ? (character.transform.localPosition - _anchorPos) : Vector3.zero,
                rootRot = _hasAnchor ? (character.transform.localEulerAngles - _anchorRot) : Vector3.zero
            };

            if (character.bonesNeedSave == null) return entry;

            foreach (Transform t in character.bonesNeedSave)
            {
                if (t == null || t == character.transform) continue;
                string boneName = FixBoneName(t.name);
                if (NativeIgnoreBones.Contains(boneName)) continue;

                entry.bones[boneName] = new BonePose
                {
                    pos = t.localPosition,
                    rot = t.localEulerAngles
                };
            }
            return entry;
        }

        public void SelectAndSnap(int index)
        {
            RefreshActiveCharacter();
            if (ActiveCharacter == null || index < 0 || index >= Keyframes.Count) return;

            // Автоматически включаем режим FREEPOSE, если до этого была выбрана ванильная поза
            if (Global.code != null &&
                Global.code.uiFreePose != null &&
                Global.code.uiFreePose.gameObject.activeInHierarchy &&
                !Global.code.uiFreePose.isCustomPoseMode)
            {
                Global.code.uiFreePose.LetRuntimeTransformRun();
            }

            if (IsPlaying)
            {
                FurnitureAnimAPI.StopPreview(ActiveCharacter);
                IsPlaying = false;
            }

            SelectedIndex = index;
            var kf = Keyframes[index];

            if (!_hasAnchor)
            {
                _anchorPos = ActiveCharacter.transform.localPosition;
                _anchorRot = ActiveCharacter.transform.localEulerAngles;
                _hasAnchor = true;
            }

            ActiveCharacter.transform.localPosition = _anchorPos + kf.rootPos;
            ActiveCharacter.transform.localEulerAngles = _anchorRot + kf.rootRot;

            if (ActiveCharacter.bonesNeedSave == null) return;

            foreach (Transform t in ActiveCharacter.bonesNeedSave)
            {
                if (t == null) continue;
                string boneName = FixBoneName(t.name);
                if (NativeIgnoreBones.Contains(boneName)) continue;

                if (kf.bones.TryGetValue(boneName, out var bonePose))
                {
                    t.localPosition = bonePose.pos;
                    t.localEulerAngles = bonePose.rot;
                }
            }
        }

        // Вызывается в конце LoadFromJson(string fileNameWithoutExt) перед SelectAndSnap(0)
        private void InflateLoadedKeyframes()
        {
            if (Keyframes.Count < 2) return;

            // 1. Собираем все кости, встречающиеся хоть в одном кадре
            var allBones = new HashSet<string>();
            foreach (var kf in Keyframes)
                foreach (var b in kf.bones.Keys)
                    allBones.Add(b);

            // 2. Протягиваем значения вперед по всей ленте кадров
            for (int i = 0; i < Keyframes.Count; i++)
            {
                var curr = Keyframes[i];
                foreach (var boneName in allBones)
                {
                    if (!curr.bones.ContainsKey(boneName))
                    {
                        if (i > 0 && Keyframes[i - 1].bones.TryGetValue(boneName, out var prevPose))
                        {
                            curr.bones[boneName] = prevPose;
                        }
                    }
                }
            }
        }

        // Сборка PoseAnimationData с фильтрами (Black/White/Grey), инфляцией и выпрямлением углов
        private PoseAnimationData BuildPoseAnimationData(BoneFilterProfile filter, bool bypassFilter = false)
        {
            Vector3 basePos = _hasAnchor ? _anchorPos : Vector3.zero;
            Vector3 baseRot = _hasAnchor ? _anchorRot : Vector3.zero;
            var f0 = Keyframes[0];

            var data = new PoseAnimationData
            {
                name = AnimationName,
                rate = Rate,
                loop = Loop,
                reverse = Reverse,
                startPos = ToArr(basePos + f0.rootPos),
                startRot = ToArr(baseRot + f0.rootRot)
            };

            // 1. Собираем все уникальные кости и проверяем по активному профилю фильтра
            var allBones = new HashSet<string>();
            foreach (var kf in Keyframes)
                foreach (var b in kf.bones.Keys)
                    allBones.Add(b);

            var approvedBones = new List<string>();
            foreach (var boneName in allBones)
            {
                if (bypassFilter || filter.ShouldIncludeBone(boneName))
                {
                    approvedBones.Add(boneName);
                }
            }

            // 2. Заполняем boneStartDict для всех прошедших фильтр костей
            foreach (var boneName in approvedBones)
            {
                if (f0.bones.TryGetValue(boneName, out var startPose))
                {
                    data.boneStartDict[boneName] = new BoneDelta(ToArr(startPose.pos), ToArr(startPose.rot));
                }
            }

            // 3. Строим бесшовную цепочку дельт с выпрямлением углов
            for (int i = 1; i < Keyframes.Count; i++)
            {
                var curr = Keyframes[i];
                var delta = new PoseAnimationDelta
                {
                    frames = Mathf.Max(1, curr.frames),
                    easeMode = curr.easeMode,
                    bezier = curr.bezier != null ? (float[])curr.bezier.Clone() : null,
                    endPosDelta = ToArr(curr.rootPos - f0.rootPos),
                    endRotDelta = ToArr(curr.rootRot - f0.rootRot)
                };

                foreach (var boneName in approvedBones)
                {
                    // Берем точку старта строго из финиша предыдущей дельты (или boneStartDict)
                    float[] prevPos = (i > 1 && data.deltas[i - 2].boneDatas.TryGetValue(boneName, out var prevDelta))
                        ? prevDelta.endPos
                        : (data.boneStartDict.TryGetValue(boneName, out var bStart) ? bStart.endPos : null);

                    float[] prevRot = (i > 1 && data.deltas[i - 2].boneDatas.TryGetValue(boneName, out var prevDeltaR))
                        ? prevDeltaR.endRot
                        : (data.boneStartDict.TryGetValue(boneName, out var bStartR) ? bStartR.endRot : null);

                    if (prevPos == null || prevRot == null) continue;

                    if (curr.bones.TryGetValue(boneName, out var targetPose))
                    {
                        delta.boneDatas[boneName] = new BoneDelta(
                            prevPos,
                            ToArr(targetPose.pos),
                            prevRot,
                            ToArr(targetPose.rot)
                        );
                    }
                    else
                    {
                        // Инфляция: если кость пропущена в кадре, фиксируем её на месте прошлого кадра
                        delta.boneDatas[boneName] = new BoneDelta(prevPos, prevPos, prevRot, prevRot);
                    }
                }

                data.deltas.Add(delta);
            }

            return data;
        }

        private bool CheckBoneMovedAcrossTimeline(string boneName, float minAngle, float minPos)
        {
            if (Keyframes.Count < 2 || !Keyframes[0].bones.TryGetValue(boneName, out var basePose))
                return false;

            for (int i = 1; i < Keyframes.Count; i++)
            {
                if (!Keyframes[i].bones.TryGetValue(boneName, out var p)) continue;

                if (Vector3.Distance(basePose.pos, p.pos) > minPos) return true;
                if (Mathf.Abs(Mathf.DeltaAngle(basePose.rot.x, p.rot.x)) > minAngle) return true;
                if (Mathf.Abs(Mathf.DeltaAngle(basePose.rot.y, p.rot.y)) > minAngle) return true;
                if (Mathf.Abs(Mathf.DeltaAngle(basePose.rot.z, p.rot.z)) > minAngle) return true;
            }
            return false;
        }

        private static Vector3 StraightenEuler(Vector3 start, Vector3 end)
        {
            return new Vector3(
                start.x + Mathf.DeltaAngle(start.x, end.x),
                start.y + Mathf.DeltaAngle(start.y, end.y),
                start.z + Mathf.DeltaAngle(start.z, end.z)
            );
        }

        private static float[] ToArr(Vector3 v) => new[] { v.x, v.y, v.z };

        private static Vector3 FromArr(float[] a) =>
            (a != null && a.Length >= 3) ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
    }
}
