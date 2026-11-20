using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using static System.Runtime.CompilerServices.RuntimeHelpers;

namespace AnimationStudio
{
    [BepInPlugin("com.lorifel007.animationstudio", "Animation Studio", "0.1.0")]
    [BepInDependency("com.lorifel007.furnitureanimations", BepInDependency.DependencyFlags.HardDependency)]
    public class AnimationStudioPlugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static StudioSession Session;
        private StudioWindowUI _windowUI;

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

            if (_windowUI.IsVisible)
            {
                Session.RefreshActiveCharacter();
            }
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
