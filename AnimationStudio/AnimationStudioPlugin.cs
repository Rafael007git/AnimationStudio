using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using static System.Runtime.CompilerServices.RuntimeHelpers;

namespace AnimationStudio
{
    [BepInPlugin("com.lorifel007.animationstudio", "Animation Studio", "0.1.1")]
    [BepInDependency("com.lorifel007.furnitureanimations", BepInDependency.DependencyFlags.HardDependency)]
    public class AnimationStudioPlugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static StudioSession Session;
        private StudioWindowUI _windowUI;
        private bool _wasFreePoseOpen = false;

        private void Awake()
        {
            Log = Logger;
            Session = new StudioSession();
            _windowUI = new StudioWindowUI(Session);
            Log.LogInfo("[AnimationStudio] Загружен. Нажмите F8 в Free Pose для открытия студии.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
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
            if (_windowUI.IsVisible)
            {
                _windowUI.Draw();
            }
        }
    }
}
