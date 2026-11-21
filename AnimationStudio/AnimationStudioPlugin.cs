using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using static System.Runtime.CompilerServices.RuntimeHelpers;

namespace AnimationStudio
{
    [BepInPlugin("com.lorifel007.animationstudio", "Animation Studio", "0.1.3")]
    [BepInDependency("com.lorifel007.furnitureanimations", BepInDependency.DependencyFlags.HardDependency)]
    public class AnimationStudioPlugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static StudioSession Session;
        public static BepInEx.Configuration.ConfigEntry<KeyCode> ToggleWindowKey;
        private StudioWindowUI _windowUI;
        private bool _wasFreePoseOpen = false;

        private void Awake()
        {
            Log = Logger;
            Session = new StudioSession();

            ToggleWindowKey = Config.Bind(
                "General",
                "ToggleStudioKey",
                KeyCode.F7,
                "Hotkey to toggle the Animation Studio window."
            );

            _windowUI = new StudioWindowUI(Session);
            Log.LogInfo("[AnimationStudio] Загружен. Нажмите F8 в Free Pose для открытия студии.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(ToggleWindowKey.Value))
            {
                _windowUI.ToggleVisible();
            }

            bool isFreePoseOpen = Global.code != null &&
                                  Global.code.uiFreePose != null &&
                                  Global.code.uiFreePose.gameObject.activeInHierarchy;

            if (_wasFreePoseOpen && !isFreePoseOpen)
            {
                Session.ReleaseCharacter();
            }
            _wasFreePoseOpen = isFreePoseOpen;

            if (_windowUI.IsVisible)
            {
                Session.RefreshActiveCharacter();
            }

            Session.UpdateAnchorAndGizmo(_windowUI.IsVisible);
        }

        private void OnDestroy()
        {
            Session?.UpdateAnchorAndGizmo(false);
        }

        private void OnGUI()
        {
            if (_windowUI == null || !_windowUI.IsVisible) return;

            _windowUI.Draw();

            if (_windowUI.IsMouseOverUI && Event.current != null)
            {
                EventType t = Event.current.type;
                if (t == EventType.MouseDown || t == EventType.MouseUp || t == EventType.MouseDrag || t == EventType.ScrollWheel)
                {
                    Event.current.Use();
                }
            }
        }

    }
}
