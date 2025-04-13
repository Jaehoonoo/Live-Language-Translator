using TMPro;
using UnityEngine;

public class SubtitleDisplay : MonoBehaviour
{
  public TextMeshProUGUI subtitleText;

  private void Awake()
  {
    // Your existing auto-assign logic
    if (subtitleText == null)
    {
      subtitleText = GetComponentInChildren<TextMeshProUGUI>();
      if (subtitleText != null)
      {
        Debug.Log("SubtitleDisplay auto-assigned TextMeshProUGUI from children.");
      }
      else
      {
        Debug.LogWarning("Could not find a TextMeshProUGUI component in children. Please assign one in the Inspector.");
      }
    }
  }

  private void Start()
  {
    // 1) Find the translator instance in the scene
    var translator = Object.FindAnyObjectByType<OpenAIWhisperTranslator>();
    if (translator != null)
    {
      // 2) Subscribe to the OnSubtitlesUpdated event
      translator.OnSubtitlesUpdated += UpdateSubtitle;
      Debug.Log("SubtitleDisplay subscribed to translator event.");
    }
    else
    {
      Debug.LogWarning("No OpenAIWhisperTranslator found in scene.");
    }
  }

  private void OnDestroy()
  {
    // Unsubscribe to avoid potential memory leaks if this object is destroyed.
    var translator = Object.FindAnyObjectByType<OpenAIWhisperTranslator>();
    if (translator != null)
    {
      translator.OnSubtitlesUpdated -= UpdateSubtitle;
    }
  }

  // 3) Called automatically when the translator fires its OnSubtitlesUpdated event
  public void UpdateSubtitle(string translatedText)
  {
    Debug.Log("UpdateSubtitle called with: " + translatedText);
    Debug.Log("Subtitle TextMeshProUGUI: " + subtitleText);
    if (subtitleText != null)
    {
      subtitleText.text = translatedText;
      subtitleText.ForceMeshUpdate();
      Debug.Log("Subtitle updated: " + subtitleText.text);
    }
    else
    {
      Debug.LogWarning("Subtitle TextMeshProUGUI is not assigned.");
    }
  }

  public void ClearSubtitle()
  {
    if (subtitleText != null)
    {
      subtitleText.text = "";
    }
  }
}
