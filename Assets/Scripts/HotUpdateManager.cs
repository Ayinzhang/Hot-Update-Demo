using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using YooAsset;
using HybridCLR;

public class HotUpdateManager : MonoBehaviour
{
    public EPlayMode _playMode = EPlayMode.EditorSimulateMode;
    public string defaultHostServer, fallbackHostServer, loadSceneName;

    Assembly _hotUpdateAss; ResourcePackage package; 
    Dictionary<string, byte[]> s_assetDatas = new Dictionary<string, byte[]>();
    List<string> assets { get; } = new List<string>() { "mscorlib.dll.bytes", "System.dll.bytes", "System.Core.dll.bytes", };

    internal class RemoteServices : IRemoteServices
    {
        readonly string _defaultHostServer, _fallbackHostServer;
        public RemoteServices(string defaultHostServer, string fallbackHostServer) { _defaultHostServer = defaultHostServer; _fallbackHostServer = fallbackHostServer; }
        public string GetRemoteFallbackURL(string fileName) { return $"{_fallbackHostServer}/{fileName}"; }
        public string GetRemoteMainURL(string fileName) { return $"{_defaultHostServer}/{fileName}"; }
    }
    void Start()
    {
        YooAssets.Initialize(); package = YooAssets.CreatePackage("DefaultPackage"); YooAssets.SetDefaultPackage(package);
        StartCoroutine(InitHotUpdate());
    }

    IEnumerator InitHotUpdate()
    {
        InitializationOperation initOperation = null;

        switch (_playMode)
        {
            case EPlayMode.EditorSimulateMode:
                // 编辑器模式下使用模拟构建的资源目录
                var buildResult = EditorSimulateModeHelper.SimulateBuild("DefaultPackage");
                var packageRoot = buildResult.PackageRootDirectory;
                var editorFileSystemParams = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot);
                var initParameters = new EditorSimulateModeParameters();
                initParameters.EditorFileSystemParameters = editorFileSystemParams;
                initOperation = package.InitializeAsync(initParameters);
                break;

            case EPlayMode.OfflinePlayMode:
            // 单机模式，使用内置资源（APK 内部或 StreamingAssets）
            offline:
                var buildinFileSystemParams = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
                var offinitParameters = new OfflinePlayModeParameters();
                offinitParameters.BuildinFileSystemParameters = buildinFileSystemParams;
                initOperation = package.InitializeAsync(offinitParameters);
                break;

            case EPlayMode.HostPlayMode:
                // 主机模式，从远程服务器下载资源，并缓存在本地
                IRemoteServices remoteServices = new RemoteServices(defaultHostServer, fallbackHostServer);
                var cacheFileSystemParams = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices);
                var hostbuildinFileSystemParams = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();

                var hostinitParameters = new HostPlayModeParameters();
                hostinitParameters.BuildinFileSystemParameters = hostbuildinFileSystemParams;
                hostinitParameters.CacheFileSystemParameters = cacheFileSystemParams;
                initOperation = package.InitializeAsync(hostinitParameters);
                break;

            case EPlayMode.WebPlayMode:
                // Web 模式，适用于 WebGL 平台
                IRemoteServices webremoteServices = new RemoteServices(defaultHostServer, fallbackHostServer);
                var webServerFileSystemParams = FileSystemParameters.CreateDefaultWebServerFileSystemParameters();
                var webRemoteFileSystemParams = FileSystemParameters.CreateDefaultWebRemoteFileSystemParameters(webremoteServices); //支持跨域下载

                var webinitParameters = new WebPlayModeParameters();
                webinitParameters.WebServerFileSystemParameters = webServerFileSystemParams;
                webinitParameters.WebRemoteFileSystemParameters = webRemoteFileSystemParams;

                initOperation = package.InitializeAsync(webinitParameters);
                break;

            case EPlayMode.CustomPlayMode:
                // 自定义弱联网模式，无网可游玩有网可更新
                IRemoteServices customServices = new RemoteServices(defaultHostServer, fallbackHostServer);

                var cutominitParameters = new HostPlayModeParameters();
                cutominitParameters.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
                cutominitParameters.CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(customServices);
                initOperation = package.InitializeAsync(cutominitParameters); yield return initOperation;

