using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DeepSpeechClient;
using DeepSpeechClient.Interfaces;
using DeepSpeechClient.Models;
using UnityEngine;

public class SpeechTextToText : MonoBehaviour
{
    #region Calculation Variables

    private int _micPrevPos = 0;
    private float _timeAtSilenceBegan = 0.0f;
    private float[] _samples;
    private bool _audioDetected = false;
    private bool _requestNeedsSending = false;
    private bool _currentlyRecording = false;
    private AudioClip _audioRecording = null;

    #endregion

    protected bool _detectPhrases;
    protected bool _microphoneActive = true;
    protected bool _canRecord = false;
    protected bool _isSetup = false;

    [Header("Voice Input Settings")]
    [SerializeField,
     Tooltip( "If true, voice input will be automatically detected, otherwise hold down bumper to speak and release bumper to send for conversion")]
    protected bool _autoDetectVoice = true;

    [SerializeField, Tooltip("Maximum length of recording in seconds.")]
    protected int _maxRecordingLength = 5;

    [SerializeField, Tooltip("Time in seconds of detected silence before voice request is sent")]
    protected float _silenceTimer = 1.0f;

    [SerializeField, Tooltip("The minimum volume to detect voice input for"), Range(0.0f, 1.0f)]
    protected float _minimumSpeakingSampleValue = 0.05f;


    private bool didTransmit;
    private ConcurrentQueue<short[]> _threadedBufferQueue = new ConcurrentQueue<short[]>();
    private int _threadSafeBoolBackValue = 0;

    private IDeepSpeech _sttClient;

    /// <summary>
    /// Stream used to feed data into the acoustic model.
    /// </summary>
    private DeepSpeechStream _sttStream;

    [Header("Deep Speech")] public string modelPath;
    public string externalScorerPath;

    public string transcription;

    public bool StreamingIsBusy
    {
        get => (Interlocked.CompareExchange(ref _threadSafeBoolBackValue, 1, 1) == 1);
        set
        {
            if (value) Interlocked.CompareExchange(ref _threadSafeBoolBackValue, 1, 0);
            else Interlocked.CompareExchange(ref _threadSafeBoolBackValue, 0, 1);
        }
    }

    #region Unity Methods

    private void Start()
    {
        _canRecord = true;

        if (_canRecord)
        {
            SetupService(_detectPhrases, _autoDetectVoice);
        }
    }

