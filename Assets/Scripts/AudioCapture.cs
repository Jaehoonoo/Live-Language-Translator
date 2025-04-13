using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Android;

public class AudioDetectionRecorder : MonoBehaviour
{
    [Header("Audio Detection Settings")]
    [Tooltip("Volume threshold to trigger recording (0-1)")]
    [Range(0.001f, 0.1f)]
    public float detectionThreshold = 0.01f;

    [Tooltip("Seconds of silence needed to stop recording")]
    [Range(0.5f, 5f)]
    public float silenceLimit = 2.0f;

    [Tooltip("Seconds of audio to keep before detection starts")]
    [Range(0f, 2f)]
    public float prevAudioSeconds = 0.5f;

    [Header("Recording Settings")]
    [Tooltip("Sample rate for audio recording")]
    public int sampleRate = 16000;

    // Internal variables for continuous recording
    private AudioClip microphoneClip;
    private bool isMonitoring = false;
    private bool isRecording = false;
    private float silenceTimer = 0f;

    // For sample‑accurate reading
    private int lastSamplePos = 0;

    // List to accumulate recording data
    private List<float> recordedSamples = new List<float>();

    // Pre‑buffer implemented as a fixed‑size queue
    private Queue<float> preBufferQueue = new Queue<float>();
    private int preBufferSize; // number of samples for prevAudioSeconds

