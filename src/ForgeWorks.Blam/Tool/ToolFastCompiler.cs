using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ForgeWorks.Blam.Scenario;

namespace ForgeWorks.Blam.Tool;

public sealed record CompileResult(
    bool Success,
    string StatusMessage,
    TimeSpan Elapsed,
    string LogOutput,
    string? MapFilePath = null
);

public sealed class ToolFastCompiler
{
    public static string AutoDetectEkPath(string gameFolder = "H3EK")
    {
        var roots = GetSteamLibraryRoots();
        roots.Add(@"C:\Program Files (x86)\Steam\steamapps\common");
        roots.Add(@"C:\SteamLibrary\steamapps\common");
        roots.Add(@"D:\SteamLibrary\steamapps\common");
        roots.Add(@"E:\SteamLibrary\steamapps\common");

        foreach (var root in roots)
        {
            var candidate = Path.Combine(root, gameFolder);
            if (Directory.Exists(candidate))
            {
                if (File.Exists(Path.Combine(candidate, "tool_fast.exe")) ||
                    File.Exists(Path.Combine(candidate, "tool.exe")))
                {
                    return candidate;
                }
            }
        }

        return string.Empty;
    }

    private static List<string> GetSteamLibraryRoots()
    {
        var roots = new List<string>();
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key != null)
            {
                var steamPathObj = key.GetValue("SteamPath");
                if (steamPathObj is string steamPath && Directory.Exists(steamPath))
                {
                    roots.Add(Path.Combine(steamPath, "steamapps", "common"));
                    var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdfPath))
                    {
                        string vdfContent = File.ReadAllText(vdfPath);
                        var matches = Regex.Matches(vdfContent, @"""path""\s+""([^""]+)""");
                        foreach (Match m in matches)
                        {
                            string p = m.Groups[1].Value.Replace(@"\\", @"\");
                            string common = Path.Combine(p, "steamapps", "common");
                            if (Directory.Exists(common) && !roots.Contains(common))
                            {
                                roots.Add(common);
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore registry/vdf exceptions gracefully
        }
        return roots;
    }

    public static string ResolveToolExecutable(string ekPath, bool preferFast = true)
    {
        if (preferFast)
        {
            var fast = Path.Combine(ekPath, "tool_fast.exe");
            if (File.Exists(fast)) return fast;
        }

        var standard = Path.Combine(ekPath, "tool.exe");
        return File.Exists(standard) ? standard : string.Empty;
    }

    public static (string fullDataDir, string fullTagsDir, string dataRel) SetupLevelDirectories(
        string ekPath,
        string levelName,
        string scenarioType = "multi")
    {
        string dataRel = Path.Combine("levels", scenarioType, levelName, "structure");
        string fullDataDir = Path.Combine(ekPath, "data", dataRel);
        string fullBitmapsDir = Path.Combine(ekPath, "data", "levels", scenarioType, levelName, "bitmaps");
        string fullTagsDir = Path.Combine(ekPath, "tags", "levels", scenarioType, levelName);

        Directory.CreateDirectory(fullDataDir);
        Directory.CreateDirectory(fullBitmapsDir);
        Directory.CreateDirectory(fullTagsDir);

        return (fullDataDir, fullTagsDir, dataRel);
    }

    public static async Task<CompileResult> CompileStructureAndCacheAsync(
        string ekPath,
        string levelName,
        string scenarioType = "multi",
        Scenario.ScenarioData? scenarioData = null,
        bool preferFast = true,
        Action<string>? onLogMessage = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var fullLog = new List<string>();

        string toolExe = ResolveToolExecutable(ekPath, preferFast);
        if (string.IsNullOrEmpty(toolExe))
        {
            return new CompileResult(false, "Neither tool_fast.exe nor tool.exe found in Editing Kit directory.", TimeSpan.Zero, "");
        }

        var (_, fullTagsDir, dataRel) = SetupLevelDirectories(ekPath, levelName, scenarioType);

        // Step 1: Synthesize and write authentic .scenario tag with all spawns, equipment, volumes
        string scenarioTagPath = Path.Combine(fullTagsDir, $"{levelName}.scenario");
        string bspTagRelative = Path.Combine("levels", scenarioType, levelName, levelName);
        string skyTagRelative = scenarioData?.SkyTagPath ?? @"levels\multi\chill\sky\chill_sky";

        var spawns = scenarioData != null && scenarioData.Spawns.Count > 0 ? scenarioData.Spawns : new List<PlayerStartingLocation> { new(new System.Numerics.Vector3(0, 0, 50), 0f, 0) };
        var equipment = scenarioData != null ? scenarioData.Equipment : new List<NetgameEquipment>();
        var volumes = scenarioData != null ? scenarioData.Volumes : new List<TriggerVolume>();

        Tags.ScenarioTagBuilder.GenerateScenarioTag(
            scenarioTagPath,
            levelName,
            bspTagRelative,
            skyTagRelative,
            spawns,
            equipment,
            volumes,
            ekPath);

        fullLog.Add($"[ForgeWorks] Generated Scenario Tag with {spawns.Count} spawns, {equipment.Count} weapons, {volumes.Count} volumes: {scenarioTagPath}");

        // Step 2: tool structure levels\multi\<level>\structure\<level>.ass
        string assRelative = Path.Combine(dataRel, $"{levelName}.ass");
        string structureCmd = $"structure \"{assRelative}\"";

        fullLog.Add($"=== RUNNING: {toolExe} {structureCmd} ===");
        onLogMessage?.Invoke($"[ForgeWorks] Compiling structure BSP: {assRelative}...");

        var (code1, log1) = await RunProcessAsync(toolExe, structureCmd, ekPath, onLogMessage, cancellationToken);
        fullLog.Add(log1);

        if (code1 != 0 || log1.Contains("-FATAL-") || log1.Contains("### ERROR"))
        {
            sw.Stop();
            return new CompileResult(false, $"Tool structure compilation failed with exit code {code1}.", sw.Elapsed, string.Join("\n", fullLog));
        }

        // Re-commit scenario tag to ensure structure tool didn't overwrite scenario properties
        Tags.ScenarioTagBuilder.GenerateScenarioTag(
            scenarioTagPath,
            levelName,
            bspTagRelative,
            skyTagRelative,
            spawns,
            equipment,
            volumes,
            ekPath);

        // Step 2: tool build-cache-file levels\multi\<level>\<level> pc
        string scenarioRelative = Path.Combine("levels", scenarioType, levelName, levelName);
        string cacheCmd = $"build-cache-file \"{scenarioRelative}\" pc";

        fullLog.Add($"=== RUNNING: {toolExe} {cacheCmd} ===");
        onLogMessage?.Invoke($"[ForgeWorks] Packaging map cache file: {scenarioRelative}...");

        var (code2, log2) = await RunProcessAsync(toolExe, cacheCmd, ekPath, onLogMessage, cancellationToken);
        fullLog.Add(log2);

        sw.Stop();

        string expectedMap = Path.Combine(ekPath, "maps", $"{levelName}.map");
        bool mapExists = File.Exists(expectedMap);

        if (code2 != 0 || !mapExists)
        {
            return new CompileResult(false, $"Tool build-cache-file failed with exit code {code2}.", sw.Elapsed, string.Join("\n", fullLog), expectedMap);
        }

        return new CompileResult(true, $"Compilation Succeeded in {sw.Elapsed.TotalSeconds:F1}s! Map created at: {expectedMap}", sw.Elapsed, string.Join("\n", fullLog), expectedMap);
    }

    public static async Task<(int exitCode, string output)> RunProcessAsync(
        string exePath,
        string arguments,
        string workingDir,
        Action<string>? onLineOutput,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = new Process { StartInfo = psi };
        var outputLines = new List<string>();

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                outputLines.Add(e.Data);
                onLineOutput?.Invoke(e.Data);
            }
        };

        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                outputLines.Add(e.Data);
                onLineOutput?.Invoke(e.Data);
            }
        };

        proc.Start();
        proc.StandardInput.Close(); // Prevent tool from waiting on "press any key"
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(cancellationToken);
        return (proc.ExitCode, string.Join("\n", outputLines));
    }
}
