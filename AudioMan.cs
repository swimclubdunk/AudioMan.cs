using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioMan : MonoBehaviour
{
    // A lightweight audio manager class, great for prototyping or handling all audio needs of a simple project.
    // While the focus lies on soundFx the class can easily be extended to support music needs (more fading and persistant dummy options)

    public static AudioMan Instance { get; private set; }

    GameObject dummy;   // Our stored dummy instance, for pooling needs
    readonly Queue<AudioSource> queue = new Queue<AudioSource>();    
    readonly Dictionary<string, AudioChannel> channels = new Dictionary<string, AudioChannel>();
    const int defaultMaxVoicesPerChannel = 8;

    // Store created coroutine yields, to minimise garbage allocation
    readonly Dictionary<float, WaitForSeconds> _wait = new Dictionary<float, WaitForSeconds>(100);
    readonly Dictionary<float, WaitForSecondsRealtime> _waitReal = new Dictionary<float, WaitForSecondsRealtime>(100);

    public class AudioChannel
    {
        public string name;
        public int maxEmitters;
        public int activeEmitters;

        // Constructor
        public AudioChannel(string name, int maxEmitters, int activeEmitters)
        {
            this.name = name;
            this.maxEmitters = maxEmitters;
            this.activeEmitters = activeEmitters;
        }
    }

    #region Pooling

    WaitForSeconds GetWaitForSeconds(float seconds)
    {
        if (!_wait.ContainsKey(seconds)) _wait.Add(seconds, new WaitForSeconds(seconds));
        return _wait[seconds];
    }
    WaitForSecondsRealtime GetWaitForSecondsRealTime(float seconds)
    {
        if (!_waitReal.ContainsKey(seconds)) _waitReal.Add(seconds, new WaitForSecondsRealtime(seconds));
        return _waitReal[seconds];
    }

    // Assembles the pooled dummy object and populates the queue with dummies/cloned instances
    void AssemblePool(int initialCount)
    {
        dummy = new GameObject("AudioDummy");
        dummy.AddComponent<AudioSource>();
        dummy.SetActive(false);
        dummy.transform.SetParent(transform);
        queue.Enqueue(dummy.GetComponent<AudioSource>());

        for (int j = 0; j < initialCount; j++)
        {
            var _dummy = Instantiate(dummy);
            _dummy.SetActive(false);
            _dummy.transform.SetParent(transform);
            queue.Enqueue(_dummy.GetComponent<AudioSource>());
        }
    }
    // Grows the pool by a batch of 5 instances
    void GrowPool()
    {
        for (int i = 0; i < 5; i++)
        {
            var _dummy = Instantiate(dummy);
            _dummy.SetActive(false);
            _dummy.transform.SetParent(transform);
            queue.Enqueue(_dummy.GetComponent<AudioSource>());
        }
    }
    // Retrieve a dummy instance from the pool
    AudioSource GetFromPool()
    {
        if (queue.Count == 0) GrowPool();
        AudioSource dummy = queue.Dequeue();
        dummy.transform.SetParent(null);
        dummy.gameObject.SetActive(true);
        return dummy;
    }

    #endregion

    // Declares singleton instanec and initialise object pool
    void Awake()
    {
        if (Instance != null)
            Destroy(gameObject);

        Instance = this;        
        AssemblePool(10);
    }

    // Creates a new channel with specified max simultaneously playing emitters
    public void SetChannel(string _name, int maxEmitters)
    {
        if (channels.ContainsKey(_name))
        {
            channels[_name].maxEmitters = maxEmitters;
            if (channels[_name].activeEmitters > maxEmitters)
                channels[_name].activeEmitters = maxEmitters;
        }   
        else
        {
            channels.Add(_name, new AudioChannel(_name, maxEmitters, 0));
        }
    }

    // Returns true if specified channel current emitter count is below its max emitter value, creates new channel using default values if channel does not exist
    bool ChannelIsAvailable(string channel)
    {
        if(!channels.ContainsKey(channel))
        {
            channels.Add(channel, new AudioChannel(channel, defaultMaxVoicesPerChannel, 0));
            return true;
        }

        if (channels[channel].activeEmitters < channels[channel].maxEmitters)
            return true;
        else
            return false;
    }

    // Request a non-spatialised sound to be played, takes a clip
    public void PlaySound2D(AudioClip sound, float volume = 0.5f, float pitch = 1f, float delay = 0f, bool realTimeDelay = false, string channel = null)
    {
        if(channel != null && !ChannelIsAvailable(channel))
            return;

        AudioSource audioSource = GetFromPool();
        audioSource.volume = volume;
        audioSource.pitch = pitch;
        audioSource.clip = sound;
        StartCoroutine(PlaySound(audioSource, delay, realTimeDelay, channel));
    }

    // Request a non-spatialised sound to be played, takes an array and extracts a random clip
    public void PlaySound2D(AudioClip[] sound, float volume = 0.5f, float pitch = 1f, float delay = 0f, bool realTimeDelay = false, string channel = null)
    {
        PlaySound2D(GetRandomClipOfArray(sound), volume, pitch, delay, realTimeDelay, channel);
    }

    // Request a spatialised sound to be played at position X, takes a clip
    public void PlaySound3D(Vector3 point, AudioClip sound, float volume, float pitch, float spatialBlend, Vector2 minMaxDistance, float delay = 0f, bool realTimeDelay = false, string channel = null)
    {
        if (channel != null && !ChannelIsAvailable(channel))
            return;

        AudioSource audioSource = GetFromPool();
        audioSource.transform.position = point;
        audioSource.volume = volume;
        audioSource.pitch = pitch;
        audioSource.clip = sound;
        audioSource.spatialBlend = spatialBlend;
        audioSource.minDistance = minMaxDistance.x;
        audioSource.maxDistance = minMaxDistance.y;

        StartCoroutine(PlaySound(audioSource, delay, realTimeDelay));
    }

    // Request a spatialised sound to be played at position X, takes an array and extracts a random clip
    public void Sound3D(Vector3 point, AudioClip[] sound, float volume, float pitch, float spatialBlend, Vector2 minMaxDistance, float delay = 0f, bool realTimeDelay = false, string channel = null)
    {
        PlaySound3D(point, GetRandomClipOfArray(sound), volume, pitch, spatialBlend, minMaxDistance, delay, realTimeDelay, channel);
    }

    // Coroutine which resolves the requested audio event
    IEnumerator PlaySound(AudioSource audioSource, float delay, bool realTimeDelay = false, string channel = null)
    {
        if (delay > 0f)
        {
            if (!realTimeDelay)
                yield return GetWaitForSeconds(delay);
            else
                yield return GetWaitForSecondsRealTime(delay);
        }

        audioSource.gameObject.SetActive(true);
        audioSource.PlayOneShot(audioSource.clip);

        // Register the emitter with the channels hashmap
        if (channel != null)
        {
            if (channels.ContainsKey(channel))
                channels[channel].activeEmitters += 1;
        }

        StartCoroutine(ReturnToPool(audioSource, delay, realTimeDelay, channel));
    }

    // Helper coroutine to fade volume over time
    public IEnumerator AudioSourceVolumeFade(AudioSource audioSource, float from, float to, float duration, float delay, System.Action onComplete = null, bool inRealTime = false)
    {
        if (delay > 0f)
        {
            if (!inRealTime)
                yield return GetWaitForSeconds(delay);
            else
                yield return GetWaitForSecondsRealTime(delay);
        }

        audioSource.volume = from;

        float elapsedTime = 0f;
        while (elapsedTime <= duration)
        {
            if (!inRealTime)
                elapsedTime += Time.deltaTime;
            else
                elapsedTime += Time.unscaledDeltaTime;

            audioSource.volume = Mathf.Lerp(from, to, elapsedTime / duration);
            yield return null;
        }
        audioSource.volume = to;
        onComplete?.Invoke();
    }

    // Returns the used dummy to the pool after concluding the audio clip
    IEnumerator ReturnToPool(AudioSource audioSource, float delay, bool realTimeDelay = false, string channel = null)
    {
        delay += audioSource.clip.length * audioSource.pitch + 0.1f;

        if (!realTimeDelay)
            yield return GetWaitForSeconds(delay);
        else
            yield return GetWaitForSecondsRealTime(delay);

        // De-register the emitter from the channels hashmap
        if (channel != null)
        {
            if (channels.ContainsKey(channel))
                channels[channel].activeEmitters -= 1;
        }

        // Return dummy instance to the pool
        audioSource.transform.SetParent(transform);
        audioSource.gameObject.SetActive(false);
        queue.Enqueue(audioSource);
    }

    AudioClip GetRandomClipOfArray(AudioClip[] array)
    {
        return array[Random.Range(0, array.Length)];
    }

    // For debug purposes, uncomment as needed.

    //void OnGUI()
    //{     
    //    int offY = 300;
    //    int offX = 100;
    //    int i = 0;

    //    GUI.Label(new Rect(offX, offY + 15 * -1, 400, 100), "CHANNELS:");
    //    foreach (var value in channels.Values)
    //    {            
    //        GUI.Label(new Rect(offX, offY + 15 * i, 400, 100), $"Channel: {value.name} --- Active emitters: " + value.activeEmitters + "/" + value.maxEmitters);                
    //        i++;
    //    }
    //}
}

