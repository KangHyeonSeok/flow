// E2EBeacon.cs - Unity E2E Test UDP Broadcast Beacon
//
// UDP 브로드캐스트를 통해 E2E 테스트 도구에 앱의 존재와 연결 정보를 알립니다.
// 1초 간격으로 로컬 네트워크에 JSON 메시지를 브로드캐스트합니다.
//
// 사용법:
//   1. E2EServer와 같은 GameObject에 E2EBeacon 컴포넌트 추가
//   2. Scripting Define Symbols에 E2E_TESTS 추가
//
// 브로드캐스트 메시지:
//   {
//     "app": "my-unity-app",
//     "platform": "unity",
//     "port": 51321,
//     "version": "1.0.0"
//   }

#if E2E_TESTS

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace FlowE2E
{
    /// <summary>
    /// UDP broadcast beacon for E2E test app discovery.
    /// Broadcasts connection info on port 51320 so the test tool can find this app.
    /// Only active when E2E_TESTS scripting define symbol is set.
    /// </summary>
    public class E2EBeacon : MonoBehaviour
    {
        /// <summary>
        /// Fixed UDP discovery port. Must match the test tool's listener port.
        /// </summary>
        private const int DISCOVERY_PORT = 51320;

        [Header("Beacon Settings")]
        [Tooltip("Broadcast interval in seconds")]
        [SerializeField] private float broadcastInterval = 1.0f;

        [Tooltip("Enable debug logging")]
        [SerializeField] private bool debugLog = false;

        // --- Internal State ---
        private UdpClient _udpClient;
        private IPEndPoint _broadcastEndpoint;
        private float _lastBroadcastTime;
        private E2EServer _server;
        private string _cachedMessage;

        // ─────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────

        private void Start()
        {
            _server = GetComponent<E2EServer>();
            if (_server == null)
            {
                Debug.LogError("[E2EBeacon] E2EServer component not found! " +
                               "E2EBeacon must be on the same GameObject as E2EServer.");
                enabled = false;
                return;
            }

            try
            {
                _udpClient = new UdpClient();
                _udpClient.EnableBroadcast = true;
                _broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, DISCOVERY_PORT);

                // Cache the broadcast message (doesn't change at runtime)
                _cachedMessage = BuildMessage();

                if (debugLog)
                    Debug.Log($"[E2EBeacon] Started broadcasting on UDP port {DISCOVERY_PORT}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[E2EBeacon] Failed to initialize: {ex.Message}");
                enabled = false;
            }
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup - _lastBroadcastTime >= broadcastInterval)
            {
                Broadcast();
                _lastBroadcastTime = Time.realtimeSinceStartup;
            }
        }

        private void OnDestroy()
        {
            _udpClient?.Close();
            _udpClient?.Dispose();

            if (debugLog)
                Debug.Log("[E2EBeacon] Stopped broadcasting");
        }

        // ─────────────────────────────────────────────
        // Broadcasting
        // ─────────────────────────────────────────────

        private void Broadcast()
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(_cachedMessage);
                _udpClient.Send(bytes, bytes.Length, _broadcastEndpoint);

                if (debugLog)
                    Debug.Log($"[E2EBeacon] Broadcast sent: {_cachedMessage}");
            }
            catch (SocketException ex)
            {
                // Network temporarily unavailable -- skip this cycle
                if (debugLog)
                    Debug.LogWarning($"[E2EBeacon] Broadcast failed: {ex.Message}");
            }
            catch (ObjectDisposedException)
            {
                // Client disposed during shutdown
                enabled = false;
            }
        }

        private string BuildMessage()
        {
            var msg = new BroadcastMessage
            {
                app = _server.AppName,
                platform = "unity",
                port = _server.Port,
                version = Application.version
            };

            return JsonUtility.ToJson(msg);
        }

        // ─────────────────────────────────────────────
        // Data Model
        // ─────────────────────────────────────────────

        [Serializable]
        private class BroadcastMessage
        {
            public string app;
            public string platform;
            public int port;
            public string version;
        }
    }
}

#endif // E2E_TESTS
