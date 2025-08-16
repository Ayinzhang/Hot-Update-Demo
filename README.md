# Hot-Update-Demo

[中文版](README_zh.md)

*This project explores hot-updates using YooAsset and HybridCLR. The main process is focused on the hot-update manager. The general structure of the program and files is as follows.*

```
1. InitHotUpdate() // Initialize YooAssets. The initialization process varies depending on the mode (standalone, networked, web).
2. DownloadAssets() // When connected to the network, retrieve hot-update assets from the server and perform A/B downloads for version comparison. Skip this step for standalone use.
3. InitLibrarys() // Load the three basic AOT runtime libraries provided by the official platform
4. InitScripts() // Load your own hot-update assembly
5. EnterGame() // Start Game

Assets
├─ Arts
│ ├─ Textures
│ ├─ Materials
│ ├─ Prefabs
│ └─ ...
├─ Scenes
│ └─ ...
├─ Scripts
│ ├─ HotUpdate
│ │ ├─ HotUpdate.asmdef     (Hot-update assembly)
│ │ └─ ...                    (Hot-update code)
│ └─ HotUpdateManager.cs   (Hot-update handler entry point)
│ └─ HotUpdate.dll.bytes      (Hot-update bytecode)
└─ StreamingAssets   (Assets directly embedded in the AB package)
├─ yoo                  
│ └─ ...                 (YooAsset Hot Update Assets)
└─ hybridclr
└─ ...                    (HybridCLR AOT Bytecode)
```