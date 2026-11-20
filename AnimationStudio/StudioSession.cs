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
        public BoneFilterProfile ActiveFilter = new BoneFilterProfile();

        private string SaveFolder => Path.Combine(Paths.PluginPath, "PoseAnimations");

        public void RefreshActiveCharacter()
        {
            if (ActiveCharacter != null && ActiveCharacter.gameObject.activeInHierarchy)
                return;

            var allChars = UnityEngine.Object.FindObjectsOfType<CharacterCustomization>();
            foreach (var c in allChars)
            {
                if (c != null && c.gameObject.activeInHierarchy)
                {
                    ActiveCharacter = c;
                    break;
                }
            }
        }

        public void CaptureNewFrame(int insertAfterIndex = -1)
        {
            RefreshActiveCharacter();
            if (ActiveCharacter == null) return;

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
            ActiveCharacter.transform.localPosition = kf.rootPos;
            ActiveCharacter.transform.localEulerAngles = kf.rootRot;
        }

        public void PlayPreview()
        {
            RefreshActiveCharacter();
            if (ActiveCharacter == null || Keyframes.Count < 2) return;

            var data = BuildPoseAnimationData(ActiveFilter);
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

            var data = BuildPoseAnimationData(ActiveFilter);
            string safeName = string.IsNullOrWhiteSpace(AnimationName) ? "CustomAnim" : AnimationName.Trim();
            string path = Path.Combine(SaveFolder, safeName + ".json");

            string json = JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });
            File.WriteAllText(path, json);
            AnimationStudioPlugin.Log.LogInfo($"[AnimationStudio] Сохранено: {path}");
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
                    else if (i > 0 && Keyframes[i - 1].bones.TryGetValue(boneName, out var prevPose))
                    {
                        // Выпрямляем углы при загрузке, чтобы на таймлайне не было скачков на 360°
                        var p = curr.bones[boneName];
                        p.rot = StraightenEuler(prevPose.rot, p.rot);
                        curr.bones[boneName] = p;
                    }
                }
            }
        }

        // Сборка PoseAnimationData с фильтрами (Black/White/Grey), инфляцией и выпрямлением углов
        private PoseAnimationData BuildPoseAnimationData(BoneFilterProfile filter)
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

            // 1. Собираем все уникальные кости и проверяем, двигалась ли каждая хоть раз за всю анимацию
            var allBones = new HashSet<string>();
            foreach (var kf in Keyframes)
                foreach (var b in kf.bones.Keys)
                    allBones.Add(b);

            var approvedBones = new List<string>();
            foreach (var boneName in allBones)
            {
                bool hasMoved = CheckBoneMovedAcrossTimeline(boneName, filter.MinAngleDelta, filter.MinPosDelta);
                if (filter.ShouldIncludeBone(boneName, hasMoved))
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
                        Vector3 sAngles = FromArr(prevRot);
                        Vector3 eAngles = StraightenEuler(sAngles, targetPose.rot);

                        delta.boneDatas[boneName] = new BoneDelta(
                            prevPos,
                            ToArr(targetPose.pos),
                            prevRot,
                            ToArr(eAngles)
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
