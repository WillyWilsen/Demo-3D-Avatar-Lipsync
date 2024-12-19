using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using LiveKit;
using LiveKit.Proto;
using RoomOptions = LiveKit.RoomOptions;
using System.Collections.Generic;

public class LiveKitMicrophone : MonoBehaviour
{
    private string token;
    public AudioSource audioSource;
    public AudioStream _audioStream;
    public Room room;
    Dictionary<string, GameObject> _audioObjects = new();

    [System.Serializable]
    public class TokenResponse
    {
        public string token;
    }

    IEnumerator Start()
    {
        UnityWebRequest www = UnityWebRequest.Get("http://localhost:5000/getToken");
        yield return www.SendWebRequest();
        
        if (www.result == UnityWebRequest.Result.Success)
        {
            string jsonResponse = www.downloadHandler.text;
            TokenResponse response = JsonUtility.FromJson<TokenResponse>(jsonResponse);
            token = response.token;
            
            Debug.Log("Room token fetched");
        }
        else
        {
            Debug.LogError("Error fetching token: " + www.error);
        }

        room = new Room();
        room.TrackSubscribed += TrackSubscribed;
        room.TrackUnsubscribed += UnTrackSubscribed;
        RoomOptions options = new RoomOptions();

        Debug.Log("Connecting to room...");
        var c = room.Connect("wss://voice-agent-claude-5sgklqv8.livekit.cloud", token, options);

        yield return c;

        if (!c.IsError)
        {
            Debug.Log("Connected to room.");

            // Create and publish the microphone track
            StartCoroutine(PublishMicrophoneTrack());
        }
    }

    public IEnumerator PublishMicrophoneTrack()
    {
        Debug.Log("publicMicrophone!");
        // Publish Microphone
        var localSid = "my-audio-source";
        GameObject audObject = new GameObject(localSid);
        var source = audObject.AddComponent<AudioSource>();
        source.clip = Microphone.Start(Microphone.devices[0], true, 2, (int)RtcAudioSource.DefaultMirophoneSampleRate);
        source.loop = true;

        _audioObjects[localSid] = audObject;

        var rtcSource = new RtcAudioSource(source);
        Debug.Log("CreateAudioTrack!");
        var track = LocalAudioTrack.CreateAudioTrack("my-audio-track", rtcSource, room);

        var trackOptions = new TrackPublishOptions();
        trackOptions.AudioEncoding = new AudioEncoding();
        trackOptions.AudioEncoding.MaxBitrate = 64000;
        trackOptions.Source = TrackSource.SourceMicrophone;

        Debug.Log("PublishTrack!");
        var publish = room.LocalParticipant.PublishTrack(track, trackOptions);
        yield return publish;

        if (!publish.IsError)
        {
            Debug.Log("Track published!");
        }

        rtcSource.Start();
    }

    void TrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        if (track is RemoteAudioTrack audioTrack)
        {
            GameObject audObject = new GameObject(audioTrack.Sid);
            var source = audObject.AddComponent<AudioSource>();
            _audioStream = new AudioStream(audioTrack, source, audioSource);
            _audioObjects[audioTrack.Sid] = audObject;
        }
    }

    void UnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        if (track is RemoteAudioTrack audioTrack)
        {
            var audObject = _audioObjects[audioTrack.Sid];
            if (audObject != null)
            {
                var source = audObject.GetComponent<AudioSource>();
                source.Stop();
                Destroy(audObject);
            }
            _audioObjects.Remove(audioTrack.Sid);
        }
    }

    void Update()
    {
        if (_audioStream != null) {
            _audioStream.Update();
        }
    }
}
