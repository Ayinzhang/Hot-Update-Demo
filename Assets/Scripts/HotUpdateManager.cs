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
                // �༭��ģʽ��ʹ��ģ�⹹������ԴĿ¼
                var buildResult = EditorSimulateModeHelper.SimulateBuild("DefaultPackage");
                var packageRoot = buildResult.PackageRootDirectory;
                var editorFileSystemParams = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot);
                var initParameters = new EditorSimulateModeParameters();
                initParameters.EditorFileSystemParameters = editorFileSystemParams;
                initOperation = package.InitializeAsync(initParameters);
                break;

            case EPlayMode.OfflinePlayMode:
                // ����ģʽ��ʹ��������Դ��APK �ڲ��� StreamingAssets��
                var buildinFileSystemParams = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
                var offinitParameters = new OfflinePlayModeParameters();
                offinitParameters.BuildinFileSystemParameters = buildinFileSystemParams;
                initOperation = package.InitializeAsync(offinitParameters);
                break;

            case EPlayMode.HostPlayMode:
                // ����ģʽ����Զ�̷�����������Դ���������ڱ���
                IRemoteServices remoteServices = new RemoteServices(defaultHostServer, fallbackHostServer);
                var cacheFileSystemParams = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices);
                var hostbuildinFileSystemParams = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();

                var hostinitParameters = new HostPlayModeParameters();
                hostinitParameters.BuildinFileSystemParameters = hostbuildinFileSystemParams;
                hostinitParameters.CacheFileSystemParameters = cacheFileSystemParams;
                initOperation = package.InitializeAsync(hostinitParameters);
                break;

            case EPlayMode.WebPlayMode:
                // Web ģʽ�������� WebGL ƽ̨
                IRemoteServices webremoteServices = new RemoteServices(defaultHostServer, fallbackHostServer);
                var webServerFileSystemParams = FileSystemParameters.CreateDefaultWebServerFileSystemParameters();
                var webRemoteFileSystemParams = FileSystemParameters.CreateDefaultWebRemoteFileSystemParameters(webremoteServices); //֧�ֿ�������

                var webinitParameters = new WebPlayModeParameters();
                webinitParameters.WebServerFileSystemParameters = webServerFileSystemParams;
                webinitParameters.WebRemoteFileSystemParameters = webRemoteFileSystemParams;

                initOperation = package.InitializeAsync(webinitParameters);
                break;
        }

        // �ȴ���ʼ�����
        yield return initOperation;

        Debug.Log("��ʼ�������" + initOperation.Status);
        if (initOperation.Status != EOperationStatus.Succeed)
        {
            Debug.LogError($"��ʼ��ʧ��: {initOperation.Error}");
            yield break;
        }

        // �������µ���Դ�汾��Ϣ
        var requestOperation = package.RequestPackageVersionAsync();
        Debug.Log("�����������µ���Դ�汾...");
        yield return requestOperation;

        string packageVersion = "";
        if (requestOperation.Status == EOperationStatus.Succeed)
        {
            packageVersion = requestOperation.PackageVersion;
            Debug.Log($"��ȡ������Դ�汾�ɹ�: {packageVersion}");
        }
        else
        {
            Debug.LogError("��ȡ��Դ�汾ʧ�ܣ�" + requestOperation.Error);
        }

        // ������Դ�嵥
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
                    Debug.LogError("��Դ����ʧ�ܣ�");
                }
            };
            Debug.Log("��Դ�嵥���³ɹ�");
        }
        else Debug.LogError("��Դ�嵥����ʧ�ܣ�" + updateOperation.Error);

        // ��ʼ����ȱʧ��Դ
        yield return Download();
    }

    /// <summary>
    /// ����ȱʧ��Զ����Դ
    /// </summary>
    IEnumerator Download()
    {
        int downloadingMaxNum = 10;   // ��󲢷�������
        int failedTryAgain = 3;       // ����ʧ�����Դ���

        var downloader = package.CreateResourceDownloader(downloadingMaxNum, failedTryAgain);

        if (downloader.TotalDownloadCount == 0)
        {
            Debug.Log("û����Ҫ���ص���Դ");
            yield return EnterGame(); // ֱ�ӽ�����Ϸ
        }

        // ע��ص�����
        downloader.DownloadFinishCallback = OnDownloadFinishFunction;
        downloader.DownloadErrorCallback = OnDownloadErrorFunction;
        downloader.DownloadUpdateCallback = OnDownloadUpdateFunction;
        downloader.DownloadFileBeginCallback = OnDownloadFileBeginFunction;

        // ��ʼ����
        downloader.BeginDownload();
        yield return downloader;

        if (downloader.Status == EOperationStatus.Succeed)
        {
            Debug.Log("��Դ���سɹ�");
            yield return EnterGame();
        }
        else
        {
            Debug.LogError("��Դ����ʧ��");
        }
    }

    /// <summary>
    /// ������Ϸ����
    /// </summary>
    IEnumerator EnterGame()
    {
        SceneHandle handle = package.LoadSceneAsync(loadSceneName);
        yield return handle;
    }

    #region ���ػص�����

    /// <summary>
    /// ���ؽ��ȸ��»ص�
    /// </summary>
    void OnDownloadUpdateFunction(DownloadUpdateData data)
    {
        float progress = (float)data.CurrentDownloadBytes / (float)data.TotalDownloadBytes;
        Debug.Log($"�ܴ�С: {data.TotalDownloadBytes / 1024.0f / 1024} MB | ������: {data.CurrentDownloadBytes / 1024.0f / 1024} MB | ����: {progress * 100:F2}%");
    }

    /// <summary>
    /// ��ʼ����ĳ���ļ�ʱ����
    /// </summary>
    void OnDownloadFileBeginFunction(DownloadFileData data)
    {
        Debug.Log($"��ʼ�����ļ���{data.FileName}����С��{data.FileSize / 1024.0f} KB");
    }

    /// <summary>
    /// ���ش���ʱ����
    /// </summary>
    void OnDownloadErrorFunction(DownloadErrorData data)
    {
        Debug.LogError("���ش���");
    }

    /// <summary>
    /// ���ؽ���ʱ���������۳ɹ���ʧ�ܣ�
    /// </summary>
    void OnDownloadFinishFunction(DownloaderFinishData data)
    {
        Debug.Log("�������");
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
    /// Ϊaot assembly����ԭʼmetadata�� ��������aot�����ȸ��¶��С�
    /// һ�����غ����AOT���ͺ�����Ӧnativeʵ�ֲ����ڣ����Զ��滻Ϊ����ģʽִ��
    /// </summary>
    static void LoadMetadataForAOTAssemblies()
    {
        /// ע�⣬����Ԫ�����Ǹ�AOT dll����Ԫ���ݣ������Ǹ��ȸ���dll����Ԫ���ݡ�
        /// �ȸ���dll��ȱԪ���ݣ�����Ҫ���䣬�������LoadMetadataForAOTAssembly�᷵�ش���
        HomologousImageMode mode = HomologousImageMode.SuperSet;
        foreach (var aotDllName in AOTMetaAssemblyFiles)
        {
            byte[] dllBytes = ReadBytesFromStreamingAssets(aotDllName);
            // ����assembly��Ӧ��dll�����Զ�Ϊ��hook��һ��aot���ͺ�����native���������ڣ��ý������汾����
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
        // ͨ��ʵ����assetbundle�е���Դ����ԭ��Դ�ϵ��ȸ��½ű�
        AssetBundle ab = AssetBundle.LoadFromMemory(ReadBytesFromStreamingAssets("prefabs"));
        GameObject cube = ab.LoadAsset<GameObject>("Cube");
        GameObject.Instantiate(cube);
    }
}