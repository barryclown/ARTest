using System;
using UnityEngine;

/// <summary>
/// 音效與背景音樂。其他腳本呼叫 GameAudio.Play(GameAudio.Sfx.Kill) 即可，場景裡沒有 GameAudio 時會安靜略過。
/// </summary>
public class GameAudio : MonoBehaviour
{
    public enum Sfx { Select, Kill, Hit, Appear, Tick, Win, Lose, Ui }

    public static GameAudio instance;

    /// <summary>每次要播音效時觸發（自動錄影用來重建音軌）</summary>
    public static event Action<Sfx> Played;
    /// <summary>背景音樂降低／恢復時觸發（自動錄影用）</summary>
    public static event Action<bool> BgmDucked;

    [Header("音效")]
    public AudioClip select;
    public AudioClip kill;
    public AudioClip hit;
    public AudioClip appear;
    public AudioClip tick;
    public AudioClip win;
    public AudioClip lose;
    public AudioClip ui;
    [Range(0f, 1f)] public float sfxVolume = 0.9f;

    [Header("背景音樂")]
    public AudioClip bgm;
    [Range(0f, 1f)] public float bgmVolume = 0.35f;
    [Tooltip("結算畫面時背景音樂降到原本的幾成")]
    [Range(0f, 1f)] public float bgmDuckOnResult = 0.4f;

    private AudioSource sfxSource;
    private AudioSource bgmSource;
    private float bgmTarget;

    private void Awake()
    {
        instance = this;

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;

        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.playOnAwake = false;
        bgmSource.loop = true;
        bgmSource.clip = bgm;
        bgmSource.volume = bgmTarget = bgmVolume;
    }

    private void Start()
    {
        if (bgm != null)
            bgmSource.Play();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        bgmSource.volume = Mathf.MoveTowards(bgmSource.volume, bgmTarget, Time.unscaledDeltaTime * 0.6f);
    }

    public static void Play(Sfx sfx)
    {
        Played?.Invoke(sfx);
        if (instance != null)
            instance.PlayClip(sfx);
    }

    /// <summary>結算畫面時把背景音樂壓低，開新的一局再恢復</summary>
    public static void DuckBgm(bool duck)
    {
        BgmDucked?.Invoke(duck);
        if (instance != null)
            instance.bgmTarget = instance.bgmVolume * (duck ? instance.bgmDuckOnResult : 1f);
    }

    private void PlayClip(Sfx sfx)
    {
        AudioClip clip;
        float volume = 1f;
        switch (sfx)
        {
            case Sfx.Select: clip = select; volume = 0.7f; break;
            case Sfx.Kill: clip = kill; break;
            case Sfx.Hit: clip = hit; break;
            case Sfx.Appear: clip = appear; break;
            case Sfx.Tick: clip = tick; volume = 0.6f; break;
            case Sfx.Win: clip = win; break;
            case Sfx.Lose: clip = lose; break;
            default: clip = ui; volume = 0.6f; break;
        }

        if (clip != null)
            sfxSource.PlayOneShot(clip, sfxVolume * volume);
    }
}
