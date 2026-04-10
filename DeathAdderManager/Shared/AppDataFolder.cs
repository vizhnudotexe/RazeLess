using System;
using System.IO;

namespace DeathAdderManager.Shared;

public static class AppDataFolder
{
    public static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeathAdderManager");

    static AppDataFolder()
    {
        Directory.CreateDirectory(Path);
    }
}
