using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Interactivity;
using System;
using System.Net.NetworkInformation;
using System.Linq;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Collections.ObjectModel;

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
        private List<NetworkInterface> _availableInterfaces;

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
            // 初始化计时器但不启动
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += UpdateNetworkStats;
            
            // 加载所有网络接口
            LoadNetworkInterfaces();
        }

        private void LoadNetworkInterfaces()
        {
            try
            {
                _availableInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                               ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                               ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                               IsPhysicalInterface(ni))
                    .OrderByDescending(ni => GetInterfacePriority(ni))
                    .ToList();

                var comboBox = this.FindControl<ComboBox>("InterfaceComboBox");
                if (comboBox != null)
                {
                    var items = new ObservableCollection<NetworkInterfaceItem>();
                    
                    foreach (var ni in _availableInterfaces)
                    {
                        items.Add(new NetworkInterfaceItem
                        {
                            Interface = ni,
                            DisplayName = GetInterfaceDisplayName(ni),
                            Description = GetInterfaceDescription(ni)
                        });
                    }
                    
                    comboBox.ItemsSource = items;
                    
                    // 自动选择第一个接口
                    if (items.Count > 0)
                    {
                        comboBox.SelectedIndex = 0;
                    }
                }

                UpdateStatusText($"找到 {_availableInterfaces.Count} 个物理网络接口");
            }
            catch (Exception ex)
            {
                UpdateStatusText($"加载网络接口错误: {ex.Message}");
            }
        }

        private bool IsPhysicalInterface(NetworkInterface ni)
        {
            // 过滤掉虚拟和非物理接口
            var name = ni.Name.ToLower();
            var description = ni.Description.ToLower();
            
            // Windows 特殊处理：直接排除明显的虚拟接口
            if (_currentPlatform == "Windows")
            {
                // 直接排除这些明显的虚拟/过滤接口
                var windowsExcludePatterns = new[]
                {
                    // Windows 网络过滤和虚拟组件
                    "wfp", "npcap", "pcap", "filter", "lightweight", "miniport",
                    "packet scheduler", "qos", "wan", "ras", "teredo", "isatap",
                    "6to4", "tunnel", "loopback", "virtual", "vmware", "virtualbox",
                    "hyper-v", "docker", "tap-", "vpn", "bluetooth", "infrared",
                    "hosted network", "microsoft wi-fi direct", "native wifi filter"
                };
                
                foreach (var pattern in windowsExcludePatterns)
                {
                    if (name.Contains(pattern) || description.Contains(pattern))
                    {
                        return false;
                    }
                }
                
                // Windows 上以 WLAN- 开头的通常都是虚拟组件，除非是真正的适配器
                if (name.StartsWith("wlan-"))
                {
                    // 只保留真正的 WLAN 适配器，排除所有过滤器和服务组件
                    return false;
                }
                
                // 检查接口类型，确保是真正的物理接口
                bool isPhysicalType = (
                    ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.FastEthernetT ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.FastEthernetFx ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                );
                
                // 对于 Wi-Fi 接口，进一步验证是否是真正的适配器
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    // 真正的 Wi-Fi 适配器通常名称简单，如 "Wi-Fi", "WLAN", "Wireless Network Connection"
                    // 而不是复杂的技术组件名称
                    bool isRealWiFiAdapter = (
                        name == "wi-fi" ||
                        name == "wlan" ||
                        name == "wireless network connection" ||
                        name.StartsWith("wireless network connection") ||
                        (name.Contains("wi-fi") && !name.Contains("-") && !name.Contains("filter") && !name.Contains("driver"))
                    );
                    
                    return isRealWiFiAdapter;
                }
                
                return isPhysicalType;
            }
            
            // 其他平台的处理
            var excludePatterns = new[]
            {
                // Linux 虚拟接口
                "veth", "br-", "docker", "virbr", "vnet", "tun", "tap", "ppp",
                
                // macOS 虚拟接口
                "bridge", "p2p", "awdl", "llw", "utun", "ipsec", "gif", "stf",
                
                // 通用虚拟接口
                "loopback", "virtual", "vmware", "virtualbox"
            };
            
            // 检查名称和描述是否包含虚拟接口标识
            foreach (var pattern in excludePatterns)
            {
                if (name.Contains(pattern) || description.Contains(pattern))
                {
                    return false;
                }
            }
            
            // 进一步检查接口类型
            switch (ni.NetworkInterfaceType)
            {
                case NetworkInterfaceType.Loopback:
                case NetworkInterfaceType.Tunnel:
                case NetworkInterfaceType.Slip:
                case NetworkInterfaceType.Isdn:
                case NetworkInterfaceType.BasicIsdn:
                case NetworkInterfaceType.PrimaryIsdn:
                case NetworkInterfaceType.MultiRateSymmetricDsl:
                case NetworkInterfaceType.RateAdaptDsl:
                case NetworkInterfaceType.SymmetricDsl:
                case NetworkInterfaceType.VeryHighSpeedDsl:
                case NetworkInterfaceType.AsymmetricDsl:
                case NetworkInterfaceType.GenericModem:
                    return false;
                    
                case NetworkInterfaceType.Ethernet:
                case NetworkInterfaceType.Wireless80211:
                case NetworkInterfaceType.GigabitEthernet:
                case NetworkInterfaceType.FastEthernetT:
                case NetworkInterfaceType.FastEthernetFx:
                    return true;
                    
                default:
                    // 对于 Unknown 类型，进一步检查
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Unknown)
                    {
                        // Linux 上的物理接口通常以 eth, en, wlan, wl 开头
                        if (_currentPlatform == "Linux")
                        {
                            return name.StartsWith("eth") || name.StartsWith("en") || 
                                   name.StartsWith("wlan") || name.StartsWith("wl");
                        }
                        // macOS 上的物理接口
                        else if (_currentPlatform == "macOS")
                        {
                            return name.StartsWith("en") && !name.Contains("bridge");
                        }
                        return false;
                    }
                    return true;
            }
        }

        private void OnInterfaceSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = sender as ComboBox;
            var selectedItem = comboBox?.SelectedItem as NetworkInterfaceItem;
            
            if (selectedItem != null)
            {
                SwitchToInterface(selectedItem.Interface);
            }
        }

        private void OnRefreshButtonClick(object sender, RoutedEventArgs e)
        {
            StopMonitoring();
            LoadNetworkInterfaces();
        }

        private void SwitchToInterface(NetworkInterface networkInterface)
        {
            try
            {
                // 停止当前监控
                StopMonitoring();
                
                // 切换到新接口
                _activeInterface = networkInterface;
                
                // 重置统计数据
                ResetStatistics();
                
                // 获取初始数据
                var stats = GetNetworkStatistics(_activeInterface);
                _previousBytesReceived = stats.BytesReceived;
                _previousBytesSent = stats.BytesSent;
                _previousTime = DateTime.Now;
                
                // 更新界面显示
                UpdateInterfaceDetails();
                
                // 启动监控
                _timer.Start();
                
                UpdateStatusText($"正在监控: {GetInterfaceDisplayName(_activeInterface)}");
            }
            catch (Exception ex)
            {
                UpdateStatusText($"切换接口错误: {ex.Message}");
            }
        }

        private void StopMonitoring()
        {
            _timer?.Stop();
        }

        private void ResetStatistics()
        {
            _totalBytesReceived = 0;
            _totalBytesSent = 0;
            _previousBytesReceived = 0;
            _previousBytesSent = 0;
            _previousTime = DateTime.MinValue;
            
            // 重置UI显示
            var downloadSpeedText = this.FindControl<TextBlock>("DownloadSpeedText");
            var uploadSpeedText = this.FindControl<TextBlock>("UploadSpeedText");
            var totalDataText = this.FindControl<TextBlock>("TotalDataText");
            
            if (downloadSpeedText != null) downloadSpeedText.Text = "0.00 KB/s";
            if (uploadSpeedText != null) uploadSpeedText.Text = "0.00 KB/s";
            if (totalDataText != null) totalDataText.Text = "下载: 0 MB | 上传: 0 MB";
        }

        private void UpdateInterfaceDetails()
        {
            if (_activeInterface == null) return;
            
            var detailText = this.FindControl<TextBlock>("InterfaceDetailText");
            if (detailText != null)
            {
                var speed = _activeInterface.Speed > 0 ? 
                    $" | 速度: {FormatSpeed(_activeInterface.Speed)}" : "";
                var status = _activeInterface.OperationalStatus;
                var description = _activeInterface.Description;
                
                detailText.Text = $"接口: {description} | 状态: {status}{speed}";
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

        private string GetInterfaceDescription(NetworkInterface ni)
        {
            var description = ni.Description;
            if (description.Length > 50)
                description = description.Substring(0, 47) + "...";
            return description;
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
            if (_activeInterface == null) return;

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
                    UpdateStatusText($"监控正常 - {GetInterfaceDisplayName(_activeInterface)}");
                }
                
                _previousBytesReceived = currentBytesReceived;
                _previousBytesSent = currentBytesSent;
                _previousTime = currentTime;
            }
            catch (Exception ex)
            {
                UpdateStatusText($"更新错误: {ex.Message}");
                // 接口可能断开，刷新接口列表
                LoadNetworkInterfaces();
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
                return $"{bytesPerSecond / 1024:F2} KB/s";
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
            StopMonitoring();
            base.OnClosed(e);
        }
    }

    // 网络接口数据模型
    public class NetworkInterfaceItem
    {
        public NetworkInterface Interface { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    // 空的IPv4统计类
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