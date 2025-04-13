using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;

public class ButtonSpeechToSpeech : MonoBehaviour
{
  [Header("UI Elements")]
  [Tooltip("Button to trigger recording")]
  public Button triggerButton;

  [Tooltip("Text display for transcription result")]
  public TMPro.TextMeshProUGUI transcriptionText;

  [Tooltip("Optional status text")]
  public TMPro.TextMeshProUGUI statusText;

  [Header("API Settings")]
  [Tooltip("OpenAI API Key - load from EnvLoader or set directly")]
  public string apiKey = "";

  [Tooltip("Optional EnvLoader reference")]
  public EnvLoader envLoader;

  [Tooltip("TTS Voice to use")]
  public string ttsVoice = "alloy";

  [Header("Translation Settings")]
  [Tooltip("Source language for transcription (e.g., 'es' for Spanish)")]
  public string sourceLanguage = "es";

  [Tooltip("Target language for TTS (e.g., 'en' for English)")]
  public string targetLanguage = "en";

  [Tooltip("Whether to translate between transcription and TTS")]
  public bool translateText = true;

  [Header("Automatic Audio Detection")]
  [Tooltip("Whether to start audio detection after TTS completes")]
  public bool enableAutoDetection = true;

  [Tooltip("Reference to AudioDetectionRecorder component")]
  public AudioDetectionRecorder audioDetector;

  // Private variables
  private AudioClip recordingClip;
  private bool isRecording = false;
  private AudioSource audioSource;
  private string deviceName;
  private readonly int recordingDuration = 5; // Fixed 5-second recording

  private void Awake()
  {
    // Get or add AudioSource component
    audioSource = GetComponent<AudioSource>();
    if (audioSource == null)
    {
      audioSource = gameObject.AddComponent<AudioSource>();
    }

    // Try to get API key from EnvLoader if assigned
    if (string.IsNullOrEmpty(apiKey) && envLoader != null)
    {
      apiKey = envLoader.GetEnv("OPENAI_API_KEY");
      if (string.IsNullOrEmpty(apiKey))
      {
        Debug.LogWarning("API Key not found in EnvLoader");
      }
    }

    // Find AudioDetectionRecorder if not set
    if (audioDetector == null)
    {
      audioDetector = UnityEngine.Object.FindAnyObjectByType<AudioDetectionRecorder>();
      if (audioDetector == null)
      {
        Debug.LogWarning("AudioDetectionRecorder not found in scene");
      }
    }
  }

  private void Start()
  {
    // Setup button
    if (triggerButton != null)
    {
      triggerButton.onClick.AddListener(StartRecordingProcess);
    }
    else
    {
      Debug.LogError("Trigger button is not assigned!");
    }

    // Check for microphone
    if (Microphone.devices.Length > 0)
    {
      deviceName = Microphone.devices[0];
      SetStatus($"Ready.");
    }
    else
    {
      SetStatus("Error: No microphone found");
      if (triggerButton != null) triggerButton.interactable = false;
    }
  }

  private void SetStatus(string message)
  {
    Debug.Log(message);
    if (statusText != null)
    {
      statusText.text = message;
    }
  }

  public void StartRecordingProcess()
  {
    if (isRecording) return;

    // Stop the audio detector if it's running
    if (audioDetector != null && audioDetector.isActiveAndEnabled)
    {
      audioDetector.StopMonitoring();
    }

    StartCoroutine(RecordAndProcess());
  }