    private void FixedUpdate()
    {
        if (_canRecord && _isSetup == false)
            SetupService(_detectPhrases, _autoDetectVoice);

        if (_autoDetectVoice == true && _microphoneActive == true)
        {
            DetectAudio();
        }
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause == true)
        {
            StopMicrophoneCapture(false);
        }
        else if (_autoDetectVoice == true) // Returning from pause and auto detect is on
        {
            StartMicrophoneCapture();
        }
    }

    #endregion

    #region Virtual Methods

    public void SetupService(bool detectPhrases, bool autoDetectVoice)
    {
        _sttClient = new DeepSpeech(modelPath);
        _sttClient.EnableExternalScorer(externalScorerPath);

        //If you want to add bias to a word. Only supports single words.
        //_sttClient.AddHotWord("select",20.0f);

        //You can also add a negative value
        //_sttClient.AddHotWord("cue",-250.0f);

        _detectPhrases = detectPhrases;
        _autoDetectVoice = autoDetectVoice;
        if (_canRecord && autoDetectVoice)
        {
            _isSetup = true;
            ToggleActivelyRecording(true);
        }
    }

    #endregion

    #region Google Methods

    private void SendToDeepSpeech()
    {
        if (_autoDetectVoice == false) // _samples has already been populated in DetectAudio if auto detection is used
        {
            FillSamples(0);
        }

        // _samples are in range [-1.0f, 1.0f];
        short shortSample;
        short[] shorts = new short[_samples.Length];

        float rescaleFactor = 32767; // to put float values in range of [-32767, 32767] for correct conversion
        for (int i = 0; i < _samples.Length; i++)
        {
            shortSample = (short) (_samples[i] * rescaleFactor); // convert to short
            shorts[i] = shortSample;
        }

        if (didTransmit == false)
        {
            _sttStream = _sttClient.CreateStream();
            didTransmit = true;
        }

        _threadedBufferQueue.Enqueue(shorts);
        if (StreamingIsBusy == false)
        {
            Task.Run(ThreadedWork).ConfigureAwait(false);
        }
    }

    private async void ThreadedWork()
    {
        StreamingIsBusy = true;
        while (_threadedBufferQueue.Count > 0)
        {
            if (_threadedBufferQueue.TryDequeue(out short[] voiceResult))
            {
                var output = _sttClient.SpeechToText(voiceResult, Convert.ToUInt32(voiceResult.Length));
                await Task.Delay(10);
                transcription = output;
                Debug.Log("Result: " + output);
            }
        }

        StreamingIsBusy = false;
    }

    #endregion

    #region Microphone / Voice Input Helpers

    public void ToggleActivelyRecording(bool enable)
    {
        if (_canRecord == false)
            return;

        if (enable)
        {
            StartMicrophoneCapture();
        }
        else
        {
            StopMicrophoneCapture();
        }
    }

    private void StartMicrophoneCapture()
    {
        if (_canRecord && _currentlyRecording == false)
        {
            if (didTransmit == false)
            {
                _sttStream = _sttClient.CreateStream();
                didTransmit = true;
            }

            _microphoneActive = true;
            _audioRecording = Microphone.Start(Microphone.devices[0], true, _maxRecordingLength,
                _sttClient.GetModelSampleRate());
            _currentlyRecording = true;
        }
    }

    private void StopMicrophoneCapture(bool sendLastRequest = true)
    {
        Microphone.End(Microphone.devices[0]);
        if (didTransmit && StreamingIsBusy == false)
        {
            didTransmit = false;
            _sttClient.FreeStream(_sttStream);
        }

        _currentlyRecording = false;
        if (sendLastRequest == true)
        {
            SendToDeepSpeech();
        }
    }

    /* Used to determine when t he user has started and stopped speaking */
    private void DetectAudio()
    {
        FillSamples(_micPrevPos);

        // Determine if the microphone noise levels have been loud enough
        float maxVolume = 0.0f;
        for (int i = _micPrevPos + 1; i < Microphone.GetPosition(Microphone.devices[0]); ++i)
        {
            if (i >= _samples.Length)
                Debug.LogError("WAS " + i + "in length: " + _samples.Length);
            if (_samples[i] > maxVolume)
            {
                maxVolume = _samples[i];
            }
        }


        if (maxVolume > _minimumSpeakingSampleValue)
        {
            if (_audioDetected == false) // User first starts talking after a gap
            {
                _audioDetected = true;
                _requestNeedsSending = true;
                Debug.Log("User started talking");
            }
        }
        else // max volume below threshold
        {
            if (_audioDetected == true) // User first stopped talking after talking
            {
                _timeAtSilenceBegan = Time.time;
                _audioDetected = false;
                Debug.Log("User stopped talking");
            }
            else if (_requestNeedsSending == true) // while no new voice input is detected
            {
                if (Time.time - _timeAtSilenceBegan > _silenceTimer)
                {
                    Debug.Log("Sending audio to recognizer...");

                    _audioDetected = false;
                    _requestNeedsSending = false;
                    SendToDeepSpeech();
                    ClearSamples();
                    if (didTransmit && StreamingIsBusy == false)
                    {
                        didTransmit = false;
                        _sttClient.FreeStream(_sttStream);
                    }
                }
            }
        }

        _micPrevPos = Microphone.GetPosition(Microphone.devices[0]);
    }


    void FillSamples(int micPosition)
    {
        _samples = new float[_audioRecording.samples]; // make a float array to hold the samples
        _audioRecording.GetData(_samples, micPosition); // Fill that array (values [-1.0f -> 1.0]
    }

    void ClearSamples()
    {
        for (int i = 0; i < _samples.Length; ++i)
        {
            _samples[i] = 0.0f;
        }

        _audioRecording.SetData(_samples, 0);
    }

    #endregion
}