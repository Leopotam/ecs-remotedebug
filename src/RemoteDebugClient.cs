// ----------------------------------------------------------------------------
// The MIT License
// Remote debug https://github.com/Leopotam/ecs-remotedebug
// for ECS framework https://github.com/Leopotam/ecs
// Copyright (c) 2017-2020 Leopotam <leopotam@gmail.com>
// ----------------------------------------------------------------------------
#if DEBUG
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Leopotam.Ecs.RemoteDebug {
    public sealed class RemoteDebugClient : IEcsWorldDebugListener, IEcsSystemsDebugListener {
        const int MaxInboundCommands = 1000;

        readonly EcsWorld _world;
        readonly Uri _url;
        readonly ClientWebSocket _ws = new ClientWebSocket ();
        readonly JsonSerialization _json = new JsonSerialization ();
        readonly object _syncObj = new object ();
        readonly Queue<RemoteCmd> _inCmds = new Queue<RemoteCmd> (512);
        readonly Queue<RemoteCmd> _outCmds = new Queue<RemoteCmd> (512);
        object[] _componentValuesCache = new object[16];
        Type[] _componentTypesCache = new Type[16];
        readonly Pool<List<string>> _stringsPool = new Pool<List<string>> (64);

        public bool IsConnected => _ws.State == WebSocketState.Open;

        public RemoteDebugClient (EcsWorld world, int port = 1111, string host = "localhost") {
            _world = world;
            _world.AddDebugListener (this);
            _url = new Uri ($"ws://{host}:{port}");
            Connect ();
        }

        /// <summary>
        /// Processes inbound commands. Should be called from EcsWorld thread. 
        /// </summary>
        public void Run () {
            lock (_syncObj) {
                while (_inCmds.Count > 0) {
                    var cmd = _inCmds.Dequeue ();
                    ProcessRequest (cmd);
                }
            }
        }

        void ProcessRequest (in RemoteCmd cmd) {
#if UNITY_2019_1_OR_NEWER
            // UnityEngine.Debug.Log ($"[IN] {_json.Serialize (cmd)}");
#endif
            switch (cmd.Type) {
                case RemoteCmdType.HeartBeat:
                    _outCmds.Enqueue (new RemoteCmd { Type = RemoteCmdType.HeartBeat });
                    break;
                case RemoteCmdType.EntityDataRequested:
                    var entity = _world.RestoreEntityFromInternalId (cmd.EntityId, cmd.EntityGen);
                    if (entity.IsAlive ()) {
                        var count = entity.GetComponentTypes (ref _componentTypesCache);
                        entity.GetComponentValues (ref _componentValuesCache);
                        var list = _stringsPool.Get ();
                        for (var i = 0; i < count; i++) {
                            list.Add (_componentTypesCache[i].Name);
                            list.Add (_json.Serialize (_componentValuesCache[i]));
                        }
                        var res = new RemoteCmd {
                            Type = RemoteCmdType.EntityDataResponded,
                            EntityId = cmd.EntityId,
                            EntityGen = cmd.EntityGen,
                            ComponentsData = list
                        };
                        _outCmds.Enqueue (res);
                    }
                    break;
            }
        }

        async void Connect () {
            try {
                await _ws.ConnectAsync (_url, CancellationToken.None);
#if UNITY_2019_1_OR_NEWER
                if (IsConnected) {
                    UnityEngine.Debug.LogWarning ("[REMOTE-DEBUG] CONNECTED");
                }
#endif
                var sndTask = Send (_ws);
                var rcvTask = Receive (_ws);
                await Task.WhenAny (sndTask, rcvTask);
#if UNITY_2019_1_OR_NEWER
                if (rcvTask.Exception != null) {
                    UnityEngine.Debug.LogWarning (rcvTask.Exception.InnerException?.ToString ());
                }
#endif
            } catch {
                // ignored
            } finally {
                if (IsConnected) {
                    try {
                        await _ws.CloseAsync (WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    } catch {
                        // ignored
                    }
                }
#if UNITY_2019_1_OR_NEWER
                UnityEngine.Debug.LogWarning ("[REMOTE-DEBUG] DISCONNECTED");
#endif
            }
        }

        async Task Send (ClientWebSocket ws) {
            while (IsConnected) {
                string cmdJson = null;
                lock (_syncObj) {
                    if (_outCmds.Count > 0) {
                        var cmd = _outCmds.Dequeue ();
                        cmdJson = _json.Serialize (cmd);
                        if (cmd.ComponentsData != null) {
                            cmd.ComponentsData.Clear ();
                            _stringsPool.Recycle (cmd.ComponentsData);
                        }
                    }
                }
                if (cmdJson != null) {
#if UNITY_2019_1_OR_NEWER
                    // UnityEngine.Debug.Log ($"[REMOTE-DEBUG] Send: {cmdJson}");
#endif
                    var buffer = Encoding.UTF8.GetBytes (cmdJson);
                    await ws.SendAsync (new ArraySegment<byte> (buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                } else {
                    await Task.Yield ();
                }
            }
        }

        async Task Receive (ClientWebSocket webSocket) {
            var buffer = new byte[4096];
            while (IsConnected) {
                var result = await webSocket.ReceiveAsync (new ArraySegment<byte> (buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) {
                    await webSocket.CloseAsync (WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    return;
                }
                if (!result.EndOfMessage) {
                    throw new Exception ("[REMOTE-DEBUG] Inbound partial messages not supported.");
                }
                var json = Encoding.UTF8.GetString (buffer, 0, result.Count);
                var cmd = _json.Deserialize<RemoteCmd> (json);
                lock (_syncObj) {
                    if (_inCmds.Count > MaxInboundCommands) { throw new Exception ("[REMOTE-DEBUG] Too many non-processed inbound commands."); }
                    _inCmds.Enqueue (cmd);
                }
            }
        }

        void IEcsWorldDebugListener.OnEntityCreated (EcsEntity entity) {
            lock (_syncObj) {
                var cmd = new RemoteCmd {
                    Type = RemoteCmdType.EntityCreated,
                    EntityId = entity.GetInternalId (),
                    EntityGen = entity.GetInternalGen ()
                };
                _outCmds.Enqueue (cmd);
            }
        }

        void IEcsWorldDebugListener.OnEntityDestroyed (EcsEntity entity) {
            lock (_syncObj) {
                var cmd = new RemoteCmd {
                    Type = RemoteCmdType.EntityDestroyed,
                    EntityId = entity.GetInternalId (),
                    EntityGen = entity.GetInternalGen ()
                };
                _outCmds.Enqueue (cmd);
            }
        }

        void IEcsWorldDebugListener.OnFilterCreated (EcsFilter filter) { }

        void IEcsWorldDebugListener.OnComponentListChanged (EcsEntity entity) {
            lock (_syncObj) {
                var cmd = new RemoteCmd {
                    Type = RemoteCmdType.EntityChanged,
                    EntityId = entity.GetInternalId (),
                    EntityGen = entity.GetInternalGen ()
                };
                _outCmds.Enqueue (cmd);
            }
        }

        void IEcsWorldDebugListener.OnWorldDestroyed (EcsWorld world) {
            if (world == _world) {
                lock (_syncObj) {
                    _world.RemoveDebugListener (this);
                    _inCmds.Clear ();
                    _outCmds.Clear ();
                    if (IsConnected) {
                        _ws.CloseAsync (WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    }
                }
            }
        }

        void IEcsSystemsDebugListener.OnSystemsDestroyed (EcsSystems systems) { }

        /// <summary>
        /// Pool for ref types.
        /// </summary>
        sealed class Pool<T> where T : new () {
            T[] _items;
            int _count;
            readonly object _sync = new object ();

            public Pool (int capacity) {
                _items = new T[capacity];
            }

            public T Get () {
                lock (_sync) {
                    if (_count > 0) {
                        return _items[--_count];
                    }
                }
                return new T ();
            }

            public void Recycle (T item) {
                lock (_sync) {
                    if (_items.Length == _count) {
                        Array.Resize (ref _items, _items.Length << 1);
                    }
                    _items[_count++] = item;
                }
            }
        }

        enum RemoteCmdType {
            HeartBeat,
            EntityCreated,
            EntityDestroyed,
            EntityChanged,
            EntityDataRequested,
            EntityDataResponded,
        }

        struct RemoteCmd {
            [JsonName ("t")]
            public RemoteCmdType Type;
            [JsonName ("i")]
            public int EntityId;
            [JsonName ("g")]
            public int EntityGen;
            [JsonName ("c")]
            // ReSharper disable once NotAccessedField.Local
            public List<string> ComponentsData;
        }
    }
}
#endif