  private IEnumerator RecordAndProcess()
  {
    // 1. Start recording - fixed 10-second duration
    isRecording = true;
    SetStatus("Recording...");

    // Start microphone recording for exactly 10 seconds
    recordingClip = Microphone.Start(deviceName, false, recordingDuration, 16000);

    // Make button non-interactable during recording
    if (triggerButton != null)
    {
      triggerButton.interactable = false;
    }

    // Show countdown in status
    for (int i = recordingDuration; i > 0; i--)
    {
      SetStatus($"Recording... {i} seconds remaining");
      yield return new WaitForSeconds(1.0f);
    }

    // 2. Stop recording
    Microphone.End(deviceName);
    isRecording = false;
    SetStatus("Processing recording...");

    // Re-enable the button
    if (triggerButton != null)
    {
      triggerButton.interactable = true;
    }

    // 3. Convert speech to text
    byte[] audioData = WavUtility.FromAudioClip(recordingClip);
    if (audioData != null)
    {
      SetStatus("Transcribing audio...");

      string transcription = null;
      yield return StartCoroutine(TranscribeAudio(audioData, (result) =>
      {
        transcription = result;
      }));

      if (!string.IsNullOrEmpty(transcription))
      {
        // Display transcription
        if (transcriptionText != null)
        {
          transcriptionText.text = transcription;
        }

        string textForTTS = transcription;

        // Add translation step if enabled
        if (translateText && sourceLanguage != targetLanguage)
        {
          SetStatus($"Translating from {sourceLanguage} to {targetLanguage}...");
          yield return StartCoroutine(TranslateText(transcription, (translatedText) =>
          {
            textForTTS = translatedText;
          }));
        }

        // 4. Convert text to speech (now using potentially translated text)
        SetStatus("Converting text to speech...");
        yield return StartCoroutine(TextToSpeech(textForTTS));

        // Wait a moment for the TTS audio to finish playing
        float ttsPlaybackDuration = audioSource.clip ? audioSource.clip.length : 3.0f;
        SetStatus($"Playing TTS audio... ({ttsPlaybackDuration:F1}s)");
        yield return new WaitForSeconds(ttsPlaybackDuration + 0.5f);

        // Start audio detection after TTS completes
        if (enableAutoDetection && audioDetector != null)
        {
          SetStatus("Starting audio detection...");
          audioDetector.StartMonitoring();
        }
      }
      else
      {
        SetStatus("Transcription failed");

        // Start audio detection even if transcription failed
        if (enableAutoDetection && audioDetector != null)
        {
          SetStatus("Starting audio detection...");
          audioDetector.StartMonitoring();
        }
      }
    }
    else
    {
      SetStatus("Failed to process audio");

      // Start audio detection even if audio processing failed
      if (enableAutoDetection && audioDetector != null)
      {
        SetStatus("Starting audio detection...");
        audioDetector.StartMonitoring();
      }
    }
  }

  private IEnumerator TranscribeAudio(byte[] audioData, Action<string> callback)
  {
    if (string.IsNullOrEmpty(apiKey))
    {
      SetStatus("Error: API Key not set");
      callback(null);
      yield break;
    }

    // Use proper form data construction for binary upload
    WWWForm form = new WWWForm();
    form.AddBinaryData("file", audioData, "audio.wav", "audio/wav");
    form.AddField("model", "whisper-1");
    form.AddField("language", sourceLanguage);

    using (UnityWebRequest www = UnityWebRequest.Post("https://api.openai.com/v1/audio/transcriptions", form))
    {
      www.downloadHandler = new DownloadHandlerBuffer();
      www.SetRequestHeader("Authorization", "Bearer " + apiKey);

      yield return www.SendWebRequest();

      if (www.result == UnityWebRequest.Result.Success)
      {
        // Parse response
        string response = www.downloadHandler.text;
        TranscriptionResponse transcription = JsonUtility.FromJson<TranscriptionResponse>(response);

        if (transcription != null && !string.IsNullOrEmpty(transcription.text))
        {
          SetStatus("Transcription complete");
          callback(transcription.text);
        }
        else
        {
          SetStatus("Error: Empty transcription response");
          callback(null);
        }
      }
      else
      {
        SetStatus($"Transcription error: {www.error}");
        Debug.LogError($"Detailed error: {www.downloadHandler.text}");
        callback(null);
      }
    }
  }

