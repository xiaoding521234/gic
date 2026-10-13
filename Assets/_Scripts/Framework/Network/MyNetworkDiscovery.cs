using System;
using System.Collections;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;
using Mirror;
using Mirror.Discovery;
using System.Collections.Generic;
using GIC.Data;
using GIC.Pet;
namespace GIC.Framework
{


    public class MyNetworkDiscovery : NetworkDiscovery
    {
        [Header("房间信息")]
        public string RoomName = "战棋游戏";
        public string HostPlayerName = "玩家";
        public int CurrentPlayers = 1;
        public int MaxPlayers = 6;
        public string GameMode = "对战";

        /// <summary>客户端收到的房间显示信息（按 serverId 索引）</summary>
        public static readonly System.Collections.Generic.Dictionary<long, RoomDisplayInfo> DiscoveredRooms = new();

        public struct RoomDisplayInfo
        {
            public string HostName;
            public int CurrentPlayers;
            public int MaxPlayers;
        }
        
        [Autowired] private PlayerManager _playerManager;
        [Autowired] private SaveManager _saveManager;

        void Awake()
        {
            if (PetMode.Enabled) return; // 桌宠进程无 Wargame 上下文，跳过注入（对象随场景切换销毁）
            Wargame.Instance.Context.Inject(this);
        }

        public override void Start()
        {
            base.Start();
            
            if (_playerManager != null)
            {
                _playerManager.OnPlayerCountChanged += OnPlayerCountChanged;
            }
        }

        void OnPlayerCountChanged(int count)
        {
            CurrentPlayers = count;
            if (NetworkServer.active)
            {
                AdvertiseServer();
            }
        }

        public void StartBroadcast()
        {
            if (!NetworkServer.active)
            {
                GICLog.Warn("[MyNetworkDiscovery] StartBroadcast 跳过：NetworkServer 未激活（房间对发现客户端不可见）");
                return;
            }

            try
            {
                AdvertiseServer();
                GICLog.Info("[MyNetworkDiscovery] 开始广播房间信息");
            }
            catch (Exception ex)
            {
                // 47777 绑定失败此前会炸断 StartHost 整链（TryStartHostOnPort 吞掉后端口连跳），
                // 而 ServerListenAsync 又静默吞错——结果=房间建起来了但发现服务死亡，外部全搜不到且零日志。
                // 自愈：清掉残留监听再试一次；仍失败则记日志保命（房间可用，仅不可发现）。
                GICLog.Error($"[MyNetworkDiscovery] 发现服务绑定失败: {ex.Message}——清残留后重试");
                try { serverUdpClient?.Close(); } catch (Exception) { }
                try
                {
                    AdvertiseServer();
                    GICLog.Info("[MyNetworkDiscovery] 重试后开始广播房间信息");
                }
                catch (Exception ex2)
                {
                    GICLog.Error($"[MyNetworkDiscovery] 发现服务重试仍失败（房间将不可被发现）: {ex2.Message}");
                }
            }
        }

        public void StopBroadcast()
        {
            StopDiscovery();
            GICLog.Info("[MyNetworkDiscovery] 停止广播");
        }

        // ========== 修改点：替换默认的广播发现为单播扫描 ==========
        private Coroutine _scanCoroutine;

        public void StartDiscovering()
        {
            GICLog.Info("[MyNetworkDiscovery] 开始扫描局域网（单播模式）...");

            // 先调用基类方法初始化 clientUdpClient
            StartDiscovery();

            // 启动单播扫描协程
            _scanCoroutine = StartCoroutine(UnicastScanCoroutine());
        }

        public void StopDiscovering()
        {
            if (_scanCoroutine != null)
            {
                StopCoroutine(_scanCoroutine);
                _scanCoroutine = null;
            }
            StopDiscovery();
            GICLog.Info("[MyNetworkDiscovery] 停止扫描");
        }

