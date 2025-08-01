using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.Net.NetworkInformation;
using System.Linq;
using System.Runtime.InteropServices;

namespace NetworkMonitor
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer _timer;
        private NetworkInterface _activeInterface;
        private long _previousBytesReceived;
        private long _previousBytesSent;
        private DateTime _previousTime;
        private long _totalBytesReceived;
        private long _totalBytesSent;
        private string _currentPlatform;

        public MainWindow()
        {
            InitializeComponent();
            DetectPlatform();
            InitializeNetworkMonitoring();
        }

        private void DetectPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _currentPlatform = "Windows";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                _currentPlatform = "macOS";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _currentPlatform = "Linux";
            }
            else
            {
                _currentPlatform = "Unknown Platform";
            }

            var platformText = this.FindControl<TextBlock>("PlatformInfoText");
            if (platformText != null)
            {
                platformText.Text = $"运行平台: {_currentPlatform} | {RuntimeInformation.ProcessArchitecture}";
            }
        }

        private void InitializeNetworkMonitoring()
        {
            FindActiveNetworkInterface();
            
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += UpdateNetworkStats;
            
            if (_activeInterface != null)
            {
                try
                {
                    var stats = GetNetworkStatistics(_activeInterface);
                    _previousBytesReceived = stats.BytesReceived;
                    _previousBytesSent = stats.BytesSent;
                    _previousTime = DateTime.Now;
                }
                catch (Exception ex)
                {
                    UpdateStatusText($"初始化错误: {ex.Message}");
                }
            }
            
            _timer.Start();
        }

        private void FindActiveNetworkInterface()
        {
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                               ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                               ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .OrderByDescending(ni => GetInterfacePriority(ni))
                    .ToList();

                _activeInterface = interfaces.FirstOrDefault();
                
                if (_activeInterface != null)
                {
                    var interfaceText = this.FindControl<TextBlock>("NetworkInterfaceText");
                    if (interfaceText != null)
                    {
                        var interfaceInfo = GetInterfaceDisplayName(_activeInterface);
                        interfaceText.Text = $"网络接口: {interfaceInfo}";
                    }
                    UpdateStatusText("监控已启动");
                }
                else
                {
                    UpdateStatusText("未找到可用的网络接口");
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText($"接口检测错误: {ex.Message}");
            }
        }

        private int GetInterfacePriority(NetworkInterface ni)
        {
            switch (ni.NetworkInterfaceType)
            {
                case NetworkInterfaceType.Ethernet:
                    return 10;
                case NetworkInterfaceType.Wireless80211:
                    return 9;
                case NetworkInterfaceType.GigabitEthernet:
                    return 8;
                case NetworkInterfaceType.FastEthernetT:
                    return 7;
                case NetworkInterfaceType.Ppp:
                    return 6;
                default:
                    if (_currentPlatform == "Linux" && ni.Name.StartsWith("wl"))
                        return 9;
                    if (_currentPlatform == "Linux" && ni.Name.StartsWith("en"))
                        return 10;
                    return 5;
            }
        }

        private string GetInterfaceDisplayName(NetworkInterface ni)
        {
            var typeName = GetFriendlyInterfaceType(ni);
            return $"{ni.Name} ({typeName})";
        }

        private string GetFriendlyInterfaceType(NetworkInterface ni)
        {
            switch (ni.NetworkInterfaceType)
            {
                case NetworkInterfaceType.Ethernet:
                case NetworkInterfaceType.GigabitEthernet:
                case NetworkInterfaceType.FastEthernetT:
                    return "以太网";
                case NetworkInterfaceType.Wireless80211:
                    return "无线网络";
                case NetworkInterfaceType.Ppp:
                    return "拨号连接";
                default:
                    if (_currentPlatform == "Linux")
                    {
                        if (ni.Name.StartsWith("wl") || ni.Name.StartsWith("wlan"))
                            return "无线网络 (Linux)";
                        if (ni.Name.StartsWith("en") || ni.Name.StartsWith("eth"))
                            return "以太网 (Linux)";
                    }
                    else if (_currentPlatform == "macOS")
                    {
                        if (ni.Name.StartsWith("en0") || ni.Name.StartsWith("en1"))
                            return ni.Name.Contains("Wi-Fi") ? "Wi-Fi (macOS)" : "以太网 (macOS)";
                    }
                    return $"{ni.NetworkInterfaceType}";
            }
        }

        // 修复：使用正确的返回类型
        private IPv4InterfaceStatistics GetNetworkStatistics(NetworkInterface ni)
        {
            try
            {
                return ni.GetIPv4Statistics();
            }
            catch
            {
                return new EmptyIPv4Statistics();
            }
        }

        private void UpdateNetworkStats(object sender, EventArgs e)
        {
            if (_activeInterface == null)
            {
                FindActiveNetworkInterface();
                return;
            }

            try
            {
                var stats = GetNetworkStatistics(_activeInterface);
                var currentTime = DateTime.Now;
                
                var currentBytesReceived = stats.BytesReceived;
                var currentBytesSent = stats.BytesSent;
                
                var timeDiff = (currentTime - _previousTime).TotalSeconds;
                
                if (timeDiff > 0 && _previousTime != DateTime.MinValue)
                {
                    var downloadSpeed = Math.Max(0, (currentBytesReceived - _previousBytesReceived) / timeDiff);
                    var uploadSpeed = Math.Max(0, (currentBytesSent - _previousBytesSent) / timeDiff);
                    
                    var downloadDiff = Math.Max(0, currentBytesReceived - _previousBytesReceived);
                    var uploadDiff = Math.Max(0, currentBytesSent - _previousBytesSent);
                    
                    _totalBytesReceived += downloadDiff;
                    _totalBytesSent += uploadDiff;
                    
                    UpdateUI(downloadSpeed, uploadSpeed);
                    UpdateStatusText("监控正常");
                }
                
                _previousBytesReceived = currentBytesReceived;
                _previousBytesSent = currentBytesSent;
                _previousTime = currentTime;
            }
            catch (Exception ex)
            {
                UpdateStatusText($"更新错误: {ex.Message}");
                FindActiveNetworkInterface();
            }
        }

        private void UpdateUI(double downloadSpeed, double uploadSpeed)
        {
            var downloadSpeedText = this.FindControl<TextBlock>("DownloadSpeedText");
            var uploadSpeedText = this.FindControl<TextBlock>("UploadSpeedText");
            var totalDataText = this.FindControl<TextBlock>("TotalDataText");
            var lastUpdateText = this.FindControl<TextBlock>("LastUpdateText");
            
            if (downloadSpeedText != null)
                downloadSpeedText.Text = FormatSpeed(downloadSpeed);
            
            if (uploadSpeedText != null)
                uploadSpeedText.Text = FormatSpeed(uploadSpeed);
            
            if (totalDataText != null)
            {
                var totalDownload = FormatBytes(_totalBytesReceived);
                var totalUpload = FormatBytes(_totalBytesSent);
                totalDataText.Text = $"下载: {totalDownload} | 上传: {totalUpload}";
            }
            
            if (lastUpdateText != null)
                lastUpdateText.Text = $"最后更新: {DateTime.Now:HH:mm:ss}";
        }

        private void UpdateStatusText(string status)
        {
            var statusText = this.FindControl<TextBlock>("StatusText");
            if (statusText != null)
                statusText.Text = $"状态: {status}";
        }

        private string FormatSpeed(double bytesPerSecond)
        {
            var absSpeed = Math.Abs(bytesPerSecond);
            
            if (absSpeed < 1024)
                return $"{bytesPerSecond:F2} B/s";
            else if (absSpeed < 1024 * 1024)
                return $"{bytesPerSecond / 1024:F2} KB/s";
            else if (absSpeed < 1024 * 1024 * 1024)
                return $"{bytesPerSecond / (1024 * 1024):F2} MB/s";
            else
                return $"{bytesPerSecond / (1024 * 1024 * 1024):F2} GB/s";
        }

        private string FormatBytes(long bytes)
        {
            var absBytes = Math.Abs(bytes);
            
            if (absBytes < 1024)
                return $"{bytes} B";
            else if (absBytes < 1024 * 1024)
                return $"{bytes / 1024.0:F1} KB";
            else if (absBytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):F1} MB";
            else
                return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer?.Stop();
            base.OnClosed(e);
        }
    }

    // 修复：使用正确的基类
    public class EmptyIPv4Statistics : IPv4InterfaceStatistics
    {
        public override long BytesReceived => 0;
        public override long BytesSent => 0;
        public override long IncomingPacketsDiscarded => 0;
        public override long IncomingPacketsWithErrors => 0;
        public override long IncomingUnknownProtocolPackets => 0;
        public override long NonUnicastPacketsReceived => 0;
        public override long NonUnicastPacketsSent => 0;
        public override long OutgoingPacketsDiscarded => 0;
        public override long OutgoingPacketsWithErrors => 0;
        public override long OutputQueueLength => 0;
        public override long UnicastPacketsReceived => 0;
        public override long UnicastPacketsSent => 0;
    }
}