using System.Diagnostics;
using System.Drawing;

namespace BridgeWindowsDesktop;

internal sealed class MainForm : Form
{
    private readonly BridgeHostConfigService _configService = new();
    private readonly BridgeDesktopApiClient _apiClient = new();
    private readonly BridgeHostProcessManager _processManager;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 2000 };

    private readonly Label _serverStatusValue = CreateValueLabel();
    private readonly Label _addressValue = CreateWrappedValueLabel();
    private readonly Label _portValue = CreateValueLabel();
    private readonly Label _secretStatusValue = CreateWrappedValueLabel();
    private readonly Label _macClientStatusValue = CreateValueLabel();
    private readonly Label _sharedFolderValue = CreateWrappedValueLabel();
    private readonly Label _controlMacPhaseValue = CreateValueLabel();
    private readonly Label _screenPositionHelpLabel = new()
    {
        AutoSize = true,
        ForeColor = Color.DimGray,
        MaximumSize = new Size(560, 0),
        Margin = new Padding(0, 0, 0, 8),
        Text = "Choose where your MacBook is physically placed relative to your Windows monitor."
    };
    private readonly Label _layoutPreviewLabel = new()
    {
        AutoSize = false,
        Width = 260,
        Height = 82,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.WhiteSmoke,
        Font = new Font("Consolas", 11, FontStyle.Bold),
        Padding = new Padding(10),
        Margin = new Padding(0, 6, 0, 0)
    };
    private readonly Label _footerValue = new()
    {
        AutoSize = true,
        ForeColor = Color.DimGray,
        MaximumSize = new Size(700, 0),
        Margin = new Padding(0, 12, 0, 0)
    };

    private readonly CheckBox _controlMacToggle = new()
    {
        AutoSize = true,
        Text = "Control Mac from Windows",
        Margin = new Padding(0, 0, 16, 0)
    };

    private readonly ComboBox _screenPositionCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 280
    };

    private readonly Button _openSharedFolderButton = new()
    {
        AutoSize = true,
        Text = "Open Shared Folder"
    };

    private readonly Button _startStopButton = new()
    {
        AutoSize = true,
        Text = "Start Bridge"
    };

    private bool _isRefreshing;
    private bool _isUpdatingToggle;
    private bool _isUpdatingScreenPosition;
    private bool _isShuttingDown;
    private DesktopLocalSettings _desktopSettings;

    public MainForm()
    {
        _processManager = new BridgeHostProcessManager(_configService);
        _desktopSettings = LoadDesktopSettingsSafe();

        Text = "BridgeWindowsHost Control";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 470);
        ClientSize = new Size(820, 520);

        Controls.Add(BuildLayout());

        Shown += async (_, _) => await HandleShownAsync();
        FormClosing += HandleFormClosing;
        _refreshTimer.Tick += async (_, _) => await RefreshRuntimeAsync();
        _controlMacToggle.CheckedChanged += async (_, _) => await HandleControlMacToggleChangedAsync();
        _screenPositionCombo.SelectedIndexChanged += async (_, _) => await HandleScreenPositionChangedAsync();
        _openSharedFolderButton.Click += (_, _) => HandleOpenSharedFolderClicked();
        _startStopButton.Click += async (_, _) => await HandleStartStopClickedAsync();

        _screenPositionCombo.Items.AddRange(Enum.GetValues<MacScreenPosition>().Cast<object>().ToArray());
        _screenPositionCombo.Format += (_, eventArgs) =>
        {
            if (eventArgs.ListItem is MacScreenPosition position)
            {
                eventArgs.Value = GetScreenPositionLabel(position);
            }
        };

        ApplyDesktopSettingsSnapshot();
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(18),
            ColumnCount = 2
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            Text = "BridgeWindowsHost Desktop Control",
            Margin = new Padding(0, 0, 0, 6)
        };

        var subtitleLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            MaximumSize = new Size(720, 0),
            Text = "Manage the local bridge service, inspect its network status, and toggle the Windows-to-Mac input bridge without relying on a console window.",
            Margin = new Padding(0, 0, 0, 14)
        };

        AddFullWidthRow(root, titleLabel);
        AddFullWidthRow(root, subtitleLabel);
        AddLabeledRow(root, "Server Status", _serverStatusValue);
        AddLabeledRow(root, "Current IP Addresses", _addressValue);
        AddLabeledRow(root, "Port", _portValue);
        AddLabeledRow(root, "Shared Secret Status", _secretStatusValue);
        AddLabeledRow(root, "Connected Mac Clients", _macClientStatusValue);

        var screenPositionPanel = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Margin = new Padding(0)
        };
        screenPositionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        screenPositionPanel.Controls.Add(_screenPositionHelpLabel, 0, 0);
        screenPositionPanel.Controls.Add(_screenPositionCombo, 0, 1);
        screenPositionPanel.Controls.Add(_layoutPreviewLabel, 0, 2);
        AddLabeledRow(root, "Mac Screen Position", screenPositionPanel);

        var controlPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0)
        };
        controlPanel.Controls.Add(_controlMacToggle);
        controlPanel.Controls.Add(_controlMacPhaseValue);
        AddLabeledRow(root, "Input Bridge", controlPanel);

        AddLabeledRow(root, "Shared Folder", _sharedFolderValue);

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0)
        };
        buttonPanel.Controls.Add(_startStopButton);
        buttonPanel.Controls.Add(_openSharedFolderButton);
        AddLabeledRow(root, "Actions", buttonPanel);

        AddFullWidthRow(root, _footerValue);
        return root;
    }

    private async Task HandleShownAsync()
    {
        try
        {
            SetFooter("Starting BridgeWindowsHost...", Color.DimGray);
            await StartManagedHostIfNeededAsync();
        }
        catch (Exception exception)
        {
            SetFooter(exception.Message, Color.Firebrick);
        }
        finally
        {
            _refreshTimer.Start();
            await RefreshRuntimeAsync();
        }
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        _refreshTimer.Stop();

        try
        {
            var options = TryLoadConfiguration();
            if (options is not null && _processManager.IsManagedHostRunning)
            {
                TryDisableControlMacBeforeStopAsync(options).GetAwaiter().GetResult();
            }
        }
        catch
        {
            // Best effort during shutdown.
        }

        _processManager.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private async Task HandleStartStopClickedAsync()
    {
        _startStopButton.Enabled = false;

        try
        {
            var options = LoadConfiguration();
            var isHealthy = await _apiClient.IsHealthyAsync(options, CancellationToken.None);

            if (_processManager.IsManagedHostRunning)
            {
                await TryDisableControlMacBeforeStopAsync(options);
                await _processManager.StopAsync(CancellationToken.None);
                SetFooter("BridgeWindowsHost stopped.", Color.DimGray);
            }
            else if (isHealthy)
            {
                SetFooter("Another BridgeWindowsHost instance is already running on this port. Use the process that started it to stop it.", Color.DarkOrange);
            }
            else
            {
                await _processManager.StartAsync(CancellationToken.None);
                SetFooter("BridgeWindowsHost starting...", Color.DimGray);
            }
        }
        catch (Exception exception)
        {
            SetFooter(exception.Message, Color.Firebrick);
        }
        finally
        {
            _startStopButton.Enabled = true;
            await RefreshRuntimeAsync();
        }
    }

    private void HandleOpenSharedFolderClicked()
    {
        try
        {
            var options = LoadConfiguration();
            var folderPath = _configService.GetExpandedStorageRoot(options);
            Directory.CreateDirectory(folderPath);

            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });

            SetFooter($"Opened shared folder: {folderPath}", Color.DimGray);
        }
        catch (Exception exception)
        {
            SetFooter(exception.Message, Color.Firebrick);
        }
    }

    private async Task HandleControlMacToggleChangedAsync()
    {
        if (_isUpdatingToggle || _isRefreshing)
        {
            return;
        }

        try
        {
            var options = LoadConfiguration();
            var state = await _apiClient.SetControlMacFromWindowsAsync(
                options,
                _controlMacToggle.Checked,
                GetSelectedScreenPosition(),
                CancellationToken.None);

            var message = _controlMacToggle.Checked
                ? $"Control Mac from Windows enabled on the {state.ActivationEdge.ToLowerInvariant()} edge."
                : "Control Mac from Windows disabled.";

            SetFooter(message, Color.DimGray);
        }
        catch (Exception exception)
        {
            SetFooter(exception.Message, Color.Firebrick);
        }
        finally
        {
            await RefreshRuntimeAsync();
        }
    }

    private async Task HandleScreenPositionChangedAsync()
    {
        if (_isUpdatingScreenPosition)
        {
            return;
        }

        var selectedPosition = GetSelectedScreenPosition();
        _desktopSettings = _desktopSettings with { MacScreenPosition = selectedPosition };
        _configService.SaveDesktopSettings(_desktopSettings);
        UpdateLayoutPreview(selectedPosition);

        try
        {
            var options = LoadConfiguration();
            if (!await _apiClient.IsHealthyAsync(options, CancellationToken.None))
            {
                SetFooter($"Saved Mac screen position: {GetScreenPositionLabel(selectedPosition)}. It will apply after the bridge starts.", Color.DimGray);
                return;
            }

            var state = await _apiClient.SetControlMacFromWindowsAsync(
                options,
                _controlMacToggle.Checked,
                selectedPosition,
                CancellationToken.None);

            _controlMacPhaseValue.Text = $"State: {state.Phase} | Edge: {state.ActivationEdge}";
            SetFooter($"Saved Mac screen position: {GetScreenPositionLabel(selectedPosition)}. Windows will activate from the {state.ActivationEdge.ToLowerInvariant()} edge.", Color.DimGray);
        }
        catch (Exception exception)
        {
            SetFooter(exception.Message, Color.Firebrick);
        }
        finally
        {
            await RefreshRuntimeAsync();
        }
    }

    private async Task RefreshRuntimeAsync()
    {
        if (_isRefreshing || _isShuttingDown)
        {
            return;
        }

        _isRefreshing = true;

        try
        {
            var options = LoadConfiguration();
            ApplyConfigurationSnapshot(options);

            var isHealthy = await _apiClient.IsHealthyAsync(options, CancellationToken.None);
            var isManagedHostRunning = _processManager.IsManagedHostRunning;

            if (!isHealthy)
            {
                ApplyStoppedSnapshot(isManagedHostRunning);
                return;
            }

            var runtime = await _apiClient.GetRuntimeAsync(options, CancellationToken.None);
            runtime = await EnsureScreenPositionAppliedAsync(options, runtime);
            ApplyRunningSnapshot(runtime, isManagedHostRunning);
        }
        catch (Exception exception)
        {
            ApplyErrorSnapshot(exception.Message);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void ApplyConfigurationSnapshot(DesktopBridgeOptions options)
    {
        _portValue.Text = options.Port.ToString();
        _secretStatusValue.Text = _configService.GetMaskedSecretStatus(options);
        _sharedFolderValue.Text = _configService.GetExpandedStorageRoot(options);
    }

    private void ApplyRunningSnapshot(DesktopBridgeRuntimeSnapshot runtime, bool isManagedHostRunning)
    {
        _serverStatusValue.Text = isManagedHostRunning ? "Running" : "Running (external)";
        _serverStatusValue.ForeColor = isManagedHostRunning ? Color.ForestGreen : Color.DarkOrange;
        _addressValue.Text = runtime.BridgeState.LocalAddresses.Count == 0
            ? "No IPv4 addresses detected."
            : string.Join(Environment.NewLine, runtime.BridgeState.LocalAddresses);
        _portValue.Text = runtime.BridgeState.Port.ToString();
        _sharedFolderValue.Text = runtime.BridgeState.StorageRoot;
        _macClientStatusValue.Text = runtime.BridgeState.ConnectedMacClients switch
        {
            <= 0 => "No Mac client connected",
            1 => "1 Mac client connected",
            _ => $"{runtime.BridgeState.ConnectedMacClients} Mac clients connected"
        };

        _isUpdatingToggle = true;
        _controlMacToggle.Checked = runtime.ControlState.Enabled;
        _isUpdatingToggle = false;
        _controlMacToggle.Enabled = true;
        _controlMacPhaseValue.Text = $"State: {runtime.ControlState.Phase} | Edge: {runtime.ControlState.ActivationEdge}";
        _openSharedFolderButton.Enabled = true;

        if (isManagedHostRunning)
        {
            _startStopButton.Enabled = true;
            _startStopButton.Text = "Stop Bridge";
            SetFooter("BridgeWindowsHost is running normally.", Color.DimGray);
        }
        else
        {
            _startStopButton.Enabled = false;
            _startStopButton.Text = "Bridge Running Elsewhere";
            SetFooter("BridgeWindowsHost is already running outside this window. The UI can monitor it, but stop/start stays with the process that launched it.", Color.DarkOrange);
        }
    }

    private void ApplyStoppedSnapshot(bool isManagedHostRunning)
    {
        if (isManagedHostRunning)
        {
            _serverStatusValue.Text = "Starting...";
            _serverStatusValue.ForeColor = Color.DarkOrange;
            _startStopButton.Enabled = true;
            _startStopButton.Text = "Stop Bridge";
            SetFooter(_processManager.LastProcessMessage ?? "BridgeWindowsHost is starting up.", Color.DimGray);
        }
        else
        {
            _serverStatusValue.Text = "Stopped";
            _serverStatusValue.ForeColor = Color.Firebrick;
            _startStopButton.Enabled = true;
            _startStopButton.Text = "Start Bridge";
            SetFooter(_processManager.LastProcessMessage ?? "BridgeWindowsHost is not running.", Color.DimGray);
        }

        _addressValue.Text = "Not available while the bridge is stopped.";
        _macClientStatusValue.Text = "No Mac client connected";
        _openSharedFolderButton.Enabled = true;

        _isUpdatingToggle = true;
        _controlMacToggle.Checked = false;
        _isUpdatingToggle = false;
        _controlMacToggle.Enabled = false;
        _controlMacPhaseValue.Text = $"State: Off | Edge: {GetActivationEdgeLabel(GetSelectedScreenPosition())}";
    }

    private void ApplyErrorSnapshot(string message)
    {
        _serverStatusValue.Text = "Unavailable";
        _serverStatusValue.ForeColor = Color.Firebrick;
        _addressValue.Text = "Unable to query the bridge.";
        _macClientStatusValue.Text = "Unknown";
        _startStopButton.Text = _processManager.IsManagedHostRunning ? "Stop Bridge" : "Start Bridge";
        _controlMacToggle.Enabled = false;
        SetFooter(message, Color.Firebrick);
    }

    private async Task StartManagedHostIfNeededAsync()
    {
        var options = LoadConfiguration();
        if (await _apiClient.IsHealthyAsync(options, CancellationToken.None))
        {
            SetFooter("BridgeWindowsHost is already running outside this window.", Color.DarkOrange);
            return;
        }

        await _processManager.StartAsync(CancellationToken.None);
    }

    private async Task TryDisableControlMacBeforeStopAsync(DesktopBridgeOptions options)
    {
        try
        {
            if (await _apiClient.IsHealthyAsync(options, CancellationToken.None))
            {
                await _apiClient.SetControlMacFromWindowsAsync(
                    options,
                    false,
                    GetSelectedScreenPosition(),
                    CancellationToken.None);
            }
        }
        catch
        {
            // If the bridge is already unresponsive, shutting down the host process is still the safest fallback.
        }
    }

    private async Task<DesktopBridgeRuntimeSnapshot> EnsureScreenPositionAppliedAsync(
        DesktopBridgeOptions options,
        DesktopBridgeRuntimeSnapshot runtime)
    {
        var desiredPosition = GetSelectedScreenPosition();
        if (runtime.ControlState.ScreenPosition == desiredPosition)
        {
            return runtime;
        }

        var updatedControlState = await _apiClient.SetControlMacFromWindowsAsync(
            options,
            runtime.ControlState.Enabled,
            desiredPosition,
            CancellationToken.None);

        return runtime with { ControlState = updatedControlState };
    }

    private DesktopBridgeOptions LoadConfiguration()
    {
        return _configService.Load();
    }

    private DesktopBridgeOptions? TryLoadConfiguration()
    {
        try
        {
            return LoadConfiguration();
        }
        catch
        {
            return null;
        }
    }

    private void SetFooter(string text, Color color)
    {
        _footerValue.Text = text;
        _footerValue.ForeColor = color;
    }

    private DesktopLocalSettings LoadDesktopSettingsSafe()
    {
        try
        {
            return _configService.LoadDesktopSettings();
        }
        catch
        {
            return new DesktopLocalSettings();
        }
    }

    private void ApplyDesktopSettingsSnapshot()
    {
        _isUpdatingScreenPosition = true;
        _screenPositionCombo.SelectedItem = _desktopSettings.MacScreenPosition;
        _isUpdatingScreenPosition = false;
        UpdateLayoutPreview(_desktopSettings.MacScreenPosition);
    }

    private MacScreenPosition GetSelectedScreenPosition()
    {
        return _screenPositionCombo.SelectedItem is MacScreenPosition position
            ? position
            : _desktopSettings.MacScreenPosition;
    }

    private void UpdateLayoutPreview(MacScreenPosition position)
    {
        _layoutPreviewLabel.Text = position switch
        {
            MacScreenPosition.LeftOfWindowsMonitor => "[Mac]      [Windows]",
            MacScreenPosition.RightOfWindowsMonitor => "[Windows]  [Mac]",
            MacScreenPosition.AboveWindowsMonitor => "[Mac]" + Environment.NewLine + Environment.NewLine + "[Windows]",
            MacScreenPosition.BelowWindowsMonitor => "[Windows]" + Environment.NewLine + Environment.NewLine + "[Mac]",
            _ => "[Mac]      [Windows]"
        };
    }

    private static string GetScreenPositionLabel(MacScreenPosition position)
    {
        return position switch
        {
            MacScreenPosition.LeftOfWindowsMonitor => "Left of Windows monitor",
            MacScreenPosition.RightOfWindowsMonitor => "Right of Windows monitor",
            MacScreenPosition.AboveWindowsMonitor => "Above Windows monitor",
            MacScreenPosition.BelowWindowsMonitor => "Below Windows monitor",
            _ => "Left of Windows monitor"
        };
    }

    private static string GetActivationEdgeLabel(MacScreenPosition position)
    {
        return position switch
        {
            MacScreenPosition.LeftOfWindowsMonitor => "Left",
            MacScreenPosition.RightOfWindowsMonitor => "Right",
            MacScreenPosition.AboveWindowsMonitor => "Top",
            MacScreenPosition.BelowWindowsMonitor => "Bottom",
            _ => "Left"
        };
    }

    private static void AddLabeledRow(TableLayoutPanel table, string caption, Control valueControl)
    {
        var rowIndex = table.RowCount;
        table.RowCount += 1;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var captionLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Margin = new Padding(0, 8, 0, 4),
            Text = caption
        };

        valueControl.Margin = new Padding(0, 8, 0, 4);

        table.Controls.Add(captionLabel, 0, rowIndex);
        table.Controls.Add(valueControl, 1, rowIndex);
    }

    private static void AddFullWidthRow(TableLayoutPanel table, Control control)
    {
        var rowIndex = table.RowCount;
        table.RowCount += 1;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(control, 0, rowIndex);
        table.SetColumnSpan(control, 2);
    }

    private static Label CreateValueLabel()
    {
        return new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 0)
        };
    }

    private static Label CreateWrappedValueLabel()
    {
        return new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Margin = new Padding(0, 0, 0, 0)
        };
    }
}