        /// <summary>
    /// 获取本机可扫描的局域网 IPv4（2026-10-13 LAN 双机复测修复：手机开热点+蜂窝时笔记本建房
    /// 手机搜不到——根因=旧 Dns.GetHostEntry(Dns.GetHostName()) 在 Android 上不可靠，常只回
    /// 127.0.0.1 或蜂窝内网 IP（10.x）或直接抛异常→手机扫错网段/零扫描，笔记本收不到任何发现请求）。
    /// 改用 NetworkInterface 枚举：up+IPv4+非回环，排除蜂窝/VPN/虚拟网卡（接口名与描述匹配
    /// rmnet/ccmni/clat/tun/ppp/dummy/vmware/wsl 等——Windows 系统无此类名零影响；Android
    /// 热点接口 ap0/swlan0/wlan1 与 STA wlan0 天然保留），全空时回落全量非回环 IPv4 保底再
    /// 回落 Dns 法。热点主机与 STA 两形态同源覆盖。
    /// </summary>
    public List<string> GetAllLocalIPs()
    {
        List<string> lanIps = new List<string>();
        List<string> allIps = new List<string>();

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                // 排除蜂窝/VPN/虚拟网卡（Android 蜂窝=rmnet/联发科=ccmni/clat/VPN=tun/ppp；
                // PC 虚拟网卡=VMware/WSL/Hyper-V/VirtualBox——扫它们纯属浪费且可能扫蜂窝计费网段）
                string nameLower = (nic.Name + " " + nic.Description).ToLowerInvariant();
                bool excluded = nameLower.Contains("rmnet") || nameLower.Contains("ccmni")
                    || nameLower.Contains("clat") || nameLower.Contains("tun")
                    || nameLower.Contains("ppp") || nameLower.Contains("dummy")
                    || nameLower.Contains("vmware") || nameLower.Contains("wsl")
                    || nameLower.Contains("vethernet") || nameLower.Contains("hyper")
                    || nameLower.Contains("virtualbox") || nameLower.Contains("bluetooth");

                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(addr.Address)) continue;

