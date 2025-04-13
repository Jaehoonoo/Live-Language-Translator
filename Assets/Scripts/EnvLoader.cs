using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class EnvLoader : MonoBehaviour
{
  private Dictionary<string, string> envVars = new Dictionary<string, string>();

  void Awake()
  {
    // Move up from the Assets folder to the project root
    string projectRoot = Directory.GetParent(Application.dataPath).FullName;
    string path = Path.Combine(projectRoot, ".env");

    if (File.Exists(path))
    {
      foreach (var line in File.ReadAllLines(path))
      {
        // Ignore comments or empty lines
        if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
          continue;

        int index = line.IndexOf('=');
        if (index != -1)
        {
          string key = line.Substring(0, index).Trim();
          string value = line.Substring(index + 1).Trim();
          envVars[key] = value;
        }
      }
    }
    else
    {
      Debug.LogWarning($".env file not found at {path}");
    }
  }

  public string GetEnv(string key)
  {
    return envVars.ContainsKey(key) ? envVars[key] : string.Empty;
  }
}
