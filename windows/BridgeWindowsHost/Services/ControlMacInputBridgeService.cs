using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using BridgeWindowsHost.Models;

namespace BridgeWindowsHost.Services;

public sealed class ControlMacInputBridgeService
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;

    private const int WmQuit = 0x0012;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonUp = 0x0208;
    private const int WmMouseWheel = 0x020A;
    private const int WmMouseHWheel = 0x020E;

    private const int HcAction = 0;
    private const uint LlmhfInjected = 0x00000001;
    private const uint LlkhfUp = 0x00000080;
    private const int WheelDelta = 120;

    private const ushort VkEscape = 0x1B;
    private const ushort VkLControl = 0xA2;
    private const ushort VkRControl = 0xA3;
    private const ushort VkLMenu = 0xA4;
    private const ushort VkRMenu = 0xA5;
    private const ushort VkLWin = 0x5B;
    private const ushort VkRWin = 0x5C;

    private const int SystemMetricSmXVirtualScreen = 76;
    private const int SystemMetricSmCxVirtualScreen = 78;

    private readonly BridgeEventHub _eventHub;
    private readonly ILogger<ControlMacInputBridgeService> _logger;
    private readonly object _gate = new();
    private readonly Channel<BridgeOutboundMessage> _outboundMessages = Channel.CreateUnbounded<BridgeOutboundMessage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly HashSet<ushort> _pressedKeys = [];
    private readonly LowLevelKeyboardProc _keyboardHookCallback;
    private readonly LowLevelMouseProc _mouseHookCallback;
    private readonly ManualResetEventSlim _hookReady = new(false);

    private Thread? _hookThread;
    private uint _hookThreadId;
    private IntPtr _keyboardHook = IntPtr.Zero;
    private IntPtr _mouseHook = IntPtr.Zero;
    private Task? _dispatchTask;
    private CancellationTokenSource? _dispatchCts;

    private bool _enabled;
    private ControlMacBridgePhase _phase = ControlMacBridgePhase.Off;
    private NativePoint _anchorPoint;

    public ControlMacInputBridgeService(
        BridgeEventHub eventHub,
        ILogger<ControlMacInputBridgeService> logger)
    {
        _eventHub = eventHub;
        _logger = logger;
        _keyboardHookCallback = KeyboardHookProc;
        _mouseHookCallback = MouseHookProc;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _dispatchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _dispatchTask = Task.Run(() => DispatchLoopAsync(_dispatchCts.Token), _dispatchCts.Token);

        // Run the low-level hooks on their own STA thread so the main ASP.NET request loop stays responsive.
        _hookThread = new Thread(HookThreadMain)
        {
            IsBackground = true,
            Name = "ControlMacInputBridgeHooks"
        };

        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();
        _hookReady.Wait(cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _enabled = false;
            ExitActiveSessionUnsafe(returnToWindows: false);
            _phase = ControlMacBridgePhase.Off;
        }

        QueueStateSnapshot();

        if (_hookThreadId != 0)
        {
            PostThreadMessage(_hookThreadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
        }

        if (_dispatchCts is not null)
        {
            _dispatchCts.Cancel();
        }

        if (_dispatchTask is not null)
        {
            try
            {
                await _dispatchTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Normal during shutdown.
            }
        }
    }

    public ControlMacFromWindowsStateDto GetState()
    {
        lock (_gate)
        {
            return BuildStateUnsafe();
        }
    }

    public ControlMacFromWindowsStateDto SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            _enabled = enabled;

            if (!enabled)
            {
                ExitActiveSessionUnsafe(returnToWindows: true);
                _phase = ControlMacBridgePhase.Off;
            }
            else if (_phase == ControlMacBridgePhase.Off)
            {
                _phase = ControlMacBridgePhase.Armed;
            }

            var state = BuildStateUnsafe();
            QueueStateSnapshotUnsafe(state);
            return state;
        }
    }

    private async Task DispatchLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _outboundMessages.Reader.ReadAllAsync(cancellationToken))
            {
                await _eventHub.BroadcastAsync(message.EventType, message.Payload, cancellationToken);

                if (!_eventHub.HasConnections && GetPhase() == ControlMacBridgePhase.Active)
                {
                    lock (_gate)
                    {
                        ExitActiveSessionUnsafe(returnToWindows: true);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private void HookThreadMain()
    {
        _hookThreadId = GetCurrentThreadId();

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        var moduleHandle = GetModuleHandle(module.ModuleName);

        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseHookCallback, moduleHandle, 0);
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardHookCallback, moduleHandle, 0);

        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
        {
            _logger.LogWarning("Control Mac input hooks failed to install.");
            _hookReady.Set();
            return;
        }

        _hookReady.Set();

        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    private IntPtr MouseHookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < HcAction)
        {
            return CallNextHookEx(_mouseHook, code, wParam, lParam);
        }

        var message = unchecked((int)wParam.ToInt64());
        var mouseInfo = Marshal.PtrToStructure<MsllHookStruct>(lParam);

        lock (_gate)
        {
            if (!_enabled)
            {
                return CallNextHookEx(_mouseHook, code, wParam, lParam);
            }

            if ((mouseInfo.Flags & LlmhfInjected) != 0)
            {
                return _phase == ControlMacBridgePhase.Active ? (IntPtr)1 : CallNextHookEx(_mouseHook, code, wParam, lParam);
            }

            if (_phase == ControlMacBridgePhase.Armed)
            {
                if (message == WmMouseMove && IsAtRightEdge(mouseInfo.Point) && _eventHub.HasConnections)
                {
                    ActivateSessionUnsafe(mouseInfo.Point);
                    return (IntPtr)1;
                }

                return CallNextHookEx(_mouseHook, code, wParam, lParam);
            }

            if (_phase != ControlMacBridgePhase.Active)
            {
                return CallNextHookEx(_mouseHook, code, wParam, lParam);
            }

            if (!_eventHub.HasConnections)
            {
                ExitActiveSessionUnsafe(returnToWindows: true);
                return CallNextHookEx(_mouseHook, code, wParam, lParam);
            }

            return HandleActiveMouseUnsafe(message, mouseInfo);
        }
    }

    private IntPtr KeyboardHookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < HcAction)
        {
            return CallNextHookEx(_keyboardHook, code, wParam, lParam);
        }

        var keyboardInfo = Marshal.PtrToStructure<KbdllHookStruct>(lParam);
        var virtualKey = unchecked((ushort)keyboardInfo.VkCode);
        var isDown = (keyboardInfo.Flags & LlkhfUp) == 0;

        lock (_gate)
        {
            if (_phase != ControlMacBridgePhase.Active)
            {
                UpdatePressedKeyStateUnsafe(virtualKey, isDown);
                return CallNextHookEx(_keyboardHook, code, wParam, lParam);
            }

            if (isDown && IsEscapeHotkeyUnsafe(virtualKey))
            {
                ExitActiveSessionUnsafe(returnToWindows: true);
                return (IntPtr)1;
            }

            UpdatePressedKeyStateUnsafe(virtualKey, isDown);
            QueueInputUnsafe(new InputBridgeEventDto
            {
                Kind = InputBridgeEventKind.Key,
                IsDown = isDown,
                WindowsVirtualKey = virtualKey
            });

            return (IntPtr)1;
        }
    }

    private IntPtr HandleActiveMouseUnsafe(int message, MsllHookStruct mouseInfo)
    {
        switch (message)
        {
            case WmMouseMove:
            {
                var deltaX = mouseInfo.Point.X - _anchorPoint.X;
                var deltaY = _anchorPoint.Y - mouseInfo.Point.Y;

                if (deltaX != 0 || deltaY != 0)
                {
                    QueueInputUnsafe(new InputBridgeEventDto
                    {
                        Kind = InputBridgeEventKind.MouseMove,
                        DeltaX = deltaX,
                        DeltaY = deltaY
                    });
                }

                if (mouseInfo.Point.X != _anchorPoint.X || mouseInfo.Point.Y != _anchorPoint.Y)
                {
                    SetCursorPos(_anchorPoint.X, _anchorPoint.Y);
                }

                return (IntPtr)1;
            }

            case WmLButtonDown:
                QueueMouseButtonUnsafe(InputBridgeMouseButton.Left, true);
                return (IntPtr)1;

            case WmLButtonUp:
                QueueMouseButtonUnsafe(InputBridgeMouseButton.Left, false);
                return (IntPtr)1;

            case WmRButtonDown:
                QueueMouseButtonUnsafe(InputBridgeMouseButton.Right, true);
                return (IntPtr)1;

            case WmRButtonUp:
                QueueMouseButtonUnsafe(InputBridgeMouseButton.Right, false);
                return (IntPtr)1;

            case WmMButtonDown:
                QueueMouseButtonUnsafe(InputBridgeMouseButton.Middle, true);
                return (IntPtr)1;

            case WmMButtonUp:
                QueueMouseButtonUnsafe(InputBridgeMouseButton.Middle, false);
                return (IntPtr)1;

            case WmMouseWheel:
            {
                var wheelDelta = GetSignedHighWord(mouseInfo.MouseData);
                QueueInputUnsafe(new InputBridgeEventDto
                {
                    Kind = InputBridgeEventKind.Scroll,
                    ScrollY = wheelDelta / WheelDelta
                });
                return (IntPtr)1;
            }

            case WmMouseHWheel:
            {
                var wheelDelta = GetSignedHighWord(mouseInfo.MouseData);
                QueueInputUnsafe(new InputBridgeEventDto
                {
                    Kind = InputBridgeEventKind.Scroll,
                    ScrollX = wheelDelta / WheelDelta
                });
                return (IntPtr)1;
            }

            default:
                return (IntPtr)1;
        }
    }

    private void QueueMouseButtonUnsafe(InputBridgeMouseButton button, bool isDown)
    {
        QueueInputUnsafe(new InputBridgeEventDto
        {
            Kind = InputBridgeEventKind.MouseButton,
            Button = button,
            IsDown = isDown
        });
    }

    private void ActivateSessionUnsafe(NativePoint cursorPoint)
    {
        _phase = ControlMacBridgePhase.Active;
        _anchorPoint = new NativePoint
        {
            X = GetVirtualRightEdge() - 2,
            Y = cursorPoint.Y
        };

        SetCursorPos(_anchorPoint.X, _anchorPoint.Y);
        QueueStateSnapshotUnsafe(BuildStateUnsafe());
        _logger.LogInformation("Control Mac from Windows activated.");
    }

    private void ExitActiveSessionUnsafe(bool returnToWindows)
    {
        if (_phase != ControlMacBridgePhase.Active)
        {
            return;
        }

        _phase = _enabled ? ControlMacBridgePhase.Armed : ControlMacBridgePhase.Off;
        _pressedKeys.Clear();

        if (returnToWindows)
        {
            SetCursorPos(Math.Max(GetVirtualLeftEdge() + 1, _anchorPoint.X - 24), _anchorPoint.Y);
        }

        QueueStateSnapshotUnsafe(BuildStateUnsafe());
        _logger.LogInformation("Control Mac from Windows returned to Windows.");
    }

    private void QueueInputUnsafe(InputBridgeEventDto inputEvent)
    {
        _outboundMessages.Writer.TryWrite(new BridgeOutboundMessage("control-mac-input", inputEvent));
    }

    private void QueueStateSnapshot()
    {
        lock (_gate)
        {
            QueueStateSnapshotUnsafe(BuildStateUnsafe());
        }
    }

    private void QueueStateSnapshotUnsafe(ControlMacFromWindowsStateDto state)
    {
        _outboundMessages.Writer.TryWrite(new BridgeOutboundMessage("control-mac-state", state));
    }

    private ControlMacFromWindowsStateDto BuildStateUnsafe()
    {
        return new ControlMacFromWindowsStateDto
        {
            Enabled = _enabled,
            Phase = _phase
        };
    }

    private ControlMacBridgePhase GetPhase()
    {
        lock (_gate)
        {
            return _phase;
        }
    }

    private void UpdatePressedKeyStateUnsafe(ushort virtualKey, bool isDown)
    {
        if (isDown)
        {
            _pressedKeys.Add(virtualKey);
        }
        else
        {
            _pressedKeys.Remove(virtualKey);
        }
    }

    private bool IsEscapeHotkeyUnsafe(ushort virtualKey)
    {
        return virtualKey == VkEscape
            && IsKeyDownUnsafe(VkLControl, VkRControl)
            && IsKeyDownUnsafe(VkLMenu, VkRMenu)
            && IsKeyDownUnsafe(VkLWin, VkRWin);
    }

    private bool IsKeyDownUnsafe(params ushort[] virtualKeys)
    {
        return virtualKeys.Any(virtualKey => _pressedKeys.Contains(virtualKey));
    }

    private static int GetVirtualRightEdge()
    {
        return GetSystemMetrics(SystemMetricSmXVirtualScreen) + GetSystemMetrics(SystemMetricSmCxVirtualScreen);
    }

    private static int GetVirtualLeftEdge()
    {
        return GetSystemMetrics(SystemMetricSmXVirtualScreen);
    }

    private static bool IsAtRightEdge(NativePoint point)
    {
        return point.X >= GetVirtualRightEdge() - 1;
    }

    private static int GetSignedHighWord(uint value)
    {
        return unchecked((short)((value >> 16) & 0xFFFF));
    }

    private sealed record BridgeOutboundMessage(string EventType, object Payload);

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdllHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Point;
        public uint LPrivate;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool TranslateMessage([In] ref NativeMessage lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DispatchMessage([In] ref NativeMessage lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, int msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);
}
