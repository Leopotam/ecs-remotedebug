[![discord](https://img.shields.io/discord/404358247621853185.svg?label=discord)](https://discord.gg/5GZVde6)
[![license](https://img.shields.io/github/license/Leopotam/ecs-remotedebug.svg)](https://github.com/Leopotam/ecs-remotedebug/blob/develop/LICENSE)
# Remote debug connector for Entity Component System framework
[Remote debug](https://github.com/Leopotam/ecs-remotedebug) for [ECS framework](https://github.com/Leopotam/ecs).

> **Important! It's PoC, don't use it if you dont know what is it!**

> C#7.3 or above required for this framework.

> Tested on unity 2019.1 (not dependent on it) and contains assembly definition for compiling to separate assembly file for performance reason.

> Dependent on [ECS framework](https://github.com/Leopotam/ecs) - ECS framework should be imported to the solution/unity project first.

> **Important!** It's connection (client) part only, external (server) viewer app you can [find here](https://github.com/snatvb/leo-ecs-remote-debugger).

# Installation

## As unity module
This repository can be installed as unity module directly from git url. In this way new line should be added to `Packages/manifest.json`:
```
"com.leopotam.ecs-remotedebug": "https://github.com/Leopotam/ecs-remotedebug.git",
```
By default last released version will be used. If you need trunk / developing version then `develop` name of branch should be added after hash:
```
"com.leopotam.ecs-remotedebug": "https://github.com/Leopotam/ecs-remotedebug.git#develop",
```

## As source
If you can't / don't want to use unity modules, code can be downloaded as sources archive of required release from [Releases page](`https://github.com/Leopotam/ecs-remotedebug/releases`).

## As c# project
`Leopotam.Ecs.RemoteDebug.csproj` can be used as plain c# project after adding reference to [LeoECS](https://github.com/Leopotam/ecs) `.csproj` file into

# Integration

Integration can be processed with creation of `LeopotamGroup.Ecs.RemoteDebug` class instance - this call should be wrapped to `#if DEBUG` preprocessor define:
```csharp
public class Startup : MonoBehaviour {
    EcsWorld _world;
    EcsSystems _systems;
#if DEBUG
    Leopotam.Ecs.RemoteDebug.RemoteDebugClient _debug;
#endif

    void Start () {
        _world = new EcsWorld ();
        _systems = new EcsSystems(_world);
#if DEBUG
        _debug = new Leopotam.Ecs.RemoteDebug.RemoteDebugClient (_world);
#endif  
        _systems.
            .Add (new RunSystem1());
            // Additional initialization here...
            .Init ();
    }

    void Update() {
        _systems?.Run ();
#if DEBUG
        _debug?.Run ();
#endif
    }
}
```

`RemoteDebugClient` instance **must** be created before any entity will be created in ecs-world.

# Custom connection
By default client uses `localhost` with `1111` port as external server app, you can change this behaviour:
```csharp
    void Start () {
        _world = new EcsWorld ();
        _systems = new EcsSystems(_world);
#if DEBUG
        var newPort = 1234;
        var newHost = "my.customserver.com";
        _debug = new Leopotam.Ecs.RemoteDebug.RemoteDebugClient (_world, newPort, newHost);
#endif  
        _systems.
            .Add (new RunSystem1());
            // Additional initialization here...
            .Init ();
    }
```

# License
The software released under the terms of the [MIT license](./LICENSE.md). Enjoy.

# Donate
Its free opensource software, but you can buy me a coffee:

<a href="https://www.buymeacoffee.com/leopotam" target="_blank"><img src="https://www.buymeacoffee.com/assets/img/custom_images/yellow_img.png" alt="Buy Me A Coffee" style="height: auto !important;width: auto !important;" ></a>