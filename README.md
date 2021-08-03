# deep-speech-unity
A Unity implementation of [DeepSpeech](https://github.com/mozilla/DeepSpeech) is an open source embedded (offline, on-device) speech-to-text engine which can run in real time on devices. I was inspired to create this after seeing a different implementation by [@voxell-tech](https://github.com/voxell-tech/UnityASR/tree/deepspeech)

The sample scene includes two versions of voice recognition processing. 
- The `ContinuousVoiceRecorder` script feeds the audio into DeepSpeech realtime and processess the intermediate result.
- The `SpeechTextToText` detects the users voice and processes the audio after the user stops talking. 

Both examples run offline and can auto detect if the user is speaking using a volume threshold.

## Developed using
- Windows 64
- Unity 2020.3.12

## Note
This demo only runs on Windows 64 however, DeepSpeech does support other platforms and functionality can be expanded. Pull Requests are welcomed.
