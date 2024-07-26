using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace Soundmand
{
  internal class Admin
  {
    // Method to check if the application is running as administrator
    public static bool IsRunningAsAdministrator()
    {
      using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
      {
        WindowsPrincipal principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
      }
    }

    // Method to add the executable path to the system PATH environment variable
    public static void AddPathToSystemEnvironmentVariable(string pathToAdd)
    {
      const string environmentKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
      using (RegistryKey key = Registry.LocalMachine.OpenSubKey(environmentKey, writable: true))
      {
        if (key != null)
        {
          string currentPath = (string)key.GetValue("Path", string.Empty);
          if (!currentPath.Contains(pathToAdd))
          {
            key.SetValue("Path", $"{currentPath};{pathToAdd}");
            Console.WriteLine("Path added to system PATH environment variable.");
          }
          else
          {
            Console.WriteLine("Path is already in the system PATH environment variable.");
          }
        }
      }
    }

    public static bool IsPathInSystemPath(string pathToCheck)
    {
      const string environmentKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
      using (RegistryKey key = Registry.LocalMachine.OpenSubKey(environmentKey, writable: false))
      {
        if (key != null)
        {
          string currentPath = (string)key.GetValue("Path", string.Empty);
          string[] paths = currentPath.Split(';');

          foreach (string path in paths)
          {
            if (string.Equals(path, pathToCheck, StringComparison.OrdinalIgnoreCase))
            {
              return true;
            }
          }
        }
      }
      return false;
    }
  }
}
