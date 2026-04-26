using System.Runtime.InteropServices;
using BridgeWindowsHost.Models;

namespace BridgeWindowsHost.Services;

public sealed class InputInjectionService(ILogger<InputInjectionService> logger)
{
    private const int InputMouse = 0;
    private const int InputKeyboard = 1;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventMiddleDown = 0x0020;
    private const uint MouseEventMiddleUp = 0x0040;
    private const uint MouseEventWheel = 0x0800;
    private const uint MouseEventHWheel = 0x01000;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventExtendedKey = 0x0001;
    private const int WheelDelta = 120;

    private static readonly HashSet<ushort> ExtendedKeys =
    [
        0x21, // Page Up
        0x22, // Page Down
        0x23, // End
        0x24, // Home
        0x25, // Left
        0x26, // Up
        0x27, // Right
        0x28, // Down
        0x2D, // Insert
        0x2E, // Delete
        0x5B, // Left Windows
        0x5C, // Right Windows
        0xA3, // Right Control
        0xA5  // Right Alt
    ];

    private readonly ILogger<InputInjectionService> _logger = logger;
    private readonly object _gate = new();
    private readonly HashSet<ushort> _pressedKeys = [];
    private readonly HashSet<InputBridgeMouseButton> _pressedButtons = [];

    public void BeginSession()
    {
        lock (_gate)
        {
            ReleaseAllStateUnsafe();
        }
    }

    public void EndSession()
    {
        lock (_gate)
        {
            ReleaseAllStateUnsafe();
        }
    }

    public void Inject(InputBridgeEventDto inputEvent)
    {
        lock (_gate)
        {
            switch (inputEvent.Kind)
            {
                case InputBridgeEventKind.MouseMove:
                    InjectMouseMove(inputEvent);
                    break;

                case InputBridgeEventKind.MouseButton:
                    InjectMouseButton(inputEvent);
                    break;

                case InputBridgeEventKind.Scroll:
                    InjectScroll(inputEvent);
                    break;

                case InputBridgeEventKind.Key:
                    InjectKey(inputEvent);
                    break;
            }
        }
    }

    private void InjectMouseMove(InputBridgeEventDto inputEvent)
    {
        if (inputEvent.DeltaX == 0 && inputEvent.DeltaY == 0)
        {
            return;
        }

        SendMouseInput(inputEvent.DeltaX, -inputEvent.DeltaY, 0, MouseEventMove);
    }

    private void InjectMouseButton(InputBridgeEventDto inputEvent)
    {
        if (inputEvent.Button is null || inputEvent.IsDown is null)
        {
            return;
        }

        var flags = (inputEvent.Button, inputEvent.IsDown.Value) switch
        {
            (InputBridgeMouseButton.Left, true) => MouseEventLeftDown,
            (InputBridgeMouseButton.Left, false) => MouseEventLeftUp,
            (InputBridgeMouseButton.Right, true) => MouseEventRightDown,
            (InputBridgeMouseButton.Right, false) => MouseEventRightUp,
            (InputBridgeMouseButton.Middle, true) => MouseEventMiddleDown,
            (InputBridgeMouseButton.Middle, false) => MouseEventMiddleUp,
            _ => 0u
        };

        if (flags == 0)
        {
            return;
        }

        if (inputEvent.IsDown.Value)
        {
            _pressedButtons.Add(inputEvent.Button.Value);
        }
        else
        {
            _pressedButtons.Remove(inputEvent.Button.Value);
        }

        SendMouseInput(0, 0, 0, flags);
    }

    private void InjectScroll(InputBridgeEventDto inputEvent)
    {
        if (inputEvent.ScrollY != 0)
        {
            SendMouseInput(0, 0, unchecked((uint)(inputEvent.ScrollY * WheelDelta)), MouseEventWheel);
        }

        if (inputEvent.ScrollX != 0)
        {
            SendMouseInput(0, 0, unchecked((uint)(inputEvent.ScrollX * WheelDelta)), MouseEventHWheel);
        }
    }

    private void InjectKey(InputBridgeEventDto inputEvent)
    {
        if (inputEvent.WindowsVirtualKey is null || inputEvent.IsDown is null)
        {
            return;
        }

        var virtualKey = inputEvent.WindowsVirtualKey.Value;
        var flags = inputEvent.IsDown.Value ? 0u : KeyEventKeyUp;
        if (ExtendedKeys.Contains(virtualKey))
        {
            flags |= KeyEventExtendedKey;
        }

        if (inputEvent.IsDown.Value)
        {
            _pressedKeys.Add(virtualKey);
        }
        else
        {
            _pressedKeys.Remove(virtualKey);
        }

        SendKeyboardInput(virtualKey, flags);
    }

    private void ReleaseAllStateUnsafe()
    {
        // Releasing held keys and buttons avoids "stuck input" if the Mac exits the bridge mid-gesture.
        foreach (var virtualKey in _pressedKeys.ToList())
        {
            var flags = KeyEventKeyUp;
            if (ExtendedKeys.Contains(virtualKey))
            {
                flags |= KeyEventExtendedKey;
            }

            SendKeyboardInput(virtualKey, flags);
        }

        foreach (var button in _pressedButtons.ToList())
        {
            var flags = button switch
            {
                InputBridgeMouseButton.Left => MouseEventLeftUp,
                InputBridgeMouseButton.Right => MouseEventRightUp,
                InputBridgeMouseButton.Middle => MouseEventMiddleUp,
                _ => 0u
            };

            if (flags != 0)
            {
                SendMouseInput(0, 0, 0, flags);
            }
        }

        _pressedKeys.Clear();
        _pressedButtons.Clear();
    }

    private void SendMouseInput(int dx, int dy, uint mouseData, uint flags)
    {
        var input = new Input
        {
            Type = InputMouse,
            Union = new InputUnion
            {
                Mouse = new MouseInput
                {
                    Dx = dx,
                    Dy = dy,
                    MouseData = mouseData,
                    DwFlags = flags
                }
            }
        };

        SendSingleInput(input);
    }

    private void SendKeyboardInput(ushort virtualKey, uint flags)
    {
        var input = new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = 0,
                    DwFlags = flags
                }
            }
        };

        SendSingleInput(input);
    }

    private void SendSingleInput(Input input)
    {
        var inputs = new[] { input };
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != (uint)inputs.Length)
        {
            _logger.LogWarning("SendInput failed while forwarding Input Bridge events.");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, Input[] inputs, int sizeOfInputStructure);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public nuint DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint DwFlags;
        public uint Time;
        public nuint DwExtraInfo;
    }
}
