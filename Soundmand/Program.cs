using NAudio.CoreAudioApi;
using NAudio.Vorbis;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Soundmand
{
  class Program
  {
    static ConfigSystem config;

    private async static Task<int> Main(string[] args)
    {
      string exePath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

      // don't 

      if (!File.Exists(Path.Combine(exePath, "config.json")))
      {
        Config.Init();
        Console.WriteLine("Config has been initialized. Run Soundmand.exe --help to see available options.");
        return 0;
      }

      config = Config.Get();

      #region Initialize CLI
      var rootCommand = new RootCommand("Play soundboard without gui");

      var deviceOpt = new Option<bool>("--list-device", "Device index");
      deviceOpt.AddAlias("-l");
      rootCommand.AddOption(deviceOpt);

      var volumeOpt = new Option<int>("--volume", "Set volume when playing audio");
      volumeOpt.AddAlias("-v");
      volumeOpt.SetDefaultValue(100);

      var deviceIndex = new Option<int>("--device", "Set Output audio");
      deviceIndex.AddAlias("-d");

      var audioPath = new Argument<string>("audio-or-dir-path", "Path to audio file or file including audio files");
      audioPath.Arity = ArgumentArity.ExactlyOne;

      var saveCom = new Command("save", "Save action for usage later");

      var pathSaveOpt = new Option<string>("--output", "Location to save .bat and .vbs scripts");
      pathSaveOpt.AddAlias("-o");

      saveCom.AddOption(volumeOpt);
      saveCom.AddOption(deviceIndex);
      saveCom.AddOption(pathSaveOpt);
      saveCom.AddArgument(audioPath);

      var playCom = new Command("play", "Play audio file");

      playCom.AddOption(volumeOpt);
      playCom.AddOption(deviceIndex);
      playCom.AddArgument(audioPath);

      var fixCom = new Command("fix", "Fix bat file");

      fixCom.AddOption(pathSaveOpt);

      rootCommand.AddCommand(saveCom);
      rootCommand.AddCommand(playCom);
      rootCommand.AddCommand(fixCom);
      #endregion

      #region Root command
      rootCommand.SetHandler((bool devices) =>
      {
        if (devices) ListAudioDevices();
        else
        {
          if (Admin.IsRunningAsAdministrator() && !Admin.IsPathInSystemPath(exePath))
          {
            Console.WriteLine("Wait... You're in admin mode!");
            Console.WriteLine("Can you add my app to path windows? (y for add)");

            var yesOrNo = Console.ReadLine();
            if (yesOrNo == "y")
            {
              Admin.AddPathToSystemEnvironmentVariable(exePath);
              Console.WriteLine("Done!");
            }
          }
          else
            Console.WriteLine("Check Soundmand.exe --help for more options");
        }
      }, deviceOpt);
      #endregion

      #region Save Commands
      saveCom.SetHandler((string path, int index, int volume, string pathAudio) =>
      {
        if (volume < 0)
        {
          Console.WriteLine("Invalid negative volume. Set volume between 0 and 100");
          return;
        }

        float vol = Clamp(volume / 100f, 0, 1);

        SaveScripts(pathAudio, path, index, vol);
      }, pathSaveOpt, deviceIndex, volumeOpt, audioPath);
      #endregion

      #region Play Commands
      playCom.SetHandler((int index, int volume, string path) =>
      {
        if (volume < 0)
        {
          Console.WriteLine("Invalid negative volume. Set volume between 0 and 100");
          return;
        }

        float vol = Clamp(volume / 100f, 0, 1);

        PlayAudio(path, index, vol);
      }, deviceIndex, volumeOpt, audioPath);
      #endregion

      #region Fix Commands
      fixCom.SetHandler((string path) =>
      {
        FixBatFile(path);
      }, pathSaveOpt);
      #endregion

      return await rootCommand.InvokeAsync(args);
    }

    #region Fix console file
    private static void FixBatFile(string path)
    {
      if (!File.Exists(path) || Path.GetExtension(path) != ".bat")
      {
        Console.WriteLine("Invalid path.");
        return;
      }

      string oldBatfile = File.ReadAllText(path);
      string oldVbsfile = File.ReadAllText(Path.ChangeExtension(path, ".vbs"));

      string regexExe = "(?<soundmand_dir>([A-Za-z]:\\\\)?([^\"]*?[\\\\\\/])?[Ss]oundmand\\.exe)";
      string exeFile = (config.portableMode) ? System.Reflection.Assembly.GetExecutingAssembly().Location : "Soundmand.exe";

      string newBatfile = Regex.Replace(oldBatfile, regexExe, match =>
      {
        return match.Value.Replace(match.Groups["soundmand_dir"].Value,  exeFile );
      }, RegexOptions.IgnoreCase);

      string newVbsfile = Regex.Replace(oldVbsfile, regexExe, match =>
      {
        return match.Value.Replace(match.Groups["soundmand_dir"].Value, exeFile);
      }, RegexOptions.IgnoreCase);

      File.WriteAllText(Path.ChangeExtension(path, ".bat"), newBatfile);
      File.WriteAllText(Path.ChangeExtension(path, ".vbs"), newVbsfile);

      Console.WriteLine("File successfully fixed!");
    }
    #endregion

    #region List audio devices
    static void ListAudioDevices()
    {
      var enumerator = new MMDeviceEnumerator();
      var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
      int index = 0;

      Console.WriteLine("-1: Default Output");
      foreach (var device in devices)
      {
        Console.WriteLine($"{index}: {device.FriendlyName}");
        index++;
      }
    }
    #endregion


    #region Play audio
    static void PlayAudio(string path, int deviceIndex, float volume = -1)
    {
      string pathAudio = path;

      if (!File.Exists(path) && !Directory.Exists(path))
      {
        Console.WriteLine("File/directory not found.");
        return;
      }

      #region Random soundboard from directory
      if (Directory.Exists(path))
      {
        // get all audio files in the directory, bu not for sub- dir
        string[] files = Directory.GetFiles(path).Where(file => audioDuration(file, config.maxDuration) && IsSupportedAudioFile(file)).ToArray();

        // check if empty
        if (files.Length == 0)
        {
          Console.WriteLine("No audio files found in the directory.");
          return;
        }

        // select random file
        Random random = new Random();
        int index = random.Next(0, files.Length);
        pathAudio = files[index];
      }
      #endregion

      #region When audiofile is not ext specified
      if (File.Exists(pathAudio) && !IsSupportedAudioFile(pathAudio) && Path.GetExtension(pathAudio).ToLower() != ".ogg")
      {
        Console.WriteLine($"Can't play because {Path.GetFileName(pathAudio)} is not supported.");
        return;
      }
      #endregion

      try
      {

        #region custom Output audio
        var selectedDevice = getOutputAudio(deviceIndex);

        if (selectedDevice == null)
        {
          Console.WriteLine("Invalid value, Use \"Soundmand --list-device\" to see available devices.");
          return;
        }
        #endregion

        #region playing audio (ogg)
        if (Path.GetExtension(pathAudio).ToLower() == ".ogg")
        {
          if (!config.oggSupport)
          {
            Console.WriteLine("Ogg support is disabled because some config not support.");
            Console.WriteLine("But, you can activate it in config.json");
            return;
          }
          else
          {
            if (volume > -1)
              Console.WriteLine($"Warning: volume is ignored because ogg not support volume.");
          }

          using (var audioFile = new VorbisWaveReader(pathAudio))
          using (var outputDevice = new WasapiOut(selectedDevice, AudioClientShareMode.Shared, true, 0))
          {
            if (audioFile.TotalTime.TotalSeconds > config.maxDuration)
            {
              Console.WriteLine("Audio file is too long. Maximum duration is " + config.maxDuration + " seconds.");
              return;
            }

            outputDevice.Init(audioFile);
            outputDevice.Play();

            Console.WriteLine($"Playing audio \"{Path.GetFileNameWithoutExtension(pathAudio)}\"...");

            while (outputDevice.PlaybackState == PlaybackState.Playing)
              Thread.Sleep(100);
          }

          return;
        }
        #endregion

        #region playing audio
        volume = (volume < 0) ? config.volume : volume;

        using (var audioFile = new AudioFileReader(pathAudio))
        using (var outputDevice = new WasapiOut(selectedDevice, AudioClientShareMode.Shared, true, 0))
        {
          if (audioFile.TotalTime.TotalSeconds > config.maxDuration)
          {
            Console.WriteLine("Audio file is too long. Maximum duration is " + config.maxDuration + " seconds.");
            return;
          }
          audioFile.Volume = volume;
          outputDevice.Init(audioFile);

          outputDevice.Play();

          Console.WriteLine($"Playing audio \"{Path.GetFileNameWithoutExtension(pathAudio)}\"...");

          while (outputDevice.PlaybackState == PlaybackState.Playing)
            Thread.Sleep(100);
        }
        #endregion

      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error playing audio: {ex.Message}");
      }
    }
    #endregion

    #region Save Scripts
    static void SaveScripts(string path, string savePath, int deviceIndex, float volume, int duration = 35)
    {
      try
      {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
          Console.WriteLine("File/directory not found.");
          return;
        }

        if (File.Exists(path))
        {
          if (!IsSupportedAudioFile(path))
          {
            Console.WriteLine("File is not an audio file.");
            return;
          }

          if (!audioDuration(path, duration))
          {
            Console.WriteLine("Audio file is too long. Maximum duration is " + duration + " seconds.");
            return;
          }
        }

        string exePath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

        if (string.IsNullOrEmpty(savePath) && !config.portableMode)
          savePath = config.savePath;
        else if (string.IsNullOrEmpty(savePath) && config.portableMode)
          savePath = Path.GetFullPath(Path.Combine(exePath, "..\\save"));

        if (!Directory.Exists(savePath))
          Directory.CreateDirectory(savePath);

        string exeFile = (config.portableMode) ? "{System.Reflection.Assembly.GetExecutingAssembly().Location}" : "Soundmand.exe";

        // optional 
        string setVol = (volume < 1) ? $" --volume {volume * 100}" : "";
        string setDevice = (deviceIndex > -1) ? $" --device {deviceIndex}" : "";

        string batContent = $"\"{exeFile}\" play {setDevice} {setVol} \"{path}\"";
        string batFilePath = Path.Combine(savePath, $"{Path.GetFileNameWithoutExtension(path)}.bat");

        string vbsContent = $"CreateObject(\"WScript.Shell\").Run \"{batContent.Replace("\"", "\"\"")}\", 0";
        string vbsFilePath = Path.Combine(savePath, $"{Path.GetFileNameWithoutExtension(path)}.vbs");

        File.WriteAllText(batFilePath, batContent);
        File.WriteAllText(vbsFilePath, vbsContent);

        Console.WriteLine($"Scripts saved successfully at {savePath}");
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error saving scripts: {ex.Message}");
      }
    }
    #endregion

    #region Audio Extention
    private static readonly HashSet<string> AudioSupport = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",  // MPEG-1 Audio Layer 3
        ".mp2",  // MPEG-1 Audio Layer 2
        ".aac",  // Advanced Audio Coding
        ".m4a",  // AAC in MP4 container
        ".flac", // Free Lossless Audio Codec
        ".alac", // Apple Lossless Audio Codec
        ".wav",  // Waveform Audio File Format
        ".wma",  // Windows Media Audio
        ".3gp",  // 3GPP multimedia file
        ".3g2",  // 3GPP2 multimedia file
        ".amr",   // Adaptive Multi-Rate audio
        // ".ogg",  // Ogg Vorbis
    };

    private static readonly HashSet<string> AudioSupportOgg = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",  // MPEG-1 Audio Layer 3
        ".mp2",  // MPEG-1 Audio Layer 2
        ".aac",  // Advanced Audio Coding
        ".m4a",  // AAC in MP4 container
        ".flac", // Free Lossless Audio Codec
        ".alac", // Apple Lossless Audio Codec
        ".wav",  // Waveform Audio File Format
        ".wma",  // Windows Media Audio
        ".3gp",  // 3GPP multimedia file
        ".3g2",  // 3GPP2 multimedia file
        ".amr",   // Adaptive Multi-Rate audio
        ".ogg",  // Ogg Vorbis
    };

    static bool IsSupportedAudioFile(string filePath)
    {
      string extension = Path.GetExtension(filePath);
      return (config.oggSupport) ? AudioSupportOgg.Contains(extension) : AudioSupport.Contains(extension);
    }
    #endregion

    #region some usless functions
    static bool audioDuration(string filePath, int maxDuration)
    {
      try
      {
        if (Path.GetExtension(filePath).ToLower() == ".ogg")
        {
          using (var audioFile = new VorbisWaveReader(filePath))
            return audioFile.TotalTime.TotalSeconds < maxDuration;
        }

        using (var audioFile = new AudioFileReader(filePath))
          return audioFile.TotalTime.TotalSeconds < maxDuration;
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error checking audio duration: {ex.Message}");
        return false;
      }
    }

    static float Clamp(float value, float min, float max) =>
      Math.Max(min, Math.Min(max, value));

    private static MMDevice getOutputAudio(int index)
    {
      var enumerator = new MMDeviceEnumerator();
      var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

      if (index >= devices.Count || index < -1)
        return null;
      if (index == -1)
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
      else
        return devices[index];
    }

    static bool CheckAudioDuration(string filePath)
    {
      try
      {
        if (Path.GetExtension(filePath).ToLower() == ".ogg")
        {
          if (config.oggSupport)
          {
            using (var audioFile = new VorbisWaveReader(filePath))
              return audioFile.TotalTime.TotalSeconds > config.maxDuration;
          }
          else
          {
            Console.WriteLine("Ogg support is disabled because some config not support.");
            Console.WriteLine("But, you can activate it in config.json");
            return false;
          }
        }
        else
        {
          using (var audioFile = new AudioFileReader(filePath))
            return audioFile.TotalTime.TotalSeconds > config.maxDuration;
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Error checking audio duration: {ex.Message}");
        return false;
      }
    }
    #endregion
  }
}