  private IEnumerator TranslateText(string textToTranslate, Action<string> callback)
  {
    if (string.IsNullOrEmpty(apiKey))
    {
      SetStatus("Error: API Key not set");
      callback(textToTranslate); // Return original text on error
      yield break;
    }

    // Create messages for the translation request
    List<Message> messages = new List<Message>();
    messages.Add(new Message
    {
      role = "system",
      content = $"You are a translator. Translate the following text from {sourceLanguage} to {targetLanguage}. Return only the translated text."
    });
    messages.Add(new Message
    {
      role = "user",
      content = textToTranslate
    });

    // Create the full request
    TranslationRequest request = new TranslationRequest
    {
      model = "gpt-3.5-turbo",
      messages = messages
    };

    string jsonPayload = JsonUtility.ToJson(request);

    using (UnityWebRequest www = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST"))
    {
      www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonPayload));
      www.downloadHandler = new DownloadHandlerBuffer();
      www.SetRequestHeader("Authorization", "Bearer " + apiKey);
      www.SetRequestHeader("Content-Type", "application/json");

      yield return www.SendWebRequest();

      if (www.result == UnityWebRequest.Result.Success)
      {
        string response = www.downloadHandler.text;
        TranslationResponse translationResponse = JsonUtility.FromJson<TranslationResponse>(response);

        if (translationResponse != null && translationResponse.choices.Count > 0)
        {
          string translatedText = translationResponse.choices[0].message.content;
          SetStatus("Translation complete");
          callback(translatedText);
        }
        else
        {
          SetStatus("Error: Empty translation response");
          callback(textToTranslate); // Return original text on error
        }
      }
      else
      {
        SetStatus($"Translation error: {www.error}");
        Debug.LogError($"Detailed error: {www.downloadHandler.text}");
        callback(textToTranslate); // Return original text on error
      }
    }
  }

  private IEnumerator TextToSpeech(string text)
  {
    if (string.IsNullOrEmpty(apiKey))
    {
      SetStatus("Error: API Key not set");
      yield break;
    }

    // Create request payload
    string jsonPayload = JsonUtility.ToJson(new TTSRequest
    {
      model = "tts-1",
      input = text,
      voice = ttsVoice
    });

    using (UnityWebRequest www = new UnityWebRequest("https://api.openai.com/v1/audio/speech", "POST"))
    {
      www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonPayload));
      www.downloadHandler = new DownloadHandlerBuffer();
      www.SetRequestHeader("Authorization", "Bearer " + apiKey);
      www.SetRequestHeader("Content-Type", "application/json");

      yield return www.SendWebRequest();

      if (www.result == UnityWebRequest.Result.Success)
      {
        byte[] audioData = www.downloadHandler.data;

        // Handle TTS response data (MP3 format)
        string tempPath = Path.Combine(Application.temporaryCachePath, "tts_response.mp3");
        File.WriteAllBytes(tempPath, audioData);

        // Use Unity's WWW to load the audio clip (supports MP3)
        using (UnityWebRequest audioRequest = UnityWebRequestMultimedia.GetAudioClip("file://" + tempPath, AudioType.MPEG))
        {
          yield return audioRequest.SendWebRequest();

          if (audioRequest.result == UnityWebRequest.Result.Success)
          {
            AudioClip ttsClip = DownloadHandlerAudioClip.GetContent(audioRequest);
            if (ttsClip != null)
            {
              audioSource.clip = ttsClip;
              audioSource.Play();
              SetStatus("Playing TTS audio");
            }
            else
            {
              SetStatus("Error: Failed to create TTS AudioClip");
            }
          }
          else
          {
            SetStatus($"Error loading audio: {audioRequest.error}");
          }
        }

        // Clean up temporary file
        try
        {
          File.Delete(tempPath);
        }
        catch (Exception e)
        {
          Debug.LogWarning($"Failed to delete temporary file: {e.Message}");
        }
      }
      else
      {
        SetStatus($"TTS error: {www.error}");
        Debug.LogError($"Detailed error: {www.downloadHandler.text}");
      }
    }
  }

  private void OnDestroy()
  {
    if (triggerButton != null)
    {
      triggerButton.onClick.RemoveListener(StartRecordingProcess);
    }

    if (isRecording && !string.IsNullOrEmpty(deviceName))
    {
      Microphone.End(deviceName);
    }
  }

  // Helper classes for JSON serialization
  [Serializable]
  private class TranscriptionResponse
  {
    public string text;
  }

  [Serializable]
  private class TTSRequest
  {
    public string model;
    public string input;
    public string voice;
  }

  [Serializable]
  private class TranslationRequest
  {
    public string model;
    public List<Message> messages;
  }

  [Serializable]
  private class Message
  {
    public string role;
    public string content;
  }

  [Serializable]
  private class TranslationResponse
  {
    public List<Choice> choices;
  }

  [Serializable]
  private class Choice
  {
    public Message message;
  }
}

// Utility class for WAV conversion - Only needed for sending audio to API
public static class WavUtility
{
  public static byte[] FromAudioClip(AudioClip clip)
  {
    try
    {
      // Get audio data from clip
      float[] samples = new float[clip.samples * clip.channels];
      clip.GetData(samples, 0);

      // Convert to 16-bit PCM
      short[] intData = new short[samples.Length];
      for (int i = 0; i < samples.Length; i++)
      {
        intData[i] = (short)(samples[i] * 32767);
      }

      using (var memoryStream = new MemoryStream())
      {
        using (var writer = new BinaryWriter(memoryStream))
        {
          // Write WAV header
          // "RIFF" chunk descriptor
          writer.Write(new char[] { 'R', 'I', 'F', 'F' });
          writer.Write(36 + intData.Length * 2); // File size - 8 bytes
          writer.Write(new char[] { 'W', 'A', 'V', 'E' });

          // "fmt " sub-chunk
          writer.Write(new char[] { 'f', 'm', 't', ' ' });
          writer.Write(16); // Sub-chunk size
          writer.Write((short)1); // Audio format (1 = PCM)
          writer.Write((short)clip.channels); // Channels
          writer.Write(clip.frequency); // Sample rate
          writer.Write(clip.frequency * clip.channels * 2); // Byte rate
          writer.Write((short)(clip.channels * 2)); // Block align
          writer.Write((short)16); // Bits per sample

          // "data" sub-chunk
          writer.Write(new char[] { 'd', 'a', 't', 'a' });
          writer.Write(intData.Length * 2); // Sub-chunk size

          // Write audio data
          foreach (short sample in intData)
          {
            writer.Write(sample);
          }
        }

        return memoryStream.ToArray();
      }
    }
    catch (Exception e)
    {
      Debug.LogError("Error converting AudioClip to WAV: " + e.Message);
      return null;
    }
  }
}