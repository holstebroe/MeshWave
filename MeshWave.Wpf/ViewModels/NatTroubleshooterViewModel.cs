using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MeshWave.Synchronizer;
using MeshWave.Wpf.Mvvm;

namespace MeshWave.Wpf.ViewModels;

public class NatTroubleshooterViewModel : ViewModelBase
{
    private readonly SyncOrchestrator _syncOrchestrator;
    private string _testStatus = "Idle";
    private bool _isTesting;

    public NatTroubleshooterViewModel(SyncOrchestrator syncOrchestrator, int configuredPort)
    {
        _syncOrchestrator = syncOrchestrator ?? throw new ArgumentNullException(nameof(syncOrchestrator));
        ConfiguredPort = configuredPort;

        RetryUPnPCommand = new RelayCommand(_ => _ = RetryUPnPAsync(), _ => !_isTesting);
        TestConnectionCommand = new RelayCommand(_ => _ = TestConnectionAsync(), _ => !_isTesting);
    }

    public int ConfiguredPort { get; }

    public string NatStatus => _syncOrchestrator.NatTraversal.NatStatus;
    public string Diagnostics => _syncOrchestrator.NatTraversal.Diagnostics;
    public string? ExternalIPAddress => _syncOrchestrator.NatTraversal.ExternalIPAddress;
    public string? MappingProtocol => _syncOrchestrator.NatTraversal.MappingProtocol;

    public string TestStatus
    {
        get => _testStatus;
        private set => SetProperty(ref _testStatus, value);
    }

    public bool IsTesting
    {
        get => _isTesting;
        private set
        {
            if (SetProperty(ref _isTesting, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public ICommand RetryUPnPCommand { get; }
    public ICommand TestConnectionCommand { get; }

    private async Task RetryUPnPAsync()
    {
        IsTesting = true;
        TestStatus = "Retrying UPnP/PMP discovery...";

        try
        {
            // Note: StopAsync will clear mappings, then we request new ones
            await _syncOrchestrator.NatTraversal.SetupPortMappingAsync(ConfiguredPort, CancellationToken.None);

            OnPropertyChanged(nameof(NatStatus));
            OnPropertyChanged(nameof(Diagnostics));
            OnPropertyChanged(nameof(ExternalIPAddress));
            OnPropertyChanged(nameof(MappingProtocol));

            TestStatus = "UPnP retry finished.";
        }
        catch (Exception ex)
        {
            TestStatus = $"UPnP retry error: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        TestStatus = "Testing external connection (loopback)...";

        try
        {
            if (string.IsNullOrWhiteSpace(ExternalIPAddress))
            {
                TestStatus = "External IP is unknown. Ensure you are connected to the internet and UPnP succeeded, or manual forwarding is configured.";
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var tcpClient = new TcpClient();

            try
            {
                await tcpClient.ConnectAsync(ExternalIPAddress, ConfiguredPort, cts.Token);
                TestStatus = "Success! External loopback connection reached the local port.";
            }
            catch (OperationCanceledException)
            {
                TestStatus = "Connection timed out. Port forwarding may be required or your router blocks loopback (Hairpin NAT).";
            }
            catch (SocketException ex)
            {
                TestStatus = $"Connection refused or unreachable: {ex.Message}. Check port forwarding.";
            }
        }
        catch (Exception ex)
        {
            TestStatus = $"Test error: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }
}
