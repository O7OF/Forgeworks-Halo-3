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
        string fullShadersDir = Path.Combine(ekPath, "tags", "levels", scenarioType, levelName, "shaders");
        string fullTagsBitmapsDir = Path.Combine(ekPath, "tags", "levels", scenarioType, levelName, "bitmaps");

        Directory.CreateDirectory(fullDataDir);
        Directory.CreateDirectory(fullBitmapsDir);
        Directory.CreateDirectory(fullTagsDir);
        Directory.CreateDirectory(fullShadersDir);
        Directory.CreateDirectory(fullTagsBitmapsDir);

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
            string err = "Neither tool_fast.exe nor tool.exe found in Editing Kit directory.";
            WriteLastToolOutputLog(ekPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {err}\nEK Path: {ekPath}\n");
            return new CompileResult(false, err, TimeSpan.Zero, "");
        }

        var (_, fullTagsDir, dataRel) = SetupLevelDirectories(ekPath, levelName, scenarioType);

        try
        {
            fullLog.Add($"================================================================================");
            fullLog.Add($"FORGEWORKS BLAM! TOOL COMPILATION LOG - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            fullLog.Add($"Level: {levelName} (Scenario Type: {scenarioType})");
            fullLog.Add($"Tool Executable: {toolExe}");
            fullLog.Add($"Editing Kit Path: {ekPath}");
            fullLog.Add($"================================================================================\n");

            // Ensure shader collections mapping exists
            Shaders.CustomShaderBuilder.EnsureShaderCollectionsEntry(ekPath, levelName, scenarioType);

            // Step 0: Compile custom texture bitmaps if .tif files are present
            string dataBitmapsRel = Path.Combine("levels", scenarioType, levelName, "bitmaps");
            string fullDataBitmaps = Path.Combine(ekPath, "data", dataBitmapsRel);
            if (Directory.Exists(fullDataBitmaps) && Directory.GetFiles(fullDataBitmaps, "*.tif").Length > 0)
            {
                string bitmapsCmd = $"bitmaps \"{dataBitmapsRel}\"";
                fullLog.Add($"=== STEP 0: COMPILE BITMAPS ===");
                fullLog.Add($"Command: {toolExe} {bitmapsCmd}");
                onLogMessage?.Invoke($"[ForgeWorks] Compiling custom bitmaps: {dataBitmapsRel}...");
                var (bCode, bLog) = await RunProcessAsync(toolExe, bitmapsCmd, ekPath, onLogMessage, cancellationToken);
                fullLog.Add(bLog);
                fullLog.Add($"Exit Code: {bCode}\n");
                WriteLastToolOutputLog(ekPath, string.Join("\n", fullLog));
            }

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

            fullLog.Add($"\n=== STEP 1: COMPILE STRUCTURE BSP ===");
            fullLog.Add($"Command: {toolExe} {structureCmd}");
            onLogMessage?.Invoke($"[ForgeWorks] Compiling structure BSP: {assRelative}...");

            DateTime bspStartTimeUtc = DateTime.UtcNow;
            var (code1, log1) = await RunProcessAsync(toolExe, structureCmd, ekPath, onLogMessage, cancellationToken);
            fullLog.Add(log1);
            fullLog.Add($"Exit Code: {code1}\n");
            WriteLastToolOutputLog(ekPath, string.Join("\n", fullLog));

            string bspTagPath = Path.Combine(fullTagsDir, $"{levelName}.scenario_structure_bsp");
            bool bspExists = File.Exists(bspTagPath);
            bool bspFresh = bspExists && File.GetLastWriteTimeUtc(bspTagPath) >= bspStartTimeUtc.AddSeconds(-2);

            // Classify errors: -ERROR-, -FATAL-, ### ERROR, shader load/resolution failures, layout exceptions
            var bspErrors = log1.Split('\n')
                .Where(l => l.Contains("-ERROR-") || l.Contains("-FATAL-") || l.Contains("### ERROR") ||
                            l.Contains("failed to load shader", StringComparison.OrdinalIgnoreCase) ||
                            l.Contains("could not find shader", StringComparison.OrdinalIgnoreCase) ||
                            l.Contains("fallback", StringComparison.OrdinalIgnoreCase) && l.Contains("shader", StringComparison.OrdinalIgnoreCase))
                .Select(l => l.Trim())
                .Distinct()
                .Take(5)
                .ToList();

            if (code1 != 0 || !bspFresh || bspErrors.Count > 0 || log1.Contains("-FATAL-"))
            {
                sw.Stop();
                string errorSummary = bspErrors.Count > 0
                    ? string.Join("; ", bspErrors)
                    : (!bspFresh ? "Structure BSP output was not generated or is stale." : $"Tool exited with code {code1}");

                fullLog.Add($"\n[ERROR] Structure compilation failed: {errorSummary}");
                WriteLastToolOutputLog(ekPath, string.Join("\n", fullLog));
                return new CompileResult(false, $"Tool structure compilation failed: {errorSummary}. See last-tool-output-log for details.", sw.Elapsed, string.Join("\n", fullLog));
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

            // Step 3: tool build-cache-file levels\multi\<level>\<level> pc
            string scenarioRelative = Path.Combine("levels", scenarioType, levelName, levelName);
            string cacheCmd = $"build-cache-file \"{scenarioRelative}\" pc";

            fullLog.Add($"\n=== STEP 2: BUILD CACHE FILE ===");
            fullLog.Add($"Command: {toolExe} {cacheCmd}");
            onLogMessage?.Invoke($"[ForgeWorks] Packaging map cache file: {scenarioRelative}...");

            DateTime cacheStartTimeUtc = DateTime.UtcNow;
            var (code2, log2) = await RunProcessAsync(toolExe, cacheCmd, ekPath, onLogMessage, cancellationToken);
            fullLog.Add(log2);
            fullLog.Add($"Exit Code: {code2}\n");

            sw.Stop();

            string expectedMap = Path.Combine(ekPath, "maps", $"{levelName}.map");
            bool mapExists = File.Exists(expectedMap);
            bool mapFresh = mapExists && File.GetLastWriteTimeUtc(expectedMap) >= cacheStartTimeUtc.AddSeconds(-2);

            var cacheErrors = log2.Split('\n')
                .Where(l => l.Contains("-ERROR-") || l.Contains("-FATAL-") || l.Contains("### ERROR"))
                .Select(l => l.Trim())
                .Distinct()
                .Take(5)
                .ToList();

            if (code2 != 0 || !mapFresh || cacheErrors.Count > 0)
            {
                string cacheSummary = cacheErrors.Count > 0
                    ? string.Join("; ", cacheErrors)
                    : (!mapFresh ? $"Output map '{expectedMap}' was not created or is stale." : $"Tool exited with code {code2}");

                fullLog.Add($"\n[ERROR] Build-cache-file failed: {cacheSummary}");
                WriteLastToolOutputLog(ekPath, string.Join("\n", fullLog));
                return new CompileResult(false, $"Tool build-cache-file failed: {cacheSummary}. See last-tool-output-log for details.", sw.Elapsed, string.Join("\n", fullLog), expectedMap);
            }

            fullLog.Add($"\n================================================================================");
            fullLog.Add($"[SUCCESS] Compilation succeeded in {sw.Elapsed.TotalSeconds:F1}s! Map created at: {expectedMap}");
            fullLog.Add($"================================================================================");
            WriteLastToolOutputLog(ekPath, string.Join("\n", fullLog));

            return new CompileResult(true, $"Compilation Succeeded in {sw.Elapsed.TotalSeconds:F1}s! Map created at: {expectedMap}", sw.Elapsed, string.Join("\n", fullLog), expectedMap);
        }
        catch (Exception ex)
        {
            fullLog.Add($"\n[FATAL EXCEPTION]: {ex}");
            WriteLastToolOutputLog(ekPath, string.Join("\n", fullLog));
            throw;
        }
    }

    public const string LastToolLogFileName = "last-tool-output-log.txt";

    public static string GetLastToolLogPath(string? ekPath = null)
    {
        if (!string.IsNullOrWhiteSpace(ekPath) && Directory.Exists(ekPath))
        {
            string ekLog = Path.Combine(ekPath, LastToolLogFileName);
            if (File.Exists(ekLog)) return ekLog;
        }
        string curLog = Path.Combine(Environment.CurrentDirectory, LastToolLogFileName);
        if (File.Exists(curLog)) return curLog;

        string appLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LastToolLogFileName);
        if (File.Exists(appLog)) return appLog;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForgeWorks", LastToolLogFileName);
    }

    public static void WriteLastToolOutputLog(string? ekPath, string logContent)
    {
        var targetPaths = new List<string>
        {
            Path.Combine(@"C:\Users\Chey1\Desktop\Forgeworks Halo 3", LastToolLogFileName),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LastToolLogFileName),
            Path.Combine(Environment.CurrentDirectory, LastToolLogFileName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForgeWorks", LastToolLogFileName)
        };

        if (!string.IsNullOrWhiteSpace(ekPath) && Directory.Exists(ekPath))
        {
            targetPaths.Insert(0, Path.Combine(ekPath, LastToolLogFileName));
        }

        foreach (var p in targetPaths)
        {
            try
            {
                var dir = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(p, logContent);
            }
            catch
            {
                // Never allow logging write failures to crash compilation
            }
        }
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
