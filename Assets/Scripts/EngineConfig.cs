using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class EngineOption
{
    public string name;
    public string value;
}

[Serializable]
public class EngineEntry
{
    public string name;
    public string path;
    public EngineOption[] options;
}

[Serializable]
class EngineList
{
    public EngineEntry[] engines;
}

public class Opening
{
    public string Name;
    public List<string> Moves;
}

// Reads StreamingAssets/engines.json and StreamingAssets/openings.txt.
public static class EngineConfig
{
    public const string RefereePath = "Engines/chessai";

    public static List<EngineEntry> LoadEngines()
    {
        string file = Path.Combine(Application.streamingAssetsPath, "engines.json");
        if (!File.Exists(file)) return new List<EngineEntry>();
        var list = JsonUtility.FromJson<EngineList>(File.ReadAllText(file));
        return new List<EngineEntry>(list?.engines ?? new EngineEntry[0]);
    }

    public static List<Opening> LoadOpenings()
    {
        var result = new List<Opening>();
        string file = Path.Combine(Application.streamingAssetsPath, "openings.txt");
        if (!File.Exists(file)) return result;
        foreach (var raw in File.ReadAllLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || !line.Contains("|")) continue;
            var parts = line.Split('|');
            result.Add(new Opening
            {
                Name = parts[0].Trim(),
                Moves = new List<string>(parts[1].Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)),
            });
        }
        return result;
    }

    public static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(Application.streamingAssetsPath, path);

    public static List<KeyValuePair<string, string>> Options(EngineEntry e)
    {
        var list = new List<KeyValuePair<string, string>>();
        if (e.options != null)
            foreach (var o in e.options) list.Add(new KeyValuePair<string, string>(o.name, o.value));
        return list;
    }
}
