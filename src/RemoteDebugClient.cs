// ----------------------------------------------------------------------------
// The MIT License
// Remote debug https://github.com/Leopotam/ecs-remotedebug
// for ECS framework https://github.com/Leopotam/ecs
// Copyright (c) 2017-2020 Leopotam <leopotam@gmail.com>
// ----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Leopotam.Ecs.RemoteDebug {
    public sealed class RemoteDebugClient : IEcsWorldDebugListener, IEcsSystemsDebugListener {
        readonly EcsWorld _world;
        readonly Uri _url;
        readonly ClientWebSocket _ws = new ClientWebSocket ();
        readonly JsonSerialization _json = new JsonSerialization ();
        readonly object _syncObj = new object ();
        readonly Queue<RemoteCmd> _outCmds = new Queue<RemoteCmd> (512);
        readonly Queue<RemoteCmd> _inCmds = new Queue<RemoteCmd> (512);
        object[] _componentValuesCache = new object[16];
        readonly StringBuilder _sb = new StringBuilder (512);

        public bool IsConnected => _ws.State == WebSocketState.Open;

        public RemoteDebugClient (EcsWorld world, int port = 1111, string host = "localhost") {
            _world = world;
            _world.AddDebugListener (this);
            _url = new Uri ($"ws://{host}:{port}");
            Connect ();
        }

        async void Connect () {
            try {
                await _ws.ConnectAsync (_url, CancellationToken.None);
                await Task.WhenAll (Receive (_ws), Send (_ws));
            } catch {
                // ignored
            } finally {
                if (IsConnected) {
                    await _ws.CloseAsync (WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                }
                UnityEngine.Debug.LogWarning ("STOPPED");
            }
        }

        async Task Send (ClientWebSocket ws) {
            while (IsConnected) {
                string cmd = null;
                lock (_syncObj) {
                    if (_outCmds.Count > 0) {
                        cmd = _json.Serialize (_outCmds.Dequeue ());
                    }
                }
                if (cmd != null) {
                    var buffer = Encoding.UTF8.GetBytes (cmd);
                    await ws.SendAsync (new ArraySegment<byte> (buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                } else {
                    await Task.Delay (1);
                }
            }
        }

        async Task Receive (ClientWebSocket webSocket) {
            var buffer = new byte[4096];
            while (IsConnected) {
                var result = await webSocket.ReceiveAsync (new ArraySegment<byte> (buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) {
                    await webSocket.CloseAsync (WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                    return;
                }
                if (!result.EndOfMessage) {
                    throw new Exception ("Partial messages not supported");
                }
                var cmd = _json.Deserialize<RemoteCmd> (Encoding.UTF8.GetString (buffer, 0, result.Count));
                lock (_syncObj) {
                    // FIXME: send to _inCmds queue and process in main thread of world?
                    ProcessCmdRequest (cmd);
                }
            }
        }

        void ProcessCmdRequest (RemoteCmd cmd) {
            switch (cmd.Cmd) {
                case RemoteCmdType.RequestEntityData:
                    if (cmd.Int1.HasValue && cmd.Int2.HasValue) {
                        var entity = _world.RestoreEntityFromInternalId (cmd.Int1.Value, cmd.Int2.Value);
                        if (entity.IsAlive ()) {
                            var count = entity.GetComponentValues (ref _componentValuesCache);
                            for (var i = 0; i < count; i++) {
                                _sb.AppendLine (_json.Serialize (_componentValuesCache[i]));
                            }
                            var res = new RemoteCmd {
                                Cmd = RemoteCmdType.RequestEntityData,
                                Int1 = cmd.Int1,
                                Int2 = cmd.Int2,
                                Str1 = _sb.ToString ()
                            };
                            _sb.Length = 0;
                            _outCmds.Enqueue (res);
                        }
                    }
                    break;
            }
        }

        void IEcsWorldDebugListener.OnEntityCreated (EcsEntity entity) {
            var cmd = new RemoteCmd { Cmd = RemoteCmdType.EntityCreated, Int1 = entity.GetInternalId () };
            lock (_syncObj) {
                _outCmds.Enqueue (cmd);
            }
        }

        void IEcsWorldDebugListener.OnEntityDestroyed (EcsEntity entity) {
            var cmd = new RemoteCmd { Cmd = RemoteCmdType.EntityRemoved, Int1 = entity.GetInternalId () };
            lock (_syncObj) {
                _outCmds.Enqueue (cmd);
            }
        }

        void IEcsWorldDebugListener.OnFilterCreated (EcsFilter filter) {
            // FIXME: OnFilterCreated implementation.
        }

        void IEcsWorldDebugListener.OnComponentListChanged (EcsEntity entity) {
            var cmd = new RemoteCmd { Cmd = RemoteCmdType.EntityChanged, Int1 = entity.GetInternalId () };
            lock (_syncObj) {
                _outCmds.Enqueue (cmd);
            }
        }

        void IEcsWorldDebugListener.OnWorldDestroyed (EcsWorld world) {
            if (world == _world) {
                _world.RemoveDebugListener (this);
                lock (_syncObj) {
                    _outCmds.Clear ();
                    _inCmds.Clear ();
                    if (IsConnected) {
                        _ws.CloseAsync (WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    }
                }
            }
        }

        void IEcsSystemsDebugListener.OnSystemsDestroyed (EcsSystems systems) {
            // FIXME: OnFilterCreated implementation.
        }

        enum RemoteCmdType {
            EntityCreated,
            EntityRemoved,
            EntityChanged,
            RequestEntityData
        }

        struct RemoteCmd {
            [JsonName ("cmd")]
            public RemoteCmdType Cmd;
            [JsonName ("int1")]
            public int? Int1;
            [JsonName ("int2")]
            public int? Int2;
            [JsonName ("str1")]
            // ReSharper disable once NotAccessedField.Local
            public string Str1;
        }
    }
}