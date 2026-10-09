using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace VRLandscape
{
    // View Sync: one presenter, many viewers on a local network (Wi-Fi Direct room, hotspot or shared Wi-Fi).
    // The presenter streams "where I am and what I'm looking at" ~15x a second over UDP; viewers mirror it.
    // Touching the screen makes a viewer roam freely; "Presenter view" snaps back.
    //
    // Messages (UTF-8 text, '|' separated):
    //   PROBE                          viewer -> anyone      "is anyone presenting?"
    //   HERE|name|park|room            presenter -> viewer   reply to PROBE
    //   JOIN|name  PING  LEAVE          viewer -> presenter   subscribe / keep-alive / unsubscribe
    //   STATE|seq|park|mode|node|px|py|pz|qx|qy|qz|qw         presenter -> viewers
    public class ViewSync : MonoBehaviour
    {
        public enum Role { None, Presenter, Follower }

        public struct State
        {
            public int seq;
            public string park, mode, node;
            public Vector3 position;     // presenter's camera position
            public Quaternion rotation;  // presenter's camera rotation
        }

        public class Presenter
        {
            public string name, park, room;
            public IPEndPoint endpoint;   // null for a Wi-Fi Direct room not joined yet
            public float lastSeen;
            public string Key => endpoint != null ? endpoint.ToString() : "room:" + room;
        }

        public const int Port = 47820;
        const string Magic = "VRLS1";
        const float SendInterval = 1f / 15f;
        const float ProbeInterval = 1.5f;
        const float ViewerTimeout = 8f;

        public static ViewSync Instance { get; private set; }

        public Role CurrentRole { get; private set; }
        public bool Roaming { get; private set; }
        public string RoomCode { get; private set; }
        public string PresenterName => presenter?.name;
        public int ViewerCount => viewers.Count;
        public bool HasState { get; private set; }
        public State Latest { get; private set; }
        public float LastStateTime { get; private set; } = -100f;
        public bool Connected => CurrentRole == Role.Follower && Time.unscaledTime - LastStateTime < 4f;
        public IReadOnlyList<Presenter> Presenters => presenters;
        public string Status { get; private set; } = "";

        public Func<State> Capture;          // presenter: App supplies the current view
        public Action FollowResumed;         // viewer tapped "Presenter view"

        Socket socket;
        Thread receiveThread;
        volatile bool running;
        readonly ConcurrentQueue<(string msg, IPEndPoint from)> inbox = new ConcurrentQueue<(string, IPEndPoint)>();
        readonly Dictionary<string, (IPEndPoint ep, float seen)> viewers = new Dictionary<string, (IPEndPoint, float)>();
        readonly List<Presenter> presenters = new List<Presenter>();
        Presenter presenter;
        string pendingRoom;          // viewer: room code we're joining (auto-join its presenter when found)
        bool browsing;
        float nextSend, nextProbe, nextPing, nextDiscover, nextAdvertise;
        string advertisedPark;
        float lastFullAdvertise = -100f;
        int seq;

        // Overlay badge
        Canvas overlay;
        RectTransform badge;
        Text badgeText;
        Image badgeImage;
        Button presenterViewButton;

        public static ViewSync Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("View Sync");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<ViewSync>();
            return Instance;
        }

        static string DeviceName
        {
            get
            {
                string n = SystemInfo.deviceName;
                if (string.IsNullOrEmpty(n) || n == "<unknown>") n = SystemInfo.deviceModel;
                return Clean(n);
            }
        }

        static string Clean(string s) => (s ?? "").Replace('|', '/').Trim();

        // ------------------------------------------------------------ roles

        public void StartPresenting(bool createRoom)
        {
            Stop();
            if (!OpenSocket(Port)) return;
            CurrentRole = Role.Presenter;
            RoomCode = UnityEngine.Random.Range(1000, 10000).ToString();
            advertisedPark = null;
            nextAdvertise = 0;
            if (createRoom && LocalGroup.Supported) LocalGroup.Host(RoomCode);
            Status = "Presenting";
            RefreshBadge();
        }

        // Viewer: look for presenters on the current network.
        public void StartBrowsing()
        {
            if (CurrentRole != Role.None || browsing) return;
            if (!OpenSocket(0)) return;
            browsing = true;
            presenters.Clear();
            nextProbe = 0;
            nextDiscover = 0;
        }

        // Everything joinable nearby: presenters answering on this network plus advertised Wi-Fi Direct rooms.
        public List<Presenter> Nearby()
        {
            var list = new List<Presenter>(presenters);
            if (!LocalGroup.Supported || CurrentRole != Role.None) return list;
            foreach (var r in LocalGroup.Rooms())
                if (!list.Exists(p => p.room == r.code))
                    list.Add(new Presenter { name = r.name, park = r.park, room = r.code, lastSeen = Time.unscaledTime });
            return list;
        }

        public void JoinNearby(Presenter p)
        {
            if (p == null) return;
            if (p.endpoint != null) Follow(p);
            else JoinRoom(p.room);
        }

        public void StopBrowsing()
        {
            if (!browsing || CurrentRole != Role.None) return;
            browsing = false;
            if (pendingRoom == null) LocalGroup.StopDiscovery();
            CloseSocket();
        }

        // Viewer: join a presenter's Wi-Fi Direct room by code, then follow them once they're found.
        public void JoinRoom(string code)
        {
            if (string.IsNullOrEmpty(code)) return;
            StartBrowsing();
            pendingRoom = code;
            if (LocalGroup.Supported) LocalGroup.Join(code);
            Status = "Joining room " + code + "...";
        }

        public void Follow(Presenter p)
        {
            if (p == null) return;
            if (socket == null && !OpenSocket(0)) return;
            browsing = false;
            pendingRoom = null;
            LocalGroup.StopDiscovery();
            presenter = p;
            CurrentRole = Role.Follower;
            Roaming = false;
            HasState = false;
            Send("JOIN|" + DeviceName, p.endpoint);
            nextPing = Time.unscaledTime + 2f;
            Status = "Following " + p.name;
            RefreshBadge();
        }

        public void Stop()
        {
            if (CurrentRole == Role.Follower && presenter != null) Send("LEAVE", presenter.endpoint);
            if (CurrentRole == Role.Presenter || pendingRoom != null || LocalGroup.State == "joined") LocalGroup.Stop();
            CurrentRole = Role.None;
            Roaming = false;
            HasState = false;
            presenter = null;
            pendingRoom = null;
            browsing = false;
            viewers.Clear();
            presenters.Clear();
            CloseSocket();
            Status = "";
            RefreshBadge();
        }

        // Viewer touched the screen: stop mirroring, keep the connection.
        public void Roam()
        {
            if (CurrentRole != Role.Follower || Roaming) return;
            Roaming = true;
            RefreshBadge();
        }

        public void ResumeFollowing()
        {
            if (CurrentRole != Role.Follower) return;
            Roaming = false;
            RefreshBadge();
            FollowResumed?.Invoke();
        }

        // ------------------------------------------------------------ networking

        bool OpenSocket(int port)
        {
            CloseSocket();
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.EnableBroadcast = true;
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
                running = true;
                receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "ViewSync" };
                receiveThread.Start();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[VRLandscape] View Sync socket: " + e.Message);
                Status = "Network unavailable: " + e.Message;
                CloseSocket();
                return false;
            }
        }

        void CloseSocket()
        {
            running = false;
            try { socket?.Close(); } catch { /* ignore */ }
            socket = null;
            receiveThread = null;
        }

        void ReceiveLoop()
        {
            var buffer = new byte[2048];
            while (running)
            {
                try
                {
                    EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    int n = socket.ReceiveFrom(buffer, ref from);
                    inbox.Enqueue((Encoding.UTF8.GetString(buffer, 0, n), (IPEndPoint)from));
                }
                catch
                {
                    if (!running) return;
                    Thread.Sleep(20);
                }
            }
        }

        void Send(string message, IPEndPoint to)
        {
            if (socket == null || to == null) return;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(Magic + "|" + message);
                socket.SendTo(bytes, to);
            }
            catch { /* unreachable hosts during probing are expected */ }
        }

        void Update()
        {
            while (inbox.TryDequeue(out var item)) Handle(item.msg, item.from);

            float now = Time.unscaledTime;
            if (CurrentRole == Role.Presenter)
            {
                ExpireViewers(now);
                if (LocalGroup.Supported && now >= nextAdvertise && LocalGroup.State == "hosting" && Capture != null)
                {
                    nextAdvertise = now + 5f;
                    string park = Clean(Capture().park);
                    if (park != advertisedPark || now - lastFullAdvertise > 60f)
                    {
                        advertisedPark = park;
                        lastFullAdvertise = now;
                        LocalGroup.Advertise(RoomCode, DeviceName, park.Length > 0 ? park : "Park list");
                    }
                }
                if (now >= nextSend && viewers.Count > 0 && Capture != null)
                {
                    nextSend = now + SendInterval;
                    var s = Capture();
                    string msg = string.Join("|", "STATE", (++seq).ToString(CultureInfo.InvariantCulture),
                        Clean(s.park), Clean(s.mode), Clean(s.node),
                        F(s.position.x), F(s.position.y), F(s.position.z),
                        F(s.rotation.x), F(s.rotation.y), F(s.rotation.z), F(s.rotation.w));
                    foreach (var v in viewers.Values) Send(msg, v.ep);
                }
            }
            else if (CurrentRole == Role.Follower)
            {
                if (now >= nextPing) { nextPing = now + 2f; Send("PING", presenter.endpoint); }
            }

            if (browsing || pendingRoom != null)
            {
                if (pendingRoom != null) LocalGroup.Poll();
                else if (LocalGroup.Supported && now >= nextDiscover) { nextDiscover = now + 8f; LocalGroup.Discover(); }
                if (now >= nextProbe) { nextProbe = now + ProbeInterval; Probe(); }
                presenters.RemoveAll(p => now - p.lastSeen > 6f);
            }

            RefreshBadgeText();
        }

        void Handle(string msg, IPEndPoint from)
        {
            var parts = msg.Split('|');
            if (parts.Length < 2 || parts[0] != Magic) return;
            float now = Time.unscaledTime;
            switch (parts[1])
            {
                case "PROBE":
                    if (CurrentRole == Role.Presenter && Capture != null)
                        Send($"HERE|{DeviceName}|{Clean(Capture().park)}|{RoomCode}", from);
                    break;

                case "HERE":
                    if (parts.Length < 5 || CurrentRole != Role.None) break;
                    var existing = presenters.Find(p => p.endpoint.Equals(from));
                    if (existing == null) presenters.Add(existing = new Presenter { endpoint = from });
                    existing.name = parts[2];
                    existing.park = parts[3];
                    existing.room = parts[4];
                    existing.lastSeen = now;
                    if (pendingRoom != null && existing.room == pendingRoom) Follow(existing);
                    break;

                case "JOIN":
                case "PING":
                    if (CurrentRole == Role.Presenter) viewers[from.ToString()] = (from, now);
                    break;

                case "LEAVE":
                    viewers.Remove(from.ToString());
                    break;

                case "STATE":
                    if (CurrentRole != Role.Follower || parts.Length < 13 || !from.Address.Equals(presenter.endpoint.Address)) break;
                    int s = int.Parse(parts[2], CultureInfo.InvariantCulture);
                    if (HasState && s <= Latest.seq && Latest.seq - s < 1000) break; // old or duplicate
                    Latest = new State
                    {
                        seq = s, park = parts[3], mode = parts[4], node = parts[5],
                        position = new Vector3(P(parts[6]), P(parts[7]), P(parts[8])),
                        rotation = new Quaternion(P(parts[9]), P(parts[10]), P(parts[11]), P(parts[12]))
                    };
                    HasState = true;
                    LastStateTime = now;
                    break;
            }
        }

        void ExpireViewers(float now)
        {
            List<string> gone = null;
            foreach (var kv in viewers)
                if (now - kv.Value.seen > ViewerTimeout) (gone ??= new List<string>()).Add(kv.Key);
            if (gone != null) foreach (var k in gone) viewers.Remove(k);
        }

        // Ask every address on our local /24 networks (plus broadcast and the Wi-Fi Direct owner) who's presenting.
        // Unicast sweeps work even where phones filter broadcast packets.
        void Probe()
        {
            var targets = new HashSet<string> { "255.255.255.255", LocalGroup.PresenterAddress };
            foreach (var ip in LocalAddresses())
            {
                var b = ip.GetAddressBytes();
                for (int h = 1; h < 255; h++)
                    if (h != b[3]) targets.Add($"{b[0]}.{b[1]}.{b[2]}.{h}");
            }
            foreach (var t in targets) Send("PROBE", new IPEndPoint(IPAddress.Parse(t), Port));
        }

        static List<IPAddress> LocalAddresses()
        {
            var list = new List<IPAddress>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork && IsPrivate(ua.Address) && !list.Contains(ua.Address))
                            list.Add(ua.Address);
                }
            }
            catch { /* some Android builds restrict interface listing */ }
            if (list.Count == 0)
            {
                try
                {
                    using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    s.Connect(new IPEndPoint(IPAddress.Parse("10.254.254.254"), 1));
                    var a = ((IPEndPoint)s.LocalEndPoint).Address;
                    if (IsPrivate(a)) list.Add(a);
                }
                catch { /* no network */ }
            }
            return list.Count > 4 ? list.GetRange(0, 4) : list;
        }

        static bool IsPrivate(IPAddress a)
        {
            var b = a.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
        }

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static float P(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        void OnDestroy()
        {
            Stop();
            if (Instance == this) Instance = null;
        }

        void OnApplicationQuit() => Stop();

        // ------------------------------------------------------------ overlay badge

        void RefreshBadge()
        {
            if (CurrentRole == Role.None || XRBootstrap.IsXRActive)
            {
                if (overlay != null) overlay.gameObject.SetActive(false);
                return;
            }
            if (overlay == null) BuildOverlay();
            overlay.gameObject.SetActive(true);
            bool showButton = CurrentRole == Role.Follower && Roaming;
            presenterViewButton.gameObject.SetActive(showButton);
            badge.gameObject.SetActive(!showButton);
            RefreshBadgeText();
        }

        void RefreshBadgeText()
        {
            if (badgeText == null || !badge.gameObject.activeInHierarchy) return;
            string text;
            Color color;
            if (CurrentRole == Role.Presenter)
            {
                string room = LocalGroup.Supported ? RoomText() : "";
                text = $"LIVE • {ViewerCount} VIEWER{(ViewerCount == 1 ? "" : "S")}{room}";
                color = Theme.Red;
            }
            else if (!Connected)
            {
                text = "RECONNECTING...";
                color = Theme.Yellow;
            }
            else
            {
                text = "FOLLOWING " + UIFactory.Shorten(PresenterName ?? "", 16).ToUpperInvariant();
                color = Theme.Violet;
            }
            if (badgeText.text != text) badgeText.text = text;
            badgeImage.color = color;
        }

        string RoomText()
        {
            switch (LocalGroup.State)
            {
                case "hosting": return " • ROOM " + RoomCode;
                case "starting": return " • ROOM...";
                case "failed": return " • NO ROOM";
                default: return "";
            }
        }

        void BuildOverlay()
        {
            overlay = UIFactory.CreateCanvas("Sync Overlay", world: false);
            overlay.sortingOrder = 30;
            overlay.transform.SetParent(transform, false);
            badge = UIFactory.Badge(overlay.transform, "", Theme.Red, 22, 1.5f);
            UIFactory.Place(badge, new Vector2(1, 1), new Vector2(400, 56), new Vector2(-36, -132));
            badge.GetComponent<Image>().raycastTarget = false;
            badgeImage = badge.GetComponent<Image>();
            badgeText = badge.GetComponentInChildren<Text>();
            UIFactory.FitInside(badgeText, 14);

            presenterViewButton = UIFactory.Button(overlay.transform, "← Presenter view", ResumeFollowing, false, Theme.Red, 26);
            UIFactory.Place((RectTransform)presenterViewButton.transform, new Vector2(1, 1), new Vector2(340, 84), new Vector2(-32, -126));
            presenterViewButton.gameObject.AddComponent<Pulse>();
        }
    }

    // Gentle size pulse for call-to-action buttons.
    public class Pulse : MonoBehaviour
    {
        void Update()
        {
            float s = 1f + 0.04f * Mathf.Sin(Time.unscaledTime * 5f);
            transform.localScale = new Vector3(s, s, 1f);
        }
    }
}
