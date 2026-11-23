using UnityEngine;
#if MYLTI_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Mylti
{
    public enum GameKey
    {
        Throttle, Reverse, SteerLeft, SteerRight, Brake, Reset,
        Cam1, Cam2, Cam3, Cam4, Cam5, Cam6, Cam7, Cam8, Cam9, CamAuto,
        ToggleHud, ToggleCreator, Pause, FreeUp, FreeDown, FreeFast
    }

    /// <summary>
    /// Thin input abstraction: Unity Input System when it is active (Unity 6 default),
    /// legacy Input Manager otherwise. Game hotkeys are suppressed while an IMGUI text field has focus.
    /// </summary>
    public static class InputReader
    {
        public static bool TextFieldFocused => GUIUtility.keyboardControl != 0;

        public static bool Held(GameKey k)
        {
#if MYLTI_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return false;
            switch (k)
            {
                case GameKey.Throttle: return kb.wKey.isPressed || kb.upArrowKey.isPressed;
                case GameKey.Reverse: return kb.sKey.isPressed || kb.downArrowKey.isPressed;
                case GameKey.SteerLeft: return kb.aKey.isPressed || kb.leftArrowKey.isPressed;
                case GameKey.SteerRight: return kb.dKey.isPressed || kb.rightArrowKey.isPressed;
                case GameKey.Brake: return kb.spaceKey.isPressed;
                case GameKey.FreeUp: return kb.eKey.isPressed;
                case GameKey.FreeDown: return kb.qKey.isPressed;
                case GameKey.FreeFast: return kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                default: return false;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            switch (k)
            {
                case GameKey.Throttle: return Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
                case GameKey.Reverse: return Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
                case GameKey.SteerLeft: return Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
                case GameKey.SteerRight: return Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
                case GameKey.Brake: return Input.GetKey(KeyCode.Space);
                case GameKey.FreeUp: return Input.GetKey(KeyCode.E);
                case GameKey.FreeDown: return Input.GetKey(KeyCode.Q);
                case GameKey.FreeFast: return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                default: return false;
            }
#else
            return false;
#endif
        }

        /// <summary>Single-press hotkeys (ignored while typing in a text field).</summary>
        public static bool Pressed(GameKey k)
        {
            if (TextFieldFocused) return false;
#if MYLTI_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return false;
            switch (k)
            {
                case GameKey.Reset: return kb.rKey.wasPressedThisFrame;
                case GameKey.Cam1: return kb.digit1Key.wasPressedThisFrame;
                case GameKey.Cam2: return kb.digit2Key.wasPressedThisFrame;
                case GameKey.Cam3: return kb.digit3Key.wasPressedThisFrame;
                case GameKey.Cam4: return kb.digit4Key.wasPressedThisFrame;
                case GameKey.Cam5: return kb.digit5Key.wasPressedThisFrame;
                case GameKey.Cam6: return kb.digit6Key.wasPressedThisFrame;
                case GameKey.Cam7: return kb.digit7Key.wasPressedThisFrame;
                case GameKey.Cam8: return kb.digit8Key.wasPressedThisFrame;
                case GameKey.Cam9: return kb.digit9Key.wasPressedThisFrame;
                case GameKey.CamAuto: return kb.digit0Key.wasPressedThisFrame;
                case GameKey.ToggleHud: return kb.hKey.wasPressedThisFrame;
                case GameKey.ToggleCreator: return kb.f1Key.wasPressedThisFrame;
                case GameKey.Pause: return kb.escapeKey.wasPressedThisFrame;
                default: return false;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            switch (k)
            {
                case GameKey.Reset: return Input.GetKeyDown(KeyCode.R);
                case GameKey.Cam1: return Input.GetKeyDown(KeyCode.Alpha1);
                case GameKey.Cam2: return Input.GetKeyDown(KeyCode.Alpha2);
                case GameKey.Cam3: return Input.GetKeyDown(KeyCode.Alpha3);
                case GameKey.Cam4: return Input.GetKeyDown(KeyCode.Alpha4);
                case GameKey.Cam5: return Input.GetKeyDown(KeyCode.Alpha5);
                case GameKey.Cam6: return Input.GetKeyDown(KeyCode.Alpha6);
                case GameKey.Cam7: return Input.GetKeyDown(KeyCode.Alpha7);
                case GameKey.Cam8: return Input.GetKeyDown(KeyCode.Alpha8);
                case GameKey.Cam9: return Input.GetKeyDown(KeyCode.Alpha9);
                case GameKey.CamAuto: return Input.GetKeyDown(KeyCode.Alpha0);
                case GameKey.ToggleHud: return Input.GetKeyDown(KeyCode.H);
                case GameKey.ToggleCreator: return Input.GetKeyDown(KeyCode.F1);
                case GameKey.Pause: return Input.GetKeyDown(KeyCode.Escape);
                default: return false;
            }
#else
            return false;
#endif
        }

        public static float Throttle() => TextFieldFocused ? 0f : (Held(GameKey.Throttle) ? 1f : 0f) - (Held(GameKey.Reverse) ? 1f : 0f);
        public static float Steer() => TextFieldFocused ? 0f : (Held(GameKey.SteerRight) ? 1f : 0f) - (Held(GameKey.SteerLeft) ? 1f : 0f);
        public static bool BrakeHeld() => !TextFieldFocused && Held(GameKey.Brake);

        public static Vector2 MouseDelta()
        {
#if MYLTI_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.1f : Vector2.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 2f;
#else
            return Vector2.zero;
#endif
        }

        public static bool RightMouseHeld()
        {
#if MYLTI_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(1);
#else
            return false;
#endif
        }

        public static float Scroll()
        {
#if MYLTI_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.mouseScrollDelta.y;
#else
            return 0f;
#endif
        }

        /// <summary>Free-camera WASD movement vector (x = strafe, y = forward).</summary>
        public static Vector2 FreeMove()
        {
            if (TextFieldFocused) return Vector2.zero;
            return new Vector2(Steer(), Throttle());
        }
    }
}