                if (initOperation.Status == EOperationStatus.Succeed) goto hotUpdate;
                else { Debug.Log("联网尝试失败，启用单机模式"); goto offline; }
        }

        // 等待初始化完成
        yield return initOperation; Debug.Log("初始化结果：" + initOperation.Status);
        if (initOperation.Status != EOperationStatus.Succeed)
        {
            Debug.LogError($"初始化失败: {initOperation.Error}");
            yield break;
        }
    hotUpdate:
        // 请求最新的资源版本信息
        var requestOperation = package.RequestPackageVersionAsync();
        Debug.Log("正在请求最新的资源版本...");
        yield return requestOperation;

        string packageVersion = "";
        if (requestOperation.Status == EOperationStatus.Succeed)
        {
            packageVersion = requestOperation.PackageVersion;
            Debug.Log($"获取最新资源版本成功: {packageVersion}");
        }
        else { Debug.LogError("获取资源版本失败：" + requestOperation.Error); }
        // 更新资源清单
        yield return package.UpdatePackageManifestAsync(packageVersion);
        // 开始下载缺失资源
        yield return DownloadAssets();
    }

    IEnumerator DownloadAssets()
    {
        var downloader = package.CreateResourceDownloader(10, 3); //最大并发下载数，下载失败重试次数

        if (downloader.TotalDownloadCount == 0)
        {
            Debug.Log("没有需要下载的资源");
            yield return InitScripts(); // 直接进入游戏
        }
        else 
        { 
            // 注册回调函数
            downloader.DownloadFinishCallback = OnDownloadFinishFunction; 
            downloader.DownloadErrorCallback = OnDownloadErrorFunction;
            downloader.DownloadUpdateCallback = OnDownloadUpdateFunction; 
            downloader.DownloadFileBeginCallback = OnDownloadFileBeginFunction;
            // 开始下载
            downloader.BeginDownload(); yield return downloader;
            if (downloader.Status == EOperationStatus.Succeed) { Debug.Log("资源下载成功"); yield return InitScripts(); }
            else Debug.LogError("资源下载失败");
        } 
    }

    void OnDownloadUpdateFunction(DownloadUpdateData data)
    {
        float progress = (float)data.CurrentDownloadBytes / (float)data.TotalDownloadBytes;
        Debug.Log($"总大小: {data.TotalDownloadBytes / 1024.0f / 1024} MB | 已下载: {data.CurrentDownloadBytes / 1024.0f / 1024} MB | 进度: {progress * 100:F2}%");
    }
    void OnDownloadFileBeginFunction(DownloadFileData data) { Debug.Log($"开始下载文件：{data.FileName}，大小：{data.FileSize / 1024.0f} KB"); }
    void OnDownloadErrorFunction(DownloadErrorData data) { Debug.LogError("下载错误"); }
    void OnDownloadFinishFunction(DownloaderFinishData data) { Debug.Log("下载完成"); }

    IEnumerator InitScripts()
    {
        foreach (var asset in assets)
        {
            string dllPath = $"file://{Application.streamingAssetsPath}/hybridclr/{asset}";
            Debug.Log($"start DownloadAssets asset:{dllPath}");
            UnityWebRequest www = UnityWebRequest.Get(dllPath);
            yield return www.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (www.result != UnityWebRequest.Result.Success) Debug.Log(www.error);
#else
            if (www.isHttpError || www.isNetworkError) Debug.Log(www.error);
#endif
            else
            {
                // 以二进制数据形式检索结果
                byte[] assetData = www.downloadHandler.data;
                Debug.Log($"dll:{asset}  size:{assetData.Length}");
                s_assetDatas[asset] = assetData;
            }
        }

        HomologousImageMode mode = HomologousImageMode.SuperSet;
        foreach (var aotDllName in assets)
        {
            byte[] dllBytes = s_assetDatas[aotDllName];
            // 加载assembly对应的dll，会自动为它hook。一旦aot泛型函数的native函数不存在，用解释器版本代码
            LoadImageErrorCode err = RuntimeApi.LoadMetadataForAOTAssembly(dllBytes, mode);
            Debug.Log($"LoadMetadataForAOTAssembly:{aotDllName}. mode:{mode} ret:{err}");
        }
#if !UNITY_EDITOR
        //_hotUpdateAss = Assembly.Load(File.ReadAllBytes($"{Application.streamingAssetsPath}/hybridclr/HotUpdate.dll.bytes"));
        AssetHandle handle = package.LoadAssetAsync<TextAsset>("Assets/Scripts/HotUpdate.dll.bytes");
        TextAsset textAsset = handle.AssetObject as TextAsset; _hotUpdateAss = Assembly.Load(textAsset.bytes);
#else
        _hotUpdateAss = System.AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "HotUpdate");
#endif
        Debug.Log("准备进入游戏场景: " + loadSceneName);
        yield return new WaitForSeconds(5f); yield return package.LoadSceneAsync(loadSceneName);
    }
}
