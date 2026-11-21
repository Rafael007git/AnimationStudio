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

            var old = Keyframes[SelectedIndex];
            var updated = ReadCurrentSkeleton(ActiveCharacter);
            updated.frames = old.frames;
            updated.easeMode = old.easeMode;
            updated.bezier = old.bezier != null ? (float[])old.bezier.Clone() : null;
            Keyframes[SelectedIndex] = updated;
        }

        public void DeleteSelectedFrame()
        {
            if (SelectedIndex < 0 || SelectedIndex >= Keyframes.Count) return;
            Keyframes.RemoveAt(SelectedIndex);
            SelectedIndex = Mathf.Clamp(SelectedIndex - 1, -1, Keyframes.Count - 1);
        }

        public void SelectAndSnap(int index)
        {
            if (index < 0 || index >= Keyframes.Count) return;
            SelectedIndex = index;
            IsPlaying = false;

            RefreshActiveCharacter();
            if (ActiveCharacter == null) return;

            var kf = Keyframes[index];
            var snapshot = new Dictionary<string, BoneDelta>();
            foreach (var kv in kf.bones)
            {
                snapshot[kv.Key] = new BoneDelta(
                    ToArr(kv.Value.pos),
                    ToArr(kv.Value.rot)
                );
            }

            FurnitureAnimAPI.SnapCharacterToPose(ActiveCharacter, snapshot);

            if (!_hasAnchor)
            {
                _anchorPos = ActiveCharacter.transform.localPosition;
                _anchorRot = ActiveCharacter.transform.localEulerAngles;
                _hasAnchor = true;
            }

            var f0 = Keyframes[0];
            Vector3 deltaPos = kf.rootPos - f0.rootPos;
            Quaternion anchorQ = Quaternion.Euler(_anchorRot);

            ActiveCharacter.transform.localPosition = _anchorPos + (anchorQ * deltaPos);
            ActiveCharacter.transform.localEulerAngles = _anchorRot + (kf.rootRot - f0.rootRot);
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

            // Frame 0
            var f0 = new KeyframeEntry { rootPos = baseRootPos, rootRot = baseRootRot };
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

            // Deltas -> Keyframes 1..N
            if (data.deltas != null)
            {
                foreach (var d in data.deltas)
                {
                    var kf = new KeyframeEntry
                    {
                        frames = Mathf.Max(1, d.frames),
                        easeMode = d.easeMode,
                        bezier = d.bezier != null && d.bezier.Length == 4 ? (float[])d.bezier.Clone() : null,
                        rootPos = baseRootPos + FromArr(d.endPosDelta),
                        rootRot = baseRootRot + FromArr(d.endRotDelta)
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

        public void RealignAnchor()
        {
            RefreshActiveCharacter();
            if (ActiveCharacter == null || Keyframes.Count == 0) return;

            int savedIndex = SelectedIndex;
            SelectAndSnap(0);

            Transform charRoot = ActiveCharacter.transform;
            Transform hipBone = null;

            foreach (Transform t in charRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t != charRoot && t.name == "hip")
                {
                    hipBone = t;
                    break;
                }
            }
            if (hipBone == null) return;

            // 1. Запоминаем точное мировое положение и поворот таза hip, чтобы кукла не шелохнулась
            Vector3 oldHipWorldPos = hipBone.position;
            Quaternion oldHipWorldRot = hipBone.rotation;

            // 2. Ищем высоту пола (Y) вертикальным лучом вниз из hip (игнорируя коллайдеры самой куклы)
            float floorY = charRoot.position.y;
            RaycastHit[] hits = Physics.RaycastAll(hipBone.position + Vector3.up * 0.5f, Vector3.down, 4.0f, ~0, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.transform == null || hit.transform == charRoot || hit.transform.IsChildOf(charRoot)) continue;
                if (hit.distance < bestDist)
                {
                    bestDist = hit.distance;
                    floorY = hit.point.y;
                }
            }

            // 3. Вычисляем новую мировую точку креста на полу строго под тазом hip
            Vector3 newRootWorldPos = new Vector3(oldHipWorldPos.x, floorY, oldHipWorldPos.z);
            if (Vector3.Distance(charRoot.position, newRootWorldPos) >= 3.0f)
            {
                AnimationStudioPlugin.Log?.LogWarning("[AnimationStudio] Realign aborted: target floor point is too far.");
                return;
            }

            // 4. Вычисляем новый мировой поворот креста (стрелка по проекции hip.forward на пол)
            Vector3 flatHipFwd = new Vector3(hipBone.forward.x, 0f, hipBone.forward.z);
            Quaternion newRootWorldRot = flatHipFwd.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(flatHipFwd.normalized, Vector3.up)
                : charRoot.rotation;

            // 5. Ставим корень (якорь) в новую точку и сразу вычисляем точные компенсирующие локальные pos/rot для hip
            Quaternion oldRootWorldRot = charRoot.rotation;
            Vector3 oldRootWorldPos = charRoot.position;

            charRoot.position = newRootWorldPos;
            charRoot.rotation = newRootWorldRot;

            // Точная разница в локальном пространстве корня, чтобы вернуть hip в oldHipWorldPos и oldHipWorldRot
            Quaternion localRotDelta = Quaternion.Inverse(newRootWorldRot) * oldRootWorldRot;
            Vector3 newHipTargetLocalPos = charRoot.InverseTransformPoint(oldHipWorldPos);
            Vector3 oldHipLocalPosBeforeMove = Keyframes[0].bones.TryGetValue("hip", out var f0Hip) ? f0Hip.pos : hipBone.localPosition;
            Vector3 hipLocalPosOffset = newHipTargetLocalPos - (localRotDelta * oldHipLocalPosBeforeMove);

            // 6. Обновляем базу Frame 0 и применяем компенсацию к root и hip во всех кадрах 0..N
            Vector3 oldF0Pos = Keyframes[0].rootPos;
            Vector3 oldF0Rot = Keyframes[0].rootRot;

            _anchorPos = charRoot.localPosition;
            _anchorRot = charRoot.localEulerAngles;
            _hasAnchor = true;

            Quaternion rootLocalRotDiff = Quaternion.Euler(_anchorRot) * Quaternion.Inverse(Quaternion.Euler(oldF0Rot));

            for (int i = 0; i < Keyframes.Count; i++)
            {
                var kf = Keyframes[i];
                kf.rootPos = _anchorPos + (rootLocalRotDiff * (kf.rootPos - oldF0Pos));
                kf.rootRot = (rootLocalRotDiff * Quaternion.Euler(kf.rootRot)).eulerAngles;

                if (kf.bones.TryGetValue("hip", out var hp))
                {
                    hp.pos = (localRotDelta * hp.pos) + hipLocalPosOffset;
                    hp.rot = (localRotDelta * Quaternion.Euler(hp.rot)).eulerAngles;
                    kf.bones["hip"] = hp;
                }
            }

            SelectAndSnap(Mathf.Clamp(savedIndex, 0, Keyframes.Count - 1));
            AnimationStudioPlugin.Log?.LogInfo($"[AnimationStudio] Anchor realigned to floor under hip at {newRootWorldPos}.");
        }

        public string[] GetAvailableJsonFiles()
        {
            if (!Directory.Exists(SaveFolder)) return Array.Empty<string>();
            var files = Directory.GetFiles(SaveFolder, "*.json");
            for (int i = 0; i < files.Length; i++)
                files[i] = Path.GetFileNameWithoutExtension(files[i]);
            return files;
        }

        private KeyframeEntry ReadCurrentSkeleton(CharacterCustomization character)
        {
            var entry = new KeyframeEntry
            {
                rootPos = character.transform.localPosition,
                rootRot = character.transform.localEulerAngles
            };

            var transforms = character.GetComponentsInChildren<Transform>(true);
            var lookup = new Dictionary<string, Transform>();
            foreach (var t in transforms)
            {
                if (!lookup.ContainsKey(t.name))
                    lookup[t.name] = t;
            }

            foreach (var boneName in DioramaConstants.AnatomyBoneRegistry)
            {
                if (lookup.TryGetValue(boneName, out Transform bone))
                {
                    entry.bones[boneName] = new BonePose
                    {
                        pos = bone.localPosition,
                        rot = bone.localEulerAngles
                    };
                }
            }
            return entry;
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
            var f0 = Keyframes[0];
            var data = new PoseAnimationData
            {
                name = AnimationName,
                rate = Rate,
                loop = Loop,
                reverse = Reverse,
                startPos = ToArr(f0.rootPos),
                startRot = ToArr(f0.rootRot)
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
