using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AnimationStudio
{
    [Serializable]
    public class BoneGroupEntry
    {
        public string GroupName;
        public bool Enabled;
        public List<string> Bones = new List<string>();
    }

    [Serializable]
    public class BoneFilterProfile
    {
        public string Name = "SWPT_Default";
        public bool SaveAllUnlistedBones = true;
        public List<string> BlackList = new List<string>();
        public List<string> WhiteList = new List<string>();
        public List<BoneGroupEntry> GreyGroups = new List<BoneGroupEntry>();

        public bool ShouldIncludeBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName)) return false;

            if (BlackList != null && BlackList.Contains(boneName))
                return false;

            if (GreyGroups != null)
            {
                foreach (var group in GreyGroups)
                {
                    if (group != null && group.Bones != null && group.Bones.Contains(boneName))
                        return group.Enabled;
                }
            }

            if (WhiteList != null && WhiteList.Contains(boneName))
                return true;

            return SaveAllUnlistedBones;
        }
    }

    [Serializable]
    public class BoneFilterConfig
    {
        public int ActiveProfileIndex = 0;
        public List<BoneFilterProfile> Profiles = new List<BoneFilterProfile>();

        private static string ConfigPath => Path.Combine(BepInEx.Paths.ConfigPath, "AnimationStudio_Filters.json");

        public BoneFilterProfile CurrentProfile
        {
            get
            {
                if (Profiles == null || Profiles.Count == 0)
                {
                    Profiles = new List<BoneFilterProfile> { CreateDefaultProfile() };
                    ActiveProfileIndex = 0;
                }
                ActiveProfileIndex = Mathf.Clamp(ActiveProfileIndex, 0, Profiles.Count - 1);
                return Profiles[ActiveProfileIndex];
            }
        }

        public static BoneFilterConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var cfg = JsonConvert.DeserializeObject<BoneFilterConfig>(json);
                    if (cfg != null && cfg.Profiles != null && cfg.Profiles.Count > 0)
                    {
                        // Auto-upgrade old config format if GreyGroups was empty
                        if (cfg.CurrentProfile.GreyGroups == null || cfg.CurrentProfile.GreyGroups.Count == 0)
                        {
                            cfg.Profiles[cfg.ActiveProfileIndex] = CreateDefaultProfile();
                            cfg.Save();
                        }
                        return cfg;
                    }
                }
            }
            catch (Exception ex)
            {
                AnimationStudioPlugin.Log?.LogWarning($"[AnimationStudio] Failed to load AnimationStudio_Filters.json: {ex.Message}");
            }

            var defaultCfg = new BoneFilterConfig
            {
                ActiveProfileIndex = 0,
                Profiles = new List<BoneFilterProfile> { CreateDefaultProfile() }
            };
            defaultCfg.Save();
            return defaultCfg;
        }

        public void ResetCurrentToDefault()
        {
            if (Profiles == null || Profiles.Count == 0)
                Profiles = new List<BoneFilterProfile> { CreateDefaultProfile() };
            else
                Profiles[Mathf.Clamp(ActiveProfileIndex, 0, Profiles.Count - 1)] = CreateDefaultProfile();

            Save();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented));
                AnimationStudioPlugin.Log?.LogInfo("[AnimationStudio] Saved bone filter config to AnimationStudio_Filters.json.");
            }
            catch (Exception ex)
            {
                AnimationStudioPlugin.Log?.LogWarning($"[AnimationStudio] Failed to save AnimationStudio_Filters.json: {ex.Message}");
            }
        }

        public static BoneFilterProfile CreateDefaultProfile()
        {
            return new BoneFilterProfile
            {
                Name = "SWPT_Default",
                SaveAllUnlistedBones = true,
                BlackList = new List<string>
                {
                    "lowerFaceRig", "lBrowInner", "lBrowMid", "lBrowOuter",
                    "lCheekLower", "lCheekUpper", "lEar", "lNasolabialLower",
                    "lNasolabialMiddle", "lNasolabialMouthCorner", "lNasolabialUpper",
                    "lNostril", "lSquintInner", "lSquintOuter",
                    "rBrowInner", "rBrowMid", "rBrowOuter",
                    "rCheekLower", "rCheekUpper", "rEar", "rNasolabialLower",
                    "rNasolabialMiddle", "rNasolabialMouthCorner", "rNasolabialUpper",
                    "rNostril", "rSquintInner", "rSquintOuter",
                    "CenterBrow", "MidNoseBridge", "NoseTip",
                    "upperFaceRig", "eyesRoot",
                    "lEye", "lEyelidInner", "lEyelidLower", "lEyelidLowerInner",
                    "lEyelidLowerOuter", "lEyelidOuter", "lEyelidUpper",
                    "lEyelidUpperInner", "lEyelidUpperOuter",
                    "rEye", "rEyelidInner", "rEyelidLower", "rEyelidLowerInner",
                    "rEyelidLowerOuter", "rEyelidOuter", "rEyelidUpper",
                    "rEyelidUpperInner", "rEyelidUpperOuter",
                    "rh spear", "rh polearm", "rh weapon", "rh staff",
                    "quiver", "lh bow", "lh shield", "bow holster",
                    "lh weapon", "lh dagger", "back weapon", "hip weapon",
                    "character light group", "spot light group",
                    "Anus", "Vagina", "labia", "lLabia", "rLabia",
                    "PenisBase", "PenisMid", "PenisTip", "Scrotum"
                },
                WhiteList = new List<string>
                {
                    "hip", "abdomenLower", "abdomenUpper", "chestLower", "chestUpper",
                    "neckLower", "neckUpper", "head",
                    "lCollar", "lShldrBend", "lShldrTwist", "lForearmBend", "lForearmTwist",
                    "lHand", "lCarpal1", "lCarpal2", "lCarpal3", "lCarpal4",
                    "lThumb1", "lThumb2", "lThumb3",
                    "lIndex1", "lIndex2", "lIndex3",
                    "lMid1", "lMid2", "lMid3",
                    "lRing1", "lRing2", "lRing3",
                    "lPinky1", "lPinky2", "lPinky3",
                    "rCollar", "rShldrBend", "rShldrTwist", "rForearmBend", "rForearmTwist",
                    "rHand", "rCarpal1", "rCarpal2", "rCarpal3", "rCarpal4",
                    "rThumb1", "rThumb2", "rThumb3",
                    "rIndex1", "rIndex2", "rIndex3",
                    "rMid1", "rMid2", "rMid3",
                    "rRing1", "rRing2", "rRing3",
                    "rPinky1", "rPinky2", "rPinky3",
                    "pelvis", "lThighBend", "lThighTwist", "lShin", "lFoot", "lMetatarsals", "lToe",
                    "rThighBend", "rThighTwist", "rShin", "rFoot", "rMetatarsals", "rToe"
                },
                GreyGroups = new List<BoneGroupEntry>
                {
                    new BoneGroupEntry
                    {
                        GroupName = "jaw",
                        Enabled = false,
                        Bones = new List<string> { "lowerJaw" }
                    },
                    new BoneGroupEntry
                    {
                        GroupName = "lips",
                        Enabled = false,
                        Bones = new List<string>
                        {
                            "LipBelow", "LipLowerMiddle", "lLipCorner", "lLipLowerInner",
                            "lLipLowerOuter", "lLipNasolabialCrease", "lLipUpperInner",
                            "lLipUpperOuter", "LipUpperMiddle", "rLipCorner",
                            "rLipLowerInner", "rLipLowerOuter", "rLipNasolabialCrease",
                            "rLipUpperInner", "rLipUpperOuter"
                        }
                    },
                    new BoneGroupEntry
                    {
                        GroupName = "tongue",
                        Enabled = false,
                        Bones = new List<string> { "tongue01", "tongue02", "tongue03", "tongue04", "tongueBase" }
                    },
                    new BoneGroupEntry
                    {
                        GroupName = "scene_lights",
                        Enabled = false,
                        Bones = new List<string> { "Point Light (5)", "Point Light (1)" }
                    },
                    new BoneGroupEntry
                    {
                        GroupName = "intimate_accent",
                        Enabled = false,
                        Bones = new List<string> { "Clitoris" }
                    },
                    new BoneGroupEntry
                    {
                        GroupName = "breasts",
                        Enabled = false,
                        Bones = new List<string> { "lPectoral", "rPectoral" }
                    },
                    new BoneGroupEntry
                    {
                        GroupName = "toes",
                        Enabled = true,
                        Bones = new List<string>
                        {
                            "lBigToe", "lBigToe_2", "lMidToe1", "lMidToe1_2", "lMidToe2", "lMidToe2_2",
                            "lMidToe3", "lMidToe3_2", "lMidToe4", "lMidToe4_2", "lSmallToe1",
                            "lSmallToe1_2", "lSmallToe2", "lSmallToe2_2", "lSmallToe3", "lSmallToe3_2",
                            "lSmallToe4", "lSmallToe4_2",
                            "rBigToe", "rBigToe_2", "rMidToe1", "rMidToe1_2", "rMidToe2", "rMidToe2_2",
                            "rMidToe3", "rMidToe3_2", "rMidToe4", "rMidToe4_2", "rSmallToe1",
                            "rSmallToe1_2", "rSmallToe2", "rSmallToe2_2", "rSmallToe3", "rSmallToe3_2",
                            "rSmallToe4", "rSmallToe4_2"
                        }
                    }
                }
            };
        }
    }
}
