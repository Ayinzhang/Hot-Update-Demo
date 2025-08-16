# 热更初探

[English Version](README.md)

*项目为使用YooAsset和HybridCLR的热更探索，主流程集中于热更管理器，程序和文件的大致结构如下*

```YAML
1. InitHotUpdate() //初始化YooAssets，根据不同模式(单机，联网，Web)走不同初始化流程
2. DownloadAssets() //联网时从服务器获取热更资源，比对版本走AB下载。单机时直接跳过该步
3. InitLibrarys() //加载官方给的三个最基础的AOT运行库
4. InitScripts() //加载自己的热更程序集
5. EnterGame() //原神启动

Assets
├─ Arts
│  ├─ Textures
│  ├─ Materials
│  ├─ Prefabs
│  └─ ...
├─ Scenes
│  └─ ...
├─ Scripts
│  ├─ HotUpdate
│  │  ├─ HotUpdate.asmdef    (热更程序集)
│  │  └─ ...                 (可热更代码)
│  └─ HotUpdateManager.cs    (热更处理程序入口)
│  └─ HotUpdate.dll.bytes     (热更字节码)
└─ StreamingAssets           (直接打入AB包的资源)
   ├─ yoo                   
   │  └─ ...                (YooAsset热更资源)
   └─ hybridclr
      └─ ...                 (HybridCLR AOT字节码)
```