    private void Start()
    {
        // Request microphone permission on Android (if required)
        if (Application.platform == RuntimePlatform.Android)
        {
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);
            }
        }

        // Calculate how many samples to keep for the pre‑buffer.
        preBufferSize = Mathf.RoundToInt(prevAudioSeconds * sampleRate);

        StartMonitoring();
    }

    public void StartMonitoring()
    {
        if (isMonitoring)
            return;

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("No microphone device found!");
            return;
        }

        // Start a looping recording (10 seconds long is arbitrary, adjust as needed)
        microphoneClip = Microphone.Start(null, true, 10, sampleRate);
        isMonitoring = true;
        lastSamplePos = 0;

        // Start our continuous reading coroutine.
        StartCoroutine(ProcessMicrophoneData());
        Debug.Log("Started audio monitoring.");
    }

    public void StopMonitoring()
    {
        if (!isMonitoring)
            return;

        StopAllCoroutines();
        Microphone.End(null);
        isMonitoring = false;
        isRecording = false;
        Debug.Log("Stopped audio monitoring.");
    }

    private IEnumerator ProcessMicrophoneData()
    {
        // Wait until the microphone is running.
        while (Microphone.GetPosition(null) <= 0)
            yield return null;

        while (isMonitoring)
        {
            int currentPos = Microphone.GetPosition(null);
            List<float> newSamples = new List<float>();

            // Handle wrap-around if needed:
            if (currentPos < lastSamplePos)
            {
                // Read samples from lastSamplePos to the end of the clip...
                int samplesToEnd = microphoneClip.samples - lastSamplePos;
                if (samplesToEnd > 0)
                {
                    float[] data = new float[samplesToEnd];
                    microphoneClip.GetData(data, lastSamplePos);
                    newSamples.AddRange(data);
                }
                // ... then from the beginning of the clip to currentPos.
                if (currentPos > 0)
                {
                    float[] data = new float[currentPos];
                    microphoneClip.GetData(data, 0);
                    newSamples.AddRange(data);
                }
            }
            else
            {
                int newCount = currentPos - lastSamplePos;
                if (newCount > 0)
                {
                    float[] data = new float[newCount];
                    microphoneClip.GetData(data, lastSamplePos);
                    newSamples.AddRange(data);
                }
            }

            lastSamplePos = currentPos;

            // If there are no new samples, wait a frame.
            if (newSamples.Count == 0)
            {
                yield return null;
                continue;
            }

            // Add new samples to the pre-buffer queue (and maintain its fixed size).
            foreach (float s in newSamples)
            {
                preBufferQueue.Enqueue(s);
                if (preBufferQueue.Count > preBufferSize)
                    preBufferQueue.Dequeue();
            }

            // Compute the RMS value of these new samples.
            float sumSq = 0f;
            foreach (float s in newSamples)
            {
                sumSq += s * s;
            }
            float rms = Mathf.Sqrt(sumSq / newSamples.Count);

            // Detection logic:
            if (!isRecording && rms > detectionThreshold)
            {
                // When audio above threshold is detected, start recording.
                isRecording = true;
                silenceTimer = 0f;

                // Prepend the pre-buffer to capture audio preceding the detection.
                recordedSamples.AddRange(preBufferQueue);
                recordedSamples.AddRange(newSamples);
                Debug.Log("Audio detected. Starting recording.");
            }
            else if (isRecording)
            {
                // Continue recording by adding new samples.
                recordedSamples.AddRange(newSamples);

                // If the RMS falls below threshold, accumulate silence time.
                if (rms < detectionThreshold)
                {
                    silenceTimer += newSamples.Count / (float)sampleRate;
                    if (silenceTimer >= silenceLimit)
                    {
                        // Stop recording after enough silence.
                        isRecording = false;
                        Debug.Log("Silence detected. Finalizing recording...");
                        SendRecordingToTranslator();
                        recordedSamples.Clear();
                    }
                }
                else
                {
                    silenceTimer = 0f;
                }
            }

            yield return null;
        }
    }

    /// <summary>
    /// Converts the recorded samples into an AudioClip, generates a WAV byte array in memory,
    /// and sends it directly to the OpenAIWhisperTranslator.
    /// </summary>
    private void SendRecordingToTranslator()
    {
        // Create an AudioClip from the recorded sample data.
        AudioClip recordedClip = AudioClip.Create("RecordedAudio", recordedSamples.Count, 1, sampleRate, false);
        recordedClip.SetData(recordedSamples.ToArray(), 0);

        // Generate a filename with a timestamp (for reference).
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filename = $"Recording_{timestamp}.wav";

        // Convert the AudioClip to a WAV byte array using our helper.
        byte[] wavData = SavWav.GetWavData(recordedClip);

        // Find the translator in the scene.
        OpenAIWhisperTranslator translator = UnityEngine.Object.FindFirstObjectByType<OpenAIWhisperTranslator>();


        if (translator != null)
        {
            translator.TranslateAudioData(wavData, filename);
        }
        else
        {
            Debug.LogError("OpenAIWhisperTranslator not found in scene.");
        }
    }

    private void OnDestroy()
    {
        StopMonitoring();
    }

    // Embedded WAV saving helper. We've added a method to generate the WAV data as a byte array.
    public static class SavWav
    {
        const int HEADER_SIZE = 44;

        /// <summary>
        /// Generates a WAV byte array from an AudioClip.
        /// </summary>
        public static byte[] GetWavData(AudioClip clip)
        {
            // Get audio samples.
            float[] samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);
            short[] intData = new short[samples.Length];
            byte[] bytesData = new byte[samples.Length * 2];

            for (int i = 0; i < samples.Length; i++)
            {
                intData[i] = (short)(samples[i] * 32767);
            }
            Buffer.BlockCopy(intData, 0, bytesData, 0, bytesData.Length);

            // Create header.
            byte[] header = GetWavHeader(clip, bytesData.Length);
            byte[] wavData = new byte[header.Length + bytesData.Length];
            Buffer.BlockCopy(header, 0, wavData, 0, header.Length);
            Buffer.BlockCopy(bytesData, 0, wavData, header.Length, bytesData.Length);

            return wavData;
        }

        private static byte[] GetWavHeader(AudioClip clip, int dataLength)
        {
            int hz = clip.frequency;
            int channels = clip.channels;

            MemoryStream stream = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                // RIFF header
                writer.Write(System.Text.Encoding.UTF8.GetBytes("RIFF"));
                writer.Write(dataLength + 36);
                writer.Write(System.Text.Encoding.UTF8.GetBytes("WAVE"));
                writer.Write(System.Text.Encoding.UTF8.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((ushort)1);
                writer.Write((ushort)channels);
                writer.Write(hz);
                writer.Write(hz * channels * 2);
                writer.Write((ushort)(channels * 2));
                writer.Write((ushort)16);
                writer.Write(System.Text.Encoding.UTF8.GetBytes("data"));
                writer.Write(dataLength);
            }
            return stream.ToArray();
        }
    }
}
