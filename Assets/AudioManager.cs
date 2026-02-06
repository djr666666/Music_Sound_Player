using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 简化的多轨道音频管理器 - 使用AssetDatabase和默认音量
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("基础配置")]
    [SerializeField] private int musicTrackCount = 4;
    [SerializeField] private float crossFadeDuration = 1.5f;
    [SerializeField] private int sfxPoolSize = 10;

    [Header("默认音量设置")]
    [SerializeField] private float masterVolume = 1f;      // 直接使用默认值
    [SerializeField] private float musicVolume = 0.7f;     // 直接使用默认值
    [SerializeField] private float sfxVolume = 0.8f;       // 直接使用默认值
    [SerializeField] private bool musicMuted = false;      // 直接使用默认值
    [SerializeField] private bool sfxMuted = false;        // 直接使用默认值

    // 动态加载的音频资源
    private Dictionary<string, AudioClip> musicClips = new Dictionary<string, AudioClip>();
    private Dictionary<string, AudioClip> sfxClips = new Dictionary<string, AudioClip>();

    // 音乐轨道系统
    private Dictionary<int, MusicTrack> musicTracks;

    // 音效对象池
    private Queue<AudioSource> sfxPool;
    private List<AudioSource> activeSFXSources;

    // 初始化标志
    private bool isInitialized = false;

    private class MusicTrack
    {
        public AudioSource source;
        public string currentClipName;
        public float targetVolume = 0f;
        public bool isPlaying = false;
        public int priority;
        public Coroutine fadeCoroutine;
        public float soundVolume = 1f;
    }

    #region 初始化方法

    public static AudioManager Init(bool destroyOnLoad = false)
    {
        if (Instance != null)
        {
            Debug.LogWarning("AudioManager已经初始化过了");
            return Instance;
        }

        GameObject audioManagerObject = new GameObject("AudioManager");
        AudioManager manager = audioManagerObject.AddComponent<AudioManager>();

        if (!destroyOnLoad)
        {
            DontDestroyOnLoad(audioManagerObject);
        }

        if (!manager.isInitialized)
        {
            manager.Initialize();
            manager.isInitialized = true;
        }

        return manager;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            if (!isInitialized)
            {
                Initialize();
                isInitialized = true;
            }
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void Initialize()
    {
        Debug.Log($"AudioManager初始化 - 主音量: {masterVolume}, 音乐音量: {musicVolume}");

        InitializeMusicTracks();
        InitializeSFXPool();

        Debug.Log($"AudioManager初始化完成");
    }

    private void InitializeMusicTracks()
    {
        musicTracks = new Dictionary<int, MusicTrack>();

        for (int i = 0; i < musicTrackCount; i++)
        {
            GameObject trackObject = new GameObject($"MusicTrack_{i}");
            trackObject.transform.SetParent(transform);

            AudioSource audioSource = trackObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.volume = 0f;

            MusicTrack track = new MusicTrack
            {
                source = audioSource,
                currentClipName = string.Empty,
                targetVolume = 0f,
                isPlaying = false,
                priority = i,
                fadeCoroutine = null,
                soundVolume = 1f
            };

            musicTracks.Add(i, track);
        }
    }

    private void InitializeSFXPool()
    {
        sfxPool = new Queue<AudioSource>();
        activeSFXSources = new List<AudioSource>();

        for (int i = 0; i < sfxPoolSize; i++)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            sfxPool.Enqueue(source);
        }
    }

    #endregion

    #region 播放方法 - 核心修复版本

    public void PlayMusicOnTrack(int trackId, string musicName, float volume = 1f, bool fadeIn = true,
        bool forceRestart = false, bool loop = true)
    {
        StartCoroutine(PlayMusicCoroutine(trackId, musicName, volume, fadeIn, forceRestart, loop));
    }

    private IEnumerator PlayMusicCoroutine(int trackId, string musicName, float volume, bool fadeIn,
        bool forceRestart, bool loop = true)
    {
        if (!musicTracks.ContainsKey(trackId))
        {
            Debug.LogError($"无效的音乐轨道ID: {trackId}");
            yield break;
        }

        if (string.IsNullOrEmpty(musicName))
        {
            Debug.LogWarning("音乐名称为空");
            yield break;
        }

        MusicTrack track = musicTracks[trackId];
        volume = Mathf.Clamp01(volume);

        // 如果已经在播放同一首音乐
        if (!forceRestart && track.isPlaying && track.currentClipName == musicName)
        {
            SetTrackVolume(trackId, volume, fadeIn);
            // 如果需要，可以在这里更新循环设置
            track.source.loop = loop;
            yield break;
        }

        // 1. 检查并加载音频资源
        if (!musicClips.ContainsKey(musicName))
        {
            // 使用AssetDatabase加载
            AudioClip loadedClip = LoadAudioClip(musicName, "music");
            if (loadedClip == null)
            {
                Debug.LogError($"音乐加载失败: {musicName}");
                yield break;
            }

            musicClips[musicName] = loadedClip;
            Debug.Log($"音乐加载成功: {musicName}");
        }

        // 2. 获取音频剪辑
        if (!musicClips.TryGetValue(musicName, out AudioClip clip) || clip == null)
        {
            Debug.LogError($"音乐剪辑获取失败: {musicName}");
            yield break;
        }

        // 3. 停止之前的淡入淡出
        if (track.fadeCoroutine != null)
        {
            StopCoroutine(track.fadeCoroutine);
            track.fadeCoroutine = null;
        }

        // 4. 更新轨道状态
        track.currentClipName = musicName;
        track.targetVolume = volume;
        track.isPlaying = true;
        track.soundVolume = 1f;
        track.source.clip = clip;

        // 5. 设置循环属性
        track.source.loop = loop;

        // 6. 计算并设置音量
        float calculatedVolume = CalculateFinalVolume(volume);

        if (!track.source.isPlaying)
        {
            track.source.Play();
        }

        if (fadeIn)
        {
            // 从当前音量开始淡入
            float startVolume = track.source.volume;
            //Debug.Log($"开始淡入: {startVolume} -> {track.targetVolume}");
            track.fadeCoroutine = StartCoroutine(FadeTrackVolume(track, startVolume, track.targetVolume));
        }
        else
        {
            // 直接设置音量
            track.source.volume = calculatedVolume;
            //Debug.Log($"直接设置音量: {calculatedVolume}");
        }

        Debug.Log($"音乐播放成功: {musicName}");
    }

    /// <summary>
    /// 播放音效（自动加载）
    /// </summary>
    public void PlaySFX(string name, float volumeScale = 1f, float pitch = 1f, Vector3? position = null)
    {
        StartCoroutine(PlaySFXCoroutine(name, volumeScale, pitch, position));
    }

    private IEnumerator PlaySFXCoroutine(string name, float volumeScale, float pitch, Vector3? position)
    {
        if (string.IsNullOrEmpty(name) || sfxMuted || volumeScale <= 0)
        {
            yield break;
        }

        // 检查并加载音频资源
        if (!sfxClips.ContainsKey(name))
        {
            Debug.Log($"音效 {name} 未加载，开始加载...");

            AudioClip loadedClip = LoadAudioClip(name, "sfx");
            if (loadedClip == null)
            {
                Debug.LogError($"音效加载失败: {name}");
                yield break;
            }

            sfxClips[name] = loadedClip;
            Debug.Log($"音效加载成功: {name}");
        }

        if (!sfxClips.TryGetValue(name, out AudioClip clip) || clip == null)
        {
            Debug.LogWarning($"音效未加载或不存在: {name}");
            yield break;
        }

        AudioSource source = GetAvailableSFXSource();
        if (source == null)
        {
            source = gameObject.AddComponent<AudioSource>();
            activeSFXSources.Add(source);
        }
        else
        {
            activeSFXSources.Add(source);
        }

        float finalVolume = sfxVolume * volumeScale;
        source.clip = clip;
        source.volume = finalVolume;
        source.pitch = pitch;
        source.loop = false;
        source.mute = sfxMuted;

        if (position.HasValue)
        {
            source.spatialBlend = 1f;
            source.transform.position = position.Value;
        }
        else
        {
            source.spatialBlend = 0f;
        }

        source.Play();
        StartCoroutine(ReturnToPoolAfterPlay(source, clip.length));
    }

    #endregion

    #region 音频加载方法

    /// <summary>
    /// 加载音频剪辑（使用AssetDatabase）
    /// </summary>
    private AudioClip LoadAudioClip(string audioName, string audioType = "music")
    {
#if UNITY_EDITOR
        try
        {
            // 构建搜索路径
            string[] searchFolders = new string[]
            {
                "Assets/Script/Test_09",
                "Assets/Audio",
                 "Assets/",
                $"Assets/Audio/{audioType.ToUpper()}",
                "Assets/Resources/Audio"
            };

            // 支持的音频格式
            string[] extensions = new string[] { ".mp3", ".wav", ".ogg", ".aiff" };

            // 在所有文件夹中搜索
            foreach (string folder in searchFolders)
            {
                if (!AssetDatabase.IsValidFolder(folder))
                    continue;

                // 搜索音频文件
                string[] guids = AssetDatabase.FindAssets($"{audioName} t:AudioClip", new[] { folder });

                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(path);

                    // 检查文件名是否匹配
                    if (string.Equals(fileName, audioName, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.Log($"找到音频文件: {path}");
                        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                        if (clip != null)
                        {
                            return clip;
                        }
                    }
                }
            }

            // 如果没找到，尝试直接加载
            string defaultPath = audioType == "music"
                ? $"Assets/Script/Test_09/{audioName}.mp3"
                : $"Assets/Script/Test_09/{audioName}.wav";

            if (System.IO.File.Exists(Application.dataPath + "/" + defaultPath.Substring(7)))
            {
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(defaultPath);
                if (clip != null)
                {
                    return clip;
                }
            }

            Debug.LogError($"未找到音频文件: {audioName}");
            return null;
        }
        catch (Exception e)
        {
            Debug.LogError($"加载音频时出错: {audioName}, 错误: {e.Message}");
            return null;
        }
#else
        Debug.LogError($"AssetDatabase只能在编辑器中使用");
        return null;
#endif
    }

    #endregion

    #region 音量计算方法

    /// <summary>
    /// 计算最终音量（简化版本）
    /// </summary>
    private float CalculateFinalVolume(float trackVolume)
    {
        // 如果静音，返回0
        if (musicMuted)
            return 0f;

        // 简单乘法：主音量 × 音乐音量 × 轨道音量
        float volume = masterVolume * musicVolume * trackVolume;

        // 确保在0-1范围内
        volume = Mathf.Clamp01(volume);

        return volume;
    }

    /// <summary>
    /// 设置轨道音量
    /// </summary>
    public void SetTrackVolume(int trackId, float volume, bool fade = true)
    {
        if (!musicTracks.ContainsKey(trackId)) return;

        MusicTrack track = musicTracks[trackId];
        if (!track.isPlaying) return;

        volume = Mathf.Clamp01(volume);
        track.targetVolume = volume;

        if (track.fadeCoroutine != null)
        {
            StopCoroutine(track.fadeCoroutine);
            track.fadeCoroutine = null;
        }

        if (fade)
        {
            track.fadeCoroutine = StartCoroutine(FadeTrackVolume(track, track.source.volume, track.targetVolume));
        }
        else
        {
            float finalVolume = CalculateFinalVolume(volume);
            track.source.volume = finalVolume;
        }
    }

    #endregion

    #region 辅助方法

    private IEnumerator FadeTrackVolume(MusicTrack track, float fromVolume, float toVolume, Action onComplete = null)
    {
        float duration = crossFadeDuration;
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / duration);

            float currentTargetVolume = Mathf.Lerp(fromVolume, toVolume, t);
            float finalVolume = CalculateFinalVolume(currentTargetVolume);

            track.source.volume = finalVolume;
            yield return null;
        }

        float finalTargetVolume = CalculateFinalVolume(toVolume);
        track.source.volume = finalTargetVolume;

        onComplete?.Invoke();
        track.fadeCoroutine = null;
    }

    private AudioSource GetAvailableSFXSource()
    {
        if (sfxPool.Count > 0)
        {
            return sfxPool.Dequeue();
        }

        AudioSource newSource = gameObject.AddComponent<AudioSource>();
        newSource.playOnAwake = false;
        return newSource;
    }

    private IEnumerator ReturnToPoolAfterPlay(AudioSource source, float duration)
    {
        float startTime = Time.time;

        while (Time.time - startTime < duration)
        {
            yield return null;

            // 检查音频是否已停止（可能被提前停止）
            if (source == null || !source.isPlaying)
            {
                if (source != null)
                {
                    ReturnToPool(source);
                }
                yield break;
            }
        }

        // 正常播放完毕
        if (source != null && source.isPlaying)
        {
            // 再次检查，确保完全播放完毕
            yield return null;
        }

        if (source != null)
        {
            ReturnToPool(source);
        }
    }

    private void ReturnToPool(AudioSource source)
    {
        if (source == null) return;

        if (activeSFXSources.Contains(source))
        {
            activeSFXSources.Remove(source);
            source.Stop();
            source.clip = null;
            source.pitch = 1f; // 重置音高
            source.panStereo = 0f; // 重置立体声平衡
            source.spatialBlend = 0f;
            source.reverbZoneMix = 1f;
            source.bypassEffects = false;
            source.bypassListenerEffects = false;
            source.bypassReverbZones = false;

            // 检查池是否过大
            if (activeSFXSources.Count + sfxPool.Count < sfxPoolSize * 2)
            {
                if (!sfxPool.Contains(source))
                {
                    sfxPool.Enqueue(source);
                }
            }
            else
            {
                // 池子过大，销毁多余的
                Destroy(source);
                Debug.LogWarning("音效池过大，销毁音频源");
            }
        }
    }

    #endregion

    #region 其他控制方法

    public void StopTrack(int trackId, bool fadeOut = true)
    {
        if (!musicTracks.ContainsKey(trackId)) return;
        MusicTrack track = musicTracks[trackId];
        if (!track.isPlaying) return;

        if (track.fadeCoroutine != null)
        {
            StopCoroutine(track.fadeCoroutine);
            track.fadeCoroutine = null;
        }

        if (fadeOut)
        {
            track.fadeCoroutine = StartCoroutine(FadeTrackVolume(track, track.source.volume, 0f, () =>
            {
                track.source.Stop();
                track.isPlaying = false;
                track.currentClipName = string.Empty;
            }));
        }
        else
        {
            track.source.Stop();
            track.source.volume = 0f;
            track.isPlaying = false;
            track.currentClipName = string.Empty;
        }
    }

    public void StopAllMusic(bool fadeOut = true)
    {
        foreach (int trackId in musicTracks.Keys)
        {
            StopTrack(trackId, fadeOut);
        }
    }

    public void StopAllSFX()
    {
        foreach (var source in activeSFXSources.ToArray())
        {
            source.Stop();
            ReturnToPool(source);
        }
    }

    #endregion

    #region 音量控制方法

    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);
        UpdateAllTrackVolumes();
    }

    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        UpdateAllTrackVolumes();
    }

    public void SetSFXVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
    }

    public void ToggleMusicMute()
    {
        musicMuted = !musicMuted;
        UpdateAllTrackVolumes();
    }

    public void ToggleSFXMute()
    {
        sfxMuted = !sfxMuted;
        foreach (var source in activeSFXSources)
        {
            source.mute = sfxMuted;
        }
    }

    private void UpdateAllTrackVolumes()
    {
        foreach (var track in musicTracks.Values)
        {
            if (track.isPlaying)
            {
                float finalVolume = CalculateFinalVolume(track.targetVolume);
                track.source.volume = finalVolume;
                track.source.mute = musicMuted;
            }
        }
    }

    /// <summary>
    /// 获取当前音量设置
    /// </summary>
    public void PrintVolumeSettings()
    {
        Debug.Log($"=== 音量设置 ===");
        Debug.Log($"主音量: {masterVolume}");
        Debug.Log($"音乐音量: {musicVolume}");
        Debug.Log($"音效音量: {sfxVolume}");
        Debug.Log($"音乐静音: {musicMuted}");
        Debug.Log($"音效静音: {sfxMuted}");
        Debug.Log($"===============");
    }

    /// <summary>
    /// 强制设置音量（用于调试）
    /// </summary>
    public void ForceSetVolume(float masterVol = 1f, float musicVol = 0.7f)
    {
        masterVolume = Mathf.Clamp01(masterVol);
        musicVolume = Mathf.Clamp01(musicVol);
        Debug.Log($"强制设置音量: 主音量={masterVolume}, 音乐音量={musicVolume}");
        UpdateAllTrackVolumes();
    }

    #endregion

    #region -------------------------------------------------------资源清理功能

    //清理未使用的音频资源
    public void CleanupUnusedAudioResources(bool forceUnload = false)
    {
        Debug.Log("开始清理音频资源...");

        int musicRemoved = 0;
        int sfxRemoved = 0;

        // 1. 清理音乐资源
        List<string> musicToRemove = new List<string>();
        foreach (var kvp in musicClips)
        {
            string musicName = kvp.Key;
            bool isUsed = false;

            // 检查是否有轨道正在使用这个音乐
            foreach (var track in musicTracks.Values)
            {
                if (track.currentClipName == musicName && track.isPlaying)
                {
                    isUsed = true;
                    break;
                }
            }

            if (!isUsed)
            {
                musicToRemove.Add(musicName);
            }
        }

        // 移除未使用的音乐
        foreach (string musicName in musicToRemove)
        {
            musicClips.Remove(musicName);
            musicRemoved++;
            Debug.Log($"清理音乐资源: {musicName}");
        }

        // 2. 清理音效资源
        List<string> sfxToRemove = new List<string>();
        foreach (var kvp in sfxClips)
        {
            string sfxName = kvp.Key;
            AudioClip clip = kvp.Value;
            bool isUsed = false;

            // 检查是否有活跃的音效源在使用这个音效
            foreach (var source in activeSFXSources)
            {
                if (source.clip == clip)
                {
                    isUsed = true;
                    break;
                }
            }

            // 检查是否在池中有音效源使用这个剪辑
            if (!isUsed)
            {
                foreach (var source in sfxPool)
                {
                    if (source.clip == clip)
                    {
                        isUsed = true;
                        break;
                    }
                }
            }

            if (!isUsed)
            {
                sfxToRemove.Add(sfxName);
            }
        }

        // 移除未使用的音效
        foreach (string sfxName in sfxToRemove)
        {
            sfxClips.Remove(sfxName);
            sfxRemoved++;
            Debug.Log($"清理音效资源: {sfxName}");
        }

        // 3. 强制卸载资源（可选）
        if (forceUnload && (musicRemoved > 0 || sfxRemoved > 0))
        {
            Resources.UnloadUnusedAssets();
            Debug.Log($"已调用 Resources.UnloadUnusedAssets()");
        }

        Debug.Log($"清理完成: 音乐 {musicRemoved} 个, 音效 {sfxRemoved} 个");
    }

    /// <summary>
    /// 清理所有音频资源（包括正在使用的）
    /// </summary>
    public void CleanupAllAudioResources()
    {
        Debug.LogWarning("清理所有音频资源...");

        // 1. 停止所有播放
        StopAllMusic(false);
        StopAllSFX();

        // 2. 清空缓存
        musicClips.Clear();
        sfxClips.Clear();

        // 3. 清空音效池
        foreach (var source in sfxPool)
        {
            if (source != null)
                Destroy(source);
        }
        sfxPool.Clear();
        activeSFXSources.Clear();

        // 4. 强制垃圾回收
        System.GC.Collect();
        Resources.UnloadUnusedAssets();

        Debug.Log("所有音频资源已清理");
    }

    /// <summary>
    /// 清理特定音频资源
    /// </summary>
    public void CleanupAudioResource(string audioName, bool isMusic = true)
    {
        if (isMusic)
        {
            if (musicClips.ContainsKey(audioName))
            {
                // 检查是否正在使用
                foreach (var track in musicTracks.Values)
                {
                    if (track.currentClipName == audioName && track.isPlaying)
                    {
                        Debug.LogWarning($"无法清理音乐 {audioName}，正在轨道 {track.priority} 播放中");
                        return;
                    }
                }

                musicClips.Remove(audioName);
                Debug.Log($"清理音乐资源: {audioName}");
            }
        }
        else
        {
            if (sfxClips.ContainsKey(audioName))
            {
                AudioClip clip = sfxClips[audioName];

                // 检查是否正在使用
                bool isUsed = false;
                foreach (var source in activeSFXSources)
                {
                    if (source.clip == clip && source.isPlaying)
                    {
                        isUsed = true;
                        break;
                    }
                }

                if (!isUsed)
                {
                    sfxClips.Remove(audioName);
                    Debug.Log($"清理音效资源: {audioName}");
                }
                else
                {
                    Debug.LogWarning($"无法清理音效 {audioName}，正在使用中");
                }
            }
        }
    }

    #endregion


    private void OnDestroy()
    {
        StopAllCoroutines();

        if (Instance == this)
        {
            Instance = null;
        }
    }
}