                    string ip = addr.Address.ToString();
                    if (!allIps.Contains(ip)) allIps.Add(ip);
                    if (!excluded && !lanIps.Contains(ip)) lanIps.Add(ip);
                }
            }
        }
        catch (Exception ex)
        {
            GICLog.Error($"[MyNetworkDiscovery] NetworkInterface 枚举失败: {ex.Message}");
        }

        // 选结果：局域网候选优先 → 全量保底 → Dns 兜底
        List<string> result = lanIps.Count > 0 ? lanIps : allIps;
        if (result.Count == 0)
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        result.Add(ip.ToString());
                }
            }
            catch (Exception ex)
            {
                GICLog.Error($"[MyNetworkDiscovery] 获取IP失败: {ex.Message}");
            }
        }

        // 终极兜底（保持旧契约：非空列表，UI 显示与扫描守卫依赖）
        if (result.Count == 0)
        {
            result.Add("127.0.0.1");
        }

        GICLog.Info($"[MyNetworkDiscovery] 本机局域网IP: [{string.Join(", ", result)}]"
            + (result != lanIps && allIps.Count > 0 ? $"（局域网候选为空，回落全量: [{string.Join(", ", allIps)}]）" : ""));
        return result;
    }

    /// <summary>
    /// 单播扫描协程：对所有本机IP所在的网段进行扫描
    /// </summary>
    private IEnumerator UnicastScanCoroutine()
    {
        List<string> myIps = GetAllLocalIPs();
        if (myIps.Count == 0 || (myIps.Count == 1 && myIps[0] == "127.0.0.1"))
        {
            GICLog.Error("[MyNetworkDiscovery] 无法获取有效的本机IP");
            yield break;
        }

        int port = GetServerBroadcastListenPort();
        GICLog.Info($"[MyNetworkDiscovery] 目标端口: {port}");

        UdpClient client = GetClientUdpClient();
        if (client == null)
        {
            GICLog.Error("[MyNetworkDiscovery] clientUdpClient 为空");
            yield break;
        }

        // 收集所有要扫描的网段（去重）
        HashSet<string> scannedBases = new HashSet<string>();
        foreach (string myIp in myIps)
        {
            string baseIp = myIp.Substring(0, myIp.LastIndexOf('.') + 1);
            if (scannedBases.Add(baseIp))
            {
                GICLog.Info($"[MyNetworkDiscovery] 加入扫描网段: {baseIp}1 - {baseIp}254");
            }
        }

        int totalScanned = 0;
        foreach (string baseIp in scannedBases)
        {
            for (int i = 1; i <= 254; i++)
            {
                string targetIp = baseIp + i;
                
                // 跳过本机所有IP
                if (myIps.Contains(targetIp))
                    continue;

                try
                {
                    IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(targetIp), port);
                    
                    using (NetworkWriterPooled writer = NetworkWriterPool.Get())
                    {
                        writer.WriteLong(secretHandshake);
                        ServerRequest request = GetRequest();
                        writer.Write(request);
                        ArraySegment<byte> data = writer.ToArraySegment();
                        client.SendAsync(data.Array, data.Count, endPoint);
                    }
                    totalScanned++;
                }
                catch (Exception)
                {
                    // 忽略单点失败
                }

                // 每 20 个包暂停一帧
                if (totalScanned % 20 == 0)
                    yield return null;
            }
        }

        GICLog.Info($"[MyNetworkDiscovery] 扫描完成，共扫描 {scannedBases.Count} 个网段，发送 {totalScanned} 个请求");
    }

        /// <summary>
        /// 获取本机局域网 IP（建房后 UI 显示给玩家抄 IP 用）——复用 GetAllLocalIPs 单源：
        /// 旧 Dns 法在 Android 上可能显示蜂窝 IP，别人照抄连不上（同取址修复，2026-10-13）
        /// </summary>
        public string GetLocalIPAddress()
        {
            return GetAllLocalIPs()[0];
        }

        /// <summary>
        /// 通过反射获取基类的 serverBroadcastListenPort
        /// </summary>
        private int GetServerBroadcastListenPort()
        {
            var field = typeof(NetworkDiscoveryBase<ServerRequest, ServerResponse>)
                .GetField("serverBroadcastListenPort", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (field != null)
            {
                return (int)field.GetValue(this);
            }
            
            GICLog.Warn("[MyNetworkDiscovery] 无法获取 serverBroadcastListenPort，使用默认值 47777");
            return 47777; // 默认端口
        }

        /// <summary>
        /// 通过反射获取基类的 clientUdpClient
        /// </summary>
        private UdpClient GetClientUdpClient()
        {
            var field = typeof(NetworkDiscoveryBase<ServerRequest, ServerResponse>)
                .GetField("clientUdpClient", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            return field?.GetValue(this) as UdpClient;
        }

        // ========== 以下保持原有代码不变 ==========

        protected override ServerResponse ProcessRequest(ServerRequest request, IPEndPoint endpoint)
        {
            try
            {
                if (_playerManager != null)
                {
                    CurrentPlayers = _playerManager.GetPlayerCount();
                }

                // 从存档获取 Host 名称
                HostPlayerName = _saveManager?.CurrentSave?.playerName ?? "旅行者";

                var baseUri = transport.ServerUri();
                // 将房间信息编码到 URI 查询参数中
                var builder = new UriBuilder(baseUri)
                {
                    Query = $"host={Uri.EscapeDataString(HostPlayerName)}&cur={CurrentPlayers}&max={MaxPlayers}"
                };

                var response = new ServerResponse
                {
                    serverId = ServerId,
                    uri = builder.Uri
                };

                GICLog.Info($"[MyNetworkDiscovery] 响应发现请求: {endpoint.Address}:{endpoint.Port} - 房主: {HostPlayerName} - 玩家: {CurrentPlayers}/{MaxPlayers}");
                return response;
            }
            catch (NotImplementedException)
            {
                GICLog.Error($"Transport {transport} 不支持网络发现");
                throw;
            }
            catch (Exception ex)
            {
                // 2026-10-13：上游 ServerListenAsync 只吞不记——这里不记的话，响应链任何异常
                // 都表现为「房间静默不可见」（手机端实测整段零日志），排障无从下手。
                GICLog.Error($"[MyNetworkDiscovery] 处理发现请求异常（本条不回包，继续监听）: {ex}");
                throw;
            }
        }

        protected override void ProcessResponse(ServerResponse response, IPEndPoint endpoint)
        {
            GICLog.Info($"[MyNetworkDiscovery] 收到服务器响应 - IP: {endpoint.Address}, Port: {endpoint.Port}");

            response.EndPoint = endpoint;

            // 从 URI 查询参数解析房间信息
            var query = System.Web.HttpUtility.ParseQueryString(response.uri.Query);
            string hostName = query["host"] ?? "未知房主";
            int cur = int.TryParse(query["cur"], out var c) ? c : 1;
            int max = int.TryParse(query["max"], out var m) ? m : 6;

            DiscoveredRooms[response.serverId] = new RoomDisplayInfo
            {
                HostName = Uri.UnescapeDataString(hostName),
                CurrentPlayers = cur,
                MaxPlayers = max
            };

            UriBuilder realUri = new UriBuilder(response.uri)
            {
                Host = response.EndPoint.Address.ToString(),
                Query = "" // 清除查询参数，避免影响连接
            };
            response.uri = realUri.Uri;

            OnServerFound.Invoke(response);
            
            GICLog.Info($"[MyNetworkDiscovery] 发现房间: {response.EndPoint.Address}:{response.uri.Port} 房主: {hostName} ({cur}/{max})");
        }

        void OnDestroy()
        {
            if (_playerManager != null)
            {
                _playerManager.OnPlayerCountChanged -= OnPlayerCountChanged;
            }
            
            StopDiscovery();
            DiscoveredRooms.Clear();
        }
    }
}

