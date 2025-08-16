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
            offline:
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

            case EPlayMode.CustomPlayMode:
                // �Զ���ƫ����������ģʽ�����������������ɸ���
                IRemoteServices customServices = new RemoteServices(defaultHostServer, fallbackHostServer);

                var cutominitParameters = new HostPlayModeParameters();
                cutominitParameters.BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
                cutominitParameters.CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(customServices);
                initOperation = package.InitializeAsync(cutominitParameters); yield return initOperation;

                if (initOperation.Status == EOperationStatus.Succeed) goto hotUpdate;
                else { Debug.Log("��������ʧ�ܣ����õ���ģʽ"); goto offline; }
                break;
        }

        // �ȴ���ʼ�����
        yield return initOperation; Debug.Log("��ʼ�������" + initOperation.Status);
        if (initOperation.Status != EOperationStatus.Succeed)
        {
            Debug.LogError($"��ʼ��ʧ��: {initOperation.Error}");
            yield break;
        }
    hotUpdate:
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
        else { Debug.LogError("��ȡ��Դ�汾ʧ�ܣ�" + requestOperation.Error); }
        // ������Դ�嵥
        yield return package.UpdatePackageManifestAsync(packageVersion);
        // ��ʼ����ȱʧ��Դ
        yield return DownloadAssets();
    }

    /// <summary>
    /// ����ȱʧ��Զ����Դ
    /// </summary>
    IEnumerator DownloadAssets()
    {
        var downloader = package.CreateResourceDownloader(10, 3); //��󲢷�������������ʧ�����Դ���

        if (downloader.TotalDownloadCount == 0)
        {
            Debug.Log("û����Ҫ���ص���Դ");
            yield return InitScripts(); // ֱ�ӽ�����Ϸ
        }
        else 
        { 
            // ע��ص�����
            downloader.DownloadFinishCallback = OnDownloadFinishFunction; 
            downloader.DownloadErrorCallback = OnDownloadErrorFunction;
            downloader.DownloadUpdateCallback = OnDownloadUpdateFunction; 
            downloader.DownloadFileBeginCallback = OnDownloadFileBeginFunction;
            // ��ʼ����
            downloader.BeginDownload(); yield return downloader;
            if (downloader.Status == EOperationStatus.Succeed) { Debug.Log("��Դ���سɹ�"); yield return InitScripts(); }
            else Debug.LogError("��Դ����ʧ��");
        } 
    }

    void OnDownloadUpdateFunction(DownloadUpdateData data)
    {
        float progress = (float)data.CurrentDownloadBytes / (float)data.TotalDownloadBytes;
        Debug.Log($"�ܴ�С: {data.TotalDownloadBytes / 1024.0f / 1024} MB | ������: {data.CurrentDownloadBytes / 1024.0f / 1024} MB | ����: {progress * 100:F2}%");
    }
    void OnDownloadFileBeginFunction(DownloadFileData data) { Debug.Log($"��ʼ�����ļ���{data.FileName}����С��{data.FileSize / 1024.0f} KB"); }
    void OnDownloadErrorFunction(DownloadErrorData data) { Debug.LogError("���ش���"); }
    void OnDownloadFinishFunction(DownloaderFinishData data) { Debug.Log("�������"); }

    IEnumerator InitScripts()
    {
        foreach (var asset in assets)
        {
            string dllPath = $"file://{Application.streamingAssetsPath}/hybridclr/{asset}";
            Debug.Log($"start DownloadAssets asset:{dllPath}");
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

        HomologousImageMode mode = HomologousImageMode.SuperSet;
        foreach (var aotDllName in assets)
        {
            byte[] dllBytes = s_assetDatas[aotDllName];
            // ����assembly��Ӧ��dll�����Զ�Ϊ��hook��һ��aot���ͺ�����native���������ڣ��ý������汾����
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
        Debug.Log("׼��������Ϸ����: " + loadSceneName);
        yield return new WaitForSeconds(5f); yield return package.LoadSceneAsync(loadSceneName);
    }
}