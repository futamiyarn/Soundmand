using Newtonsoft.Json;
using System;
using System.IO;

namespace Soundmand
{
  static class Config
  {
    public static void Init()
    {
      ConfigSystem configSystem = new ConfigSystem
      {
        portableMode = false,
        oggSupport = false,
        savePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Soundmand"),
        maxDuration = 35,
        volume = 100,
        device = -1
      };

      // save aside exe file
      string exeFile = System.Reflection.Assembly.GetExecutingAssembly().Location;
      string exePath = Path.GetDirectoryName(exeFile);
      string configPath = Path.Combine(exePath, "config.json");

      string json = JsonConvert.SerializeObject(configSystem, Formatting.Indented);
      File.WriteAllText(configPath, json);
    }

    public static ConfigSystem Get()
    {
      string exeFile = System.Reflection.Assembly.GetExecutingAssembly().Location;
      string exePath = Path.GetDirectoryName(exeFile);
      string configPath = Path.Combine(exePath, "config.json");
      string json = File.ReadAllText(configPath);
      return JsonConvert.DeserializeObject<ConfigSystem>(json);
    }
  }

  class ConfigSystem
  {
    [JsonProperty("portable-mode")]
    public bool portableMode { get; set; }
    [JsonProperty("ogg-support")]
    public bool oggSupport { get; set; }
    [JsonProperty("save-path")]
    public string savePath { get; set; }
    [JsonProperty("max-duration")]
    public int maxDuration { get; set; }
    public int volume { get; set; }
    public int device { get; set; }
  }
}
