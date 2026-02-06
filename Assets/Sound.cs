using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Sound
{
    public string name;         // 音频剪辑的名称
    public AudioClip clip;      // 音频剪辑
    [Range(0f, 1f)]
    public float volume = 0.7f; // 音量大小

    [Range(0.5f, 2f)]
    public float pitch = 1f;    // 音高

    [Tooltip("音频优先级（0-256），0最高")]
    [Range(0, 256)]
    public int priority = 128;  // Unity AudioSource优先级

    [Tooltip("是否预加载")]
    public bool preload = true; // 是否在初始化时预加载

}

