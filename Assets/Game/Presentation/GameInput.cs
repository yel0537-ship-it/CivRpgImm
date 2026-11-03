using UnityEngine;
#if ENABLE_INPUT_SYSTEM && IMMORTAL_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Immortal.Presentation
{
    /// <summary>
    /// Тонкая обёртка над вводом: пакет Input System, если он активен, иначе старый Input Manager.
    /// Остальной код игры не знает, какой из них работает.
    /// </summary>
    public static class GameInput
    {
        public enum Key { W, A, S, D, Up, Down, Left, Right, Space, Tab, M, I, C, F, R, E, Escape, Enter, F5, F9, F12, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9 }

#if ENABLE_INPUT_SYSTEM && IMMORTAL_INPUT_SYSTEM
        static UnityEngine.InputSystem.Key Map(Key k) => k switch
        {
            Key.W => UnityEngine.InputSystem.Key.W, Key.A => UnityEngine.InputSystem.Key.A, Key.S => UnityEngine.InputSystem.Key.S, Key.D => UnityEngine.InputSystem.Key.D,
            Key.Up => UnityEngine.InputSystem.Key.UpArrow, Key.Down => UnityEngine.InputSystem.Key.DownArrow,
            Key.Left => UnityEngine.InputSystem.Key.LeftArrow, Key.Right => UnityEngine.InputSystem.Key.RightArrow,
            Key.Space => UnityEngine.InputSystem.Key.Space, Key.Tab => UnityEngine.InputSystem.Key.Tab, Key.M => UnityEngine.InputSystem.Key.M,
            Key.I => UnityEngine.InputSystem.Key.I, Key.C => UnityEngine.InputSystem.Key.C, Key.F => UnityEngine.InputSystem.Key.F,
            Key.R => UnityEngine.InputSystem.Key.R, Key.E => UnityEngine.InputSystem.Key.E, Key.Escape => UnityEngine.InputSystem.Key.Escape,
            Key.Enter => UnityEngine.InputSystem.Key.Enter, Key.F5 => UnityEngine.InputSystem.Key.F5, Key.F9 => UnityEngine.InputSystem.Key.F9,
            Key.F12 => UnityEngine.InputSystem.Key.F12,
            Key.Alpha1 => UnityEngine.InputSystem.Key.Digit1, Key.Alpha2 => UnityEngine.InputSystem.Key.Digit2, Key.Alpha3 => UnityEngine.InputSystem.Key.Digit3,
            Key.Alpha4 => UnityEngine.InputSystem.Key.Digit4, Key.Alpha5 => UnityEngine.InputSystem.Key.Digit5, Key.Alpha6 => UnityEngine.InputSystem.Key.Digit6,
            Key.Alpha7 => UnityEngine.InputSystem.Key.Digit7, Key.Alpha8 => UnityEngine.InputSystem.Key.Digit8, _ => UnityEngine.InputSystem.Key.Digit9
        };

        public static bool Held(Key k) => Keyboard.current != null && Keyboard.current[Map(k)].isPressed;
        public static bool Down(Key k) => Keyboard.current != null && Keyboard.current[Map(k)].wasPressedThisFrame;
        public static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public static bool LeftHeld => Mouse.current != null && Mouse.current.leftButton.isPressed;
        public static bool LeftDown => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        public static bool RightDown => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        public static float Scroll => Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#else
        static KeyCode Map(Key k) => k switch
        {
            Key.W => KeyCode.W, Key.A => KeyCode.A, Key.S => KeyCode.S, Key.D => KeyCode.D,
            Key.Up => KeyCode.UpArrow, Key.Down => KeyCode.DownArrow, Key.Left => KeyCode.LeftArrow, Key.Right => KeyCode.RightArrow,
            Key.Space => KeyCode.Space, Key.Tab => KeyCode.Tab, Key.M => KeyCode.M, Key.I => KeyCode.I, Key.C => KeyCode.C,
            Key.F => KeyCode.F, Key.R => KeyCode.R, Key.E => KeyCode.E, Key.Escape => KeyCode.Escape, Key.Enter => KeyCode.Return,
            Key.F5 => KeyCode.F5, Key.F9 => KeyCode.F9, Key.F12 => KeyCode.F12,
            Key.Alpha1 => KeyCode.Alpha1, Key.Alpha2 => KeyCode.Alpha2, Key.Alpha3 => KeyCode.Alpha3, Key.Alpha4 => KeyCode.Alpha4,
            Key.Alpha5 => KeyCode.Alpha5, Key.Alpha6 => KeyCode.Alpha6, Key.Alpha7 => KeyCode.Alpha7, Key.Alpha8 => KeyCode.Alpha8, _ => KeyCode.Alpha9
        };

        public static bool Held(Key k) => Input.GetKey(Map(k));
        public static bool Down(Key k) => Input.GetKeyDown(Map(k));
        public static Vector2 MousePosition => Input.mousePosition;
        public static bool LeftHeld => Input.GetMouseButton(0);
        public static bool LeftDown => Input.GetMouseButtonDown(0);
        public static bool RightDown => Input.GetMouseButtonDown(1);
        public static float Scroll => Input.mouseScrollDelta.y;
#endif

        public static Vector2 MoveAxis()
        {
            float x = 0, y = 0;
            if (Held(Key.W) || Held(Key.Up)) y += 1;
            if (Held(Key.S) || Held(Key.Down)) y -= 1;
            if (Held(Key.D) || Held(Key.Right)) x += 1;
            if (Held(Key.A) || Held(Key.Left)) x -= 1;
            var v = new Vector2(x, y);
            return v.sqrMagnitude > 1 ? v.normalized : v;
        }

        public static int DigitDown()
        {
            for (int i = 0; i < 9; i++) if (Down(Key.Alpha1 + i)) return i + 1;
            return 0;
        }
    }
}
