using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI; // Added for LayoutRebuilder
using TMPro;  // If using TextMeshPro for subtitles

public class OpenAIWhisperTranslator : MonoBehaviour
{

  public delegate void SubtitlesUpdatedHandler(string newSubtitle);
  public event SubtitlesUpdatedHandler OnSubtitlesUpdated;

  // This variable will be populated from the .env file
  [Header("OpenAI Settings")]
  [Tooltip("Your OpenAI API key")]
  public string openAIApiKey = ""; // Leave blank; will be overwritten if found in .env

  [Tooltip("The model to use (for translations, use \"whisper-1\")")]
  public string model = "whisper-1";

  [Header("UI Settings")]
  [Tooltip("Reference to a TextMeshProUGUI component for displaying subtitles")]
  public TextMeshProUGUI subtitleText;

  [Tooltip("Force refresh canvas when text updates")]
  public bool forceCanvasRefresh = true;

  [Tooltip("Optional canvas to refresh")]
  public Canvas subtitleCanvas;

  private EnvLoader envLoader;

  void Start()
  {
    // Try to find our EnvLoader component in the scene
    envLoader = UnityEngine.Object.FindAnyObjectByType<EnvLoader>();
    if (envLoader != null)
    {
      string envApiKey = envLoader.GetEnv("OPENAI_API_KEY");

      if (string.IsNullOrEmpty(envApiKey))
      {
        Debug.LogError("OPENAI_API_KEY not found in .env file");
      }
      else
      {
        Debug.Log("Successfully loaded OPENAI_API_KEY from .env file");
        openAIApiKey = envApiKey;
      }
    }
    else
    {
      Debug.LogError("EnvLoader not found in scene. Please add the EnvLoader script to a GameObject.");
    }

    // Check if we have a subtitle text component
    if (subtitleText == null)
    {
      Debug.LogWarning("No subtitleText assigned. Please assign a TextMeshProUGUI component in the inspector for direct text updates.");
    }

    // If canvas not assigned but we have a subtitleText, try to find the canvas from the text
    if (subtitleCanvas == null && subtitleText != null)
    {
      subtitleCanvas = subtitleText.GetComponentInParent<Canvas>();
    }
  }

  /// <summary>
  /// Call this method to translate audio directly from a byte array.
  /// This eliminates the need to save the audio file to disk first.
  /// </summary>
  /// <param name="fileData">Byte array containing the audio data (WAV format)</param>
  public void TranslateAudioData(byte[] fileData, string originalFilename = "audio.wav")
  {
    StartCoroutine(TranslateAudioCoroutine(fileData, originalFilename));
  }

  /// <summary>
  /// Coroutine that handles sending audio data for translation.
  /// </summary>
  /// <param name="fileData">Byte array of the audio file data</param>
  /// <param name="originalFilename">Filename to use for the upload (can be arbitrary)</param>
  /// <returns></returns>
  private IEnumerator TranslateAudioCoroutine(byte[] fileData, string originalFilename)
  {
    if (fileData == null || fileData.Length == 0)
    {
      Debug.LogError("Audio data is empty.");
      yield break;
    }

    // Create a form for multipart/form-data
    WWWForm form = new WWWForm();
    // Add the required 'model' field.
    form.AddField("model", model);
    form.AddField("temperature", "0"); // 0 for more deterministic output

    // Add the audio file with the field name "file".
    // Adjust the mime type if necessary. For WAV files use "audio/wav".
    form.AddBinaryData("file", fileData, originalFilename, "audio/wav");

    // Create the UnityWebRequest.
    UnityWebRequest request = UnityWebRequest.Post("https://api.openai.com/v1/audio/translations", form);

    // Set the Authorization header with the API key loaded from .env.
    request.SetRequestHeader("Authorization", "Bearer " + openAIApiKey);

    // Send the request and wait until it completes.
    yield return request.SendWebRequest();

    if (request.result != UnityWebRequest.Result.Success)
    {
      Debug.LogError("Error during translation request: " + request.error);
      Debug.LogError(request.downloadHandler.text);
      yield break;
    }

    // For debugging, print the response.
    string jsonResponse = request.downloadHandler.text;
    Debug.Log("Whisper API Response: " + jsonResponse);

    // Extract the text from the JSON response.
    string translatedText = ExtractTextFromJSON(jsonResponse);
    Debug.Log("Translated text: " + translatedText);

    // Fire the event (null-check for safety)
    OnSubtitlesUpdated?.Invoke(translatedText);


    // Update the UI text directly if we have a reference
    if (subtitleText != null)
    {
      subtitleText.text = translatedText;

      // Force refresh the UI
      ForceUIRefresh();

      Debug.Log("Direct subtitle update: " + translatedText);
    }

    // Also try to use the SubtitleDisplay component if available (for backward compatibility)
    SubtitleDisplay subtitleDisplay = UnityEngine.Object.FindAnyObjectByType<SubtitleDisplay>();
    if (subtitleDisplay != null)
    {
      subtitleDisplay.UpdateSubtitle(translatedText);
      Debug.Log("SubtitleDisplay found. Updated with: " + translatedText);
    }
    else if (subtitleText == null)
    {
      Debug.LogError("No way to display subtitles. Neither subtitleText nor SubtitleDisplay is available.");
    }
  }

  /// <summary>
  /// Forces the UI to refresh immediately
  /// </summary>
  private void ForceUIRefresh()
  {
    if (!forceCanvasRefresh) return;

    // Force layout rebuild on the text
    if (subtitleText != null)
    {
      subtitleText.ForceMeshUpdate(true);

      // Try different approaches to ensure UI updates
      if (subtitleText.rectTransform != null)
      {
        LayoutRebuilder.ForceRebuildLayoutImmediate(subtitleText.rectTransform);
      }
    }

    // Force redraw of the canvas
    if (subtitleCanvas != null)
    {
      subtitleCanvas.enabled = false;
      subtitleCanvas.enabled = true;
    }

    // Additional option: mark canvas as dirty for rebuild
    if (subtitleCanvas != null)
    {
      Canvas.ForceUpdateCanvases();
    }
  }

  /// <summary>
  /// A simple extractor that looks for a "text" field in the JSON response.
  /// </summary>
  private string ExtractTextFromJSON(string json)
  {
    try
    {
      int index = json.IndexOf("\"text\"");
      if (index < 0) return "Text field not found.";
      int colon = json.IndexOf(":", index);
      int startQuote = json.IndexOf("\"", colon + 1);
      int endQuote = json.IndexOf("\"", startQuote + 1);
      if (startQuote >= 0 && endQuote > startQuote)
      {
        return json.Substring(startQuote + 1, endQuote - startQuote - 1);
      }
      else
      {
        return "Unable to parse text.";
      }
    }
    catch (Exception e)
    {
      Debug.LogError("Error parsing JSON: " + e.Message);
      return "Error parsing translation.";
    }
  }
}