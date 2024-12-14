using UnityEngine;
using UnityEngine.UI;
using SFB;
using System.IO;
using UnityEngine.Networking;

public class Mp3Uploader : MonoBehaviour
{
    public AudioSource audioSource;
    public Button uploadButton;

    void Start()
    {
        uploadButton.onClick.AddListener(OpenFileExplorer);
    }

    void OpenFileExplorer()
    {
        string[] paths = StandaloneFileBrowser.OpenFilePanel("Select MP3 File", "", "mp3", false);
        if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
        {
            StartCoroutine(LoadAudio(paths[0]));
        }
    }

    System.Collections.IEnumerator LoadAudio(string path)
    {
        using (UnityEngine.Networking.UnityWebRequest www = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.MPEG))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(www);
                audioSource.clip = clip;
                audioSource.Play();
            }
            else
            {
                Debug.LogError("Failed to load audio: " + www.error);
            }
        }
    }
}
