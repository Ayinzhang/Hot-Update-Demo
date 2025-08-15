using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using YooAsset;
using HybridCLR;

public class HotUpdateManager : MonoBehaviour
{
    public EPlayMode _playMode = EPlayMode.EditorSimulateMode;
    public string defaultHostServer = "http://127.0.0.1/CDN/Android/v1.0", fallbackHostServer = "http://127.0.0.1/CDN/Android/v1.0", loadSceneName;

    ResourcePackage package;
    void Start()
    {
        YooAssets.Initialize(); package = YooAssets.CreatePackage("DefaultPackage"); YooAssets.SetDefaultPackage(package);
        StartCoroutine(InitPackage()); StartCoroutine(DownLoadAssets());
    }

    IEnumerator InitPackage()
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
        }

        // 等待初始化完成
        yield return initOperation;

        Debug.Log("初始化结果：" + initOperation.Status);
        if (initOperation.Status != EOperationStatus.Succeed)
        {
            Debug.LogError($"初始化失败: {initOperation.Error}");
            yield break;
        }

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
        else
        {
            Debug.LogError("获取资源版本失败：" + requestOperation.Error);
        }

        // 更新资源清单
        var updateOperation = package.UpdatePackageManifestAsync(packageVersion);
        yield return updateOperation;

        if (updateOperation.Status == EOperationStatus.Succeed)
        {
            AssetHandle handle = package.LoadAssetAsync<GameObject>("Assets/Cube.prefab");
            handle.Completed += (assetHandle) =>
            {
                if (assetHandle.Status == EOperationStatus.Succeed)
                {
                    GameObject playerPrefab = assetHandle.AssetObject as GameObject;
                    Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
                }
                else
                {
                    Debug.LogError("资源加载失败！");
                }
            };
            Debug.Log("资源清单更新成功");
        }
        else Debug.LogError("资源清单更新失败：" + updateOperation.Error);

        // 开始下载缺失资源
        yield return Download();
    }

    /// <summary>
    /// 下载缺失的远程资源
    /// </summary>
    IEnumerator Download()
    {
        int downloadingMaxNum = 10;   // 最大并发下载数
        int failedTryAgain = 3;       // 下载失败重试次数

        var downloader = package.CreateResourceDownloader(downloadingMaxNum, failedTryAgain);

        if (downloader.TotalDownloadCount == 0)
        {
            Debug.Log("没有需要下载的资源");
            yield return EnterGame(); // 直接进入游戏
        }

        // 注册回调函数
        downloader.DownloadFinishCallback = OnDownloadFinishFunction;
        downloader.DownloadErrorCallback = OnDownloadErrorFunction;
        downloader.DownloadUpdateCallback = OnDownloadUpdateFunction;
        downloader.DownloadFileBeginCallback = OnDownloadFileBeginFunction;

        // 开始下载
        downloader.BeginDownload();
        yield return downloader;

        if (downloader.Status == EOperationStatus.Succeed)
        {
            Debug.Log("资源下载成功");
            yield return EnterGame();
        }
        else
        {
            Debug.LogError("资源下载失败");
        }
    }

    /// <summary>
    /// 进入游戏场景
    /// </summary>
    IEnumerator EnterGame()
    {
        SceneHandle handle = package.LoadSceneAsync(loadSceneName);
        yield return handle;
    }

    #region 下载回调函数

    /// <summary>
    /// 下载进度更新回调
    /// </summary>
    void OnDownloadUpdateFunction(DownloadUpdateData data)
    {
        float progress = (float)data.CurrentDownloadBytes / (float)data.TotalDownloadBytes;
        Debug.Log($"总大小: {data.TotalDownloadBytes / 1024.0f / 1024} MB | 已下载: {data.CurrentDownloadBytes / 1024.0f / 1024} MB | 进度: {progress * 100:F2}%");
    }

    /// <summary>
    /// 开始下载某个文件时触发
    /// </summary>
    void OnDownloadFileBeginFunction(DownloadFileData data)
    {
        Debug.Log($"开始下载文件：{data.FileName}，大小：{data.FileSize / 1024.0f} KB");
    }

    /// <summary>
    /// 下载错误时触发
    /// </summary>
    void OnDownloadErrorFunction(DownloadErrorData data)
    {
        Debug.LogError("下载错误");
    }

    /// <summary>
    /// 下载结束时触发（无论成功或失败）
    /// </summary>
    void OnDownloadFinishFunction(DownloaderFinishData data)
    {
        Debug.Log("下载完成");
    }

    #endregion
    internal class RemoteServices : IRemoteServices
    {

        readonly string _defaultHostServer;
        readonly string _fallbackHostServer;

        public RemoteServices(string defaultHostServer, string fallbackHostServer)
        {
            _defaultHostServer = defaultHostServer;
            _fallbackHostServer = fallbackHostServer;
        }


        public string GetRemoteFallbackURL(string fileName)
        {
            return $"{_fallbackHostServer}/{fileName}";
        }

        public string GetRemoteMainURL(string fileName)
        {
            return $"{_defaultHostServer}/{fileName}";
        }
    }

    #region download assets

    static Dictionary<string, byte[]> s_assetDatas = new Dictionary<string, byte[]>();

    public static byte[] ReadBytesFromStreamingAssets(string dllName)
    {
        return s_assetDatas[dllName];
    }

    string GetWebRequestPath(string asset)
    {
        var path = $"{Application.streamingAssetsPath}/{asset}";
        if (!path.Contains("://")) path = "file://" + path;
        return path;
    }
    static List<string> AOTMetaAssemblyFiles { get; } = new List<string>()
    {
        "mscorlib.dll.bytes", "System.dll.bytes", "System.Core.dll.bytes",
    };

    IEnumerator DownLoadAssets()
    {
        var assets = new List<string>
        {
            "prefabs",
            "HotUpdate.dll.bytes",
        }.Concat(AOTMetaAssemblyFiles);

        foreach (var asset in assets)
        {
            string dllPath = GetWebRequestPath(asset);
            Debug.Log($"start download asset:{dllPath}");
            UnityWebRequest www = UnityWebRequest.Get(dllPath);
            yield return www.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.Log(www.error);
            }
#else
            if (www.isHttpError || www.isNetworkError)
            {
                Debug.Log(www.error);
            }
#endif
            else
            {
                // Or retrieve results as binary data
                byte[] assetData = www.downloadHandler.data;
                Debug.Log($"dll:{asset}  size:{assetData.Length}");
                s_assetDatas[asset] = assetData;
            }
        }

        InitGame();
    }

    #endregion

    static Assembly _hotUpdateAss;

    /// <summary>
    /// 为aot assembly加载原始metadata， 这个代码放aot或者热更新都行。
    /// 一旦加载后，如果AOT泛型函数对应native实现不存在，则自动替换为解释模式执行
    /// </summary>
    static void LoadMetadataForAOTAssemblies()
    {
        /// 注意，补充元数据是给AOT dll补充元数据，而不是给热更新dll补充元数据。
        /// 热更新dll不缺元数据，不需要补充，如果调用LoadMetadataForAOTAssembly会返回错误
        HomologousImageMode mode = HomologousImageMode.SuperSet;
        foreach (var aotDllName in AOTMetaAssemblyFiles)
        {
            byte[] dllBytes = ReadBytesFromStreamingAssets(aotDllName);
            // 加载assembly对应的dll，会自动为它hook。一旦aot泛型函数的native函数不存在，用解释器版本代码
            LoadImageErrorCode err = RuntimeApi.LoadMetadataForAOTAssembly(dllBytes, mode);
            Debug.Log($"LoadMetadataForAOTAssembly:{aotDllName}. mode:{mode} ret:{err}");
        }
    }

    void InitGame()
    {
        LoadMetadataForAOTAssemblies();
#if !UNITY_EDITOR
        _hotUpdateAss = Assembly.Load(ReadBytesFromStreamingAssets("HotUpdate.dll.bytes"));
#else
        _hotUpdateAss = System.AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "HotUpdate");
#endif
        Type entryType = _hotUpdateAss.GetType("GameManager");
        entryType.GetMethod("Init").Invoke(null, null);

        //Run_InstantiateComponentByAsset();
    }

    static void Run_InstantiateComponentByAsset()
    {
        // 通过实例化assetbundle中的资源，还原资源上的热更新脚本
        AssetBundle ab = AssetBundle.LoadFromMemory(ReadBytesFromStreamingAssets("prefabs"));
        GameObject cube = ab.LoadAsset<GameObject>("Cube");
        GameObject.Instantiate(cube);
    }
}