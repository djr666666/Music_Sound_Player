using UnityEngine;
using UnityEngine.UI;

public class UseAudio : MonoBehaviour
{

    public Button Button;
    public Button Button_1;

    // Start is called before the first frame update
    void Start()
    {

        AudioManager.Init();


        Button.onClick.AddListener(() => {

            // 播放背景音乐（带淡入效果）
            // 轨道0：环境氛围音（音量30%）
            Debug.Log("Button.onClick  ");
            AudioManager.Instance.PlayMusicOnTrack(0, "bgm", 0.3f,true);
        });
        Button_1.onClick.AddListener(() => {

            AudioManager.Instance.PlaySFX("test", 0.3f);
        });


    }
}
