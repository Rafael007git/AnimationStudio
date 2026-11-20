using BepInEx;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace AnimationStudio
{
    public class GreyGroup
    {
        public string Name;
        public bool Enabled = true;        // Участвует ли группа в экспорте
        public bool OnlyIfMoved = true;    // Писать только если кость реально меняла pos, rot или scale
        public List<string> Patterns = new List<string>();
    }

    public class BoneFilterProfile
    {
        public string ProfileName = "Default (Genesis 8)";

        // Пороги чувствительности для авто-детекта движения по 3 каналам
        public float MinAngleDelta = 0.05f;
        public float MinPosDelta = 0.0005f;
        public float MinScaleDelta = 0.005f;

        // 1. Чёрный список: всегда игнорируются (служебные твисты и свет по умолчанию)
        public List<string> Blacklist = new List<string>
        {
            "Twist",
            "Point Light"
        };

        // 2. Белый список: если не пуст — записываются ТОЛЬКО кости, совпавшие с ним
        public List<string> Whitelist = new List<string>();

        // 3. Серые списки: гибкие группы под риг Genesis 8 Female
        public List<GreyGroup> GreyLists = new List<GreyGroup>
        {
            new GreyGroup
            {
                Name = "Breasts (Pectoral)",
                Enabled = true,
                OnlyIfMoved = true, // Если не трогали — работает физика игры; если поправили pos/rot/scale — запишется!
                Patterns = new List<string> { "Pectoral" }
            },
            new GreyGroup
            {
                Name = "Fingers & Carpals",
                Enabled = true,
                OnlyIfMoved = true,
                Patterns = new List<string>
                {
                    "Carpal", "Thumb", "Index", "Mid1", "Mid2", "Mid3", "Ring", "Pinky"
                }
            },
            new GreyGroup
            {
                Name = "Face, Eyes & Tongue",
                Enabled = true,
                OnlyIfMoved = true,
                Patterns = new List<string>
                {
                    "Jaw", "Teeth", "Eye", "Brow", "Cheek", "Squint", "Lip",
                    "Nasolabial", "Nose", "Nostril", "Chin", "FaceRig", "Tongue", "Ear"
                }
            },
            new GreyGroup
            {
                Name = "Pelvis & Legs",
                Enabled = true,
                OnlyIfMoved = false, // Для анимаций всего тела пишем даже статичные ноги
                Patterns = new List<string>
                {
                    "Genesis8Female", "hip", "pelvis", "Thigh", "Shin", "Foot", "Metatarsals", "Toe"
                }
            },
            new GreyGroup
            {
                Name = "Intimate Anatomy",
                Enabled = true,
                OnlyIfMoved = true,
                Patterns = new List<string>
                {
                    "Rectum", "Vagina", "Clitoris", "Colon", "Labia", "Labium"
                }
            }
        };

        /// <summary>
        /// Проверяет, должна ли кость попасть в итоговый JSON.
        /// hasMoved = true, если за анимацию менялся хотя бы один из параметров: position, rotation или scale.
        /// </summary>
        public bool ShouldIncludeBone(string boneName, bool hasMoved)
        {
            // 1. Абсолютный запрет по чёрному списку
            if (MatchesAny(boneName, Blacklist))
                return false;

            // 2. Жёсткий отбор по белому списку (если он заполнен)
            if (Whitelist != null && Whitelist.Count > 0 && !MatchesAny(boneName, Whitelist))
                return false;

            // 3. Проверка серых списков
            if (GreyLists != null)
            {
                foreach (var grey in GreyLists)
                {
                    if (MatchesAny(boneName, grey.Patterns))
                    {
                        if (!grey.Enabled)
                            return false;

                        if (grey.OnlyIfMoved && !hasMoved)
                            return false;
                    }
                }
            }

            return true;
        }

        private static bool MatchesAny(string boneName, List<string> patterns)
        {
            if (patterns == null || string.IsNullOrEmpty(boneName)) return false;

            for (int i = 0; i < patterns.Count; i++)
            {
                string p = patterns[i];
                if (!string.IsNullOrEmpty(p) && boneName.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        public static string ConfigPath => Path.Combine(Paths.PluginPath, "AnimationStudio_Filters.json");

        public static BoneFilterProfile LoadOrCreateDefault()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    var loaded = JsonConvert.DeserializeObject<BoneFilterProfile>(File.ReadAllText(ConfigPath));
                    if (loaded != null) return loaded;
                }
                catch (Exception ex)
                {
                    AnimationStudioPlugin.Log?.LogWarning($"[AnimationStudio] Ошибка чтения AnimationStudio_Filters.json, создан профиль по умолчанию: {ex.Message}");
                }
            }

            var def = new BoneFilterProfile();
            def.Save();
            return def;
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                AnimationStudioPlugin.Log?.LogWarning($"[{nameof(AnimationStudio)}] Failed to save AnimationStudio_Filters.json: {ex.Message}");
            }
        }
    }
}
