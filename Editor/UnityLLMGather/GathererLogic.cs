using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor; // Required for AssetDatabase if used, though not directly here now.
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace UnityLLMGather
{
    public class GathererLogic
    {
        private const int MaxRootObjects = 100;
        private const int MaxChildObjects = 7;
        // Using DotNet.Glob for better globbing
        // Add "com.cysharp.dotnet-glob" : "1.0.0" or later to package.json dependencies
        // or include the library manually. For now, stick to basic wildcard.
        // private static bool useGlobbing = true; // Flag to control globbing (requires library)

        private string targetDirectory;
        private int maxLinesPerFile;
        private long maxFileSize;
        private bool includeSceneSummary;
        private PatternProfile currentProfile;
        private List<string> ignoreSceneObjectNames;
        private string projectRoot;

        public int ProcessedFiles { get; private set; }
        public int SkippedFilesSize { get; private set; }
        public int SkippedFilesContent { get; private set; }
        public int SkippedFilesBinary { get; private set; }
        public int SkippedFilesInclude { get; private set; }
        public int TotalFilesFound { get; private set; }


        public GathererLogic(string targetDir, int maxLines, long maxSize, bool includeScene, PatternProfile profile, List<string> ignoreObjects)
        {
            targetDirectory = targetDir;
            maxLinesPerFile = maxLines;
            maxFileSize = maxSize;
            includeSceneSummary = includeScene;
            currentProfile = profile ?? PatternProfile.CreateDefault();
            ignoreSceneObjectNames = ignoreObjects ?? new List<string>();
            projectRoot = Directory.GetParent(Application.dataPath).FullName;
        }

        public List<string> GenerateSummary(System.Action<string, float> updateProgress)
        {
            ResetCounters();
            var outputLines = new List<string>();
            var allFilesToProcess = new List<string>();

            var fullTargetPath = Path.Combine(projectRoot, targetDirectory);

            if (!Directory.Exists(fullTargetPath) && !File.Exists(fullTargetPath))
            {
                // Handle 'Assets' specifically if root combo fails
                if (targetDirectory.Equals("Assets", System.StringComparison.OrdinalIgnoreCase) && Directory.Exists(Application.dataPath))
                {
                    fullTargetPath = Application.dataPath;
                }
                else
                {
                    throw new DirectoryNotFoundException($"Target directory or file not found: {fullTargetPath}");
                }
            }

            updateProgress?.Invoke("Scanning directory structure...", 0.1f);
            outputLines.Add("# Project Summary");

            if (Directory.Exists(fullTargetPath))
            {
                outputLines.Add("## Directory Structure");
                outputLines.Add("```");
                GenerateDirectoryTree(fullTargetPath, fullTargetPath, outputLines, ref allFilesToProcess);
                outputLines.Add("```");
            }
            else // Single File target
            {
                outputLines.Add($"## Target File: {GetRelativePath(fullTargetPath)}");
                if (!ShouldExclude(fullTargetPath))
                {
                    if (ShouldInclude(fullTargetPath))
                    {
                        allFilesToProcess.Add(fullTargetPath);
                    }
                    else
                    {
                        SkippedFilesInclude++;
                    }
                }
            }

            outputLines.Add("");
            TotalFilesFound = allFilesToProcess.Count;

            outputLines.Add("## File Contents");
            for (int i = 0; i < allFilesToProcess.Count; i++)
            {
                string filePath = allFilesToProcess[i];
                float progress = 0.2f + 0.6f * ((float)i / TotalFilesFound);
                updateProgress?.Invoke($"Processing file: {GetRelativePath(filePath)} ({i + 1}/{TotalFilesFound})", progress);

                AppendFileContent(outputLines, filePath);
            }

            if (includeSceneSummary)
            {
                updateProgress?.Invoke("Analyzing active scene...", 0.9f);
                AppendSceneSummary(outputLines);
            }

            return outputLines;
        }

        private void ResetCounters()
        {
            ProcessedFiles = 0;
            SkippedFilesSize = 0;
            SkippedFilesContent = 0;
            SkippedFilesBinary = 0;
            SkippedFilesInclude = 0;
            TotalFilesFound = 0;
        }

        private void GenerateDirectoryTree(string rootDir, string currentPath, List<string> outputLines, ref List<string> fileList, int level = 0)
        {
            var indent = new string(' ', 4 * level);
            var name = Path.GetFileName(currentPath);

            // Don't add root dir name at level 0 if it's the initial target
            if (level > 0 || !currentPath.Equals(rootDir, System.StringComparison.OrdinalIgnoreCase))
            {
                outputLines.Add($"{indent}{name}/");
            }


            try
            {
                var directories = Directory.GetDirectories(currentPath)
                                          .Select(d => d.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)) // Normalize separators
                                          .OrderBy(d => d);
                foreach (var directory in directories)
                {
                    if (ShouldExclude(directory)) continue;
                    GenerateDirectoryTree(rootDir, directory, outputLines, ref fileList, level + 1);
                }
            }
            catch (System.Exception e) { Debug.LogWarning($"[UnityLLMGather] Directory access error: {currentPath} - {e.Message}"); }

            try
            {
                var files = Directory.GetFiles(currentPath)
                                     .Select(f => f.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)) // Normalize separators
                                     .ToList();
                var filteredFiles = files.Where(f => !ShouldExclude(f)).ToList();

                int beforeIncludeCount = filteredFiles.Count;
                if (currentProfile.IncludePatterns != null && currentProfile.IncludePatterns.Count > 0 && currentProfile.IncludePatterns.Any(p => !string.IsNullOrWhiteSpace(p)))
                {
                    filteredFiles = filteredFiles.Where(ShouldInclude).ToList();
                    SkippedFilesInclude += beforeIncludeCount - filteredFiles.Count;
                }

                foreach (var file in filteredFiles.OrderBy(f => f))
                {
                    var fileName = Path.GetFileName(file);
                    // Use level + 1 for file indent relative to parent directory
                    var fileIndent = new string(' ', 4 * (level + 1));
                    outputLines.Add($"{fileIndent}{fileName}");
                    fileList.Add(file);
                }
            }
            catch (System.Exception e) { Debug.LogWarning($"[UnityLLMGather] File access error in directory: {currentPath} - {e.Message}"); }
        }


        private void AppendFileContent(List<string> outputLines, string filePath)
        {
            var relativePath = GetRelativePath(filePath);
            bool skipContent = ShouldSkipContent(filePath);
            bool skipSize = false;
            long fileSize = 0;
            try
            {
                fileSize = new FileInfo(filePath).Length;
                skipSize = maxFileSize > 0 && fileSize > maxFileSize;
            }
            catch (IOException ex)
            {
                Debug.LogWarning($"[UnityLLMGather] Could not get file size for {relativePath}: {ex.Message}");
                // Treat as skippable if size check fails? Or just proceed? Proceed for now.
            }


            outputLines.Add($"### {relativePath}");
            outputLines.Add("```");

            if (skipContent)
            {
                outputLines.Add("(Content skipped due to matching Skip Content Pattern)");
                SkippedFilesContent++;
            }
            else if (skipSize)
            {
                outputLines.Add($"(Content skipped due to file size limit: {fileSize} > {maxFileSize} bytes)");
                SkippedFilesSize++;
            }
            else
            {
                bool isBinary = IsBinaryFile(filePath);
                if (isBinary)
                {
                    outputLines.Add($"(Binary file detected, content not displayed: {fileSize} bytes)");
                    SkippedFilesBinary++;
                }
                else
                {
                    string fileContent = GetFileContents(filePath);
                    outputLines.Add(fileContent);
                    ProcessedFiles++; // Count non-binary, non-skipped files as processed
                }
                // Consider binary files "processed" in terms of listing them, but not content-wise.
                // If binary counts as processed: ProcessedFiles++; outside the else.
            }
            outputLines.Add("```");
            outputLines.Add("");
        }

        private bool ShouldExclude(string path)
        {
            var relativePath = GetRelativePath(path);
            return MatchPatternList(relativePath, currentProfile.ExcludePatterns, path);
        }

        private bool ShouldSkipContent(string path)
        {
            var relativePath = GetRelativePath(path);
            return MatchPatternList(relativePath, currentProfile.SkipContentPatterns, path);
        }

        private bool ShouldInclude(string path)
        {
            var relativePath = GetRelativePath(path);
            if (currentProfile.IncludePatterns == null || currentProfile.IncludePatterns.Count == 0 || !currentProfile.IncludePatterns.Any(p => !string.IsNullOrWhiteSpace(p)))
                return true;

            return MatchPatternList(relativePath, currentProfile.IncludePatterns, path);
        }

        private bool MatchPatternList(string relativePath, List<string> patterns, string fullPath)
        {
            if (patterns == null) return false;
            // Normalize relative path separators for matching
            string normalizedRelativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');

            foreach (var pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                if (MatchSinglePattern(normalizedRelativePath, pattern.Replace(Path.DirectorySeparatorChar, '/'))) // Normalize pattern too
                {
                    return true;
                }
            }
            return false;
        }

        // Basic Glob-like matching (supports *, **, ?)
        private bool MatchSinglePattern(string input, string pattern)
        {
            // Handle simple cases first
            if (pattern == "*" || pattern == "**") return true;
            if (string.IsNullOrEmpty(pattern)) return string.IsNullOrEmpty(input);
            if (string.IsNullOrEmpty(input)) return false; // Pattern has content, input doesn't

            // Convert basic glob to regex
            string regexPattern = ConvertGlobToRegex(pattern);
            try
            {
                // Match against the whole string, case-insensitive (common for paths)
                return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            }
            catch (ArgumentException ex)
            {
                Debug.LogWarning($"[UnityLLMGather] Invalid regex pattern generated from glob '{pattern}': {regexPattern}. Error: {ex.Message}");
                // Fallback to simple wildcard? Or just return false? Return false for safety.
                return false;
            }
        }

        // Converts basic glob patterns (*, **, ?) to regex
        private static string ConvertGlobToRegex(string glob)
        {
            var regex = new StringBuilder("^");
            int i = 0;
            while (i < glob.Length)
            {
                char c = glob[i];
                switch (c)
                {
                    case '*':
                        // Handle '**' (matches zero or more directory levels)
                        if (i + 1 < glob.Length && glob[i + 1] == '*')
                        {
                            // Check for /**/ or ** patterns
                            bool leadingSlash = (i > 0 && glob[i - 1] == '/');
                            bool trailingSlash = (i + 2 < glob.Length && glob[i + 2] == '/');

                            if (leadingSlash && trailingSlash) // Matches /anything/ or just /
                            {
                                regex.Append("(?:/|/.*/)");
                                i += 2; // Skip '*' and '/'
                            }
                            else if (leadingSlash) // Matches /anything at the end
                            {
                                regex.Append("(?:/.*)?"); // Optional segment starting with /
                            }
                            else if (trailingSlash) // Matches anything at the start/
                            {
                                regex.Append("(?:.*/)?"); // Optional segment ending with /
                            }
                            else // Standalone ** or at start/end of path segment
                            {
                                regex.Append(".*"); // Matches any character sequence including slashes
                            }
                            i++; // Skip the second '*'
                        }
                        else // Handle single '*' (matches any char except '/')
                        {
                            regex.Append("[^/]*");
                        }
                        break;
                    case '?':
                        // Matches single char except '/'
                        regex.Append("[^/]");
                        break;
                    case '/':
                        regex.Append("/");
                        break;
                    default:
                        // Escape regex special characters
                        regex.Append(Regex.Escape(c.ToString()));
                        break;
                }
                i++;
            }
            regex.Append("$");
            return regex.ToString();
        }


        private string GetRelativePath(string fullPath)
        {
            // Ensure projectRoot ends with a separator for correct relative path calculation
            string root = projectRoot.EndsWith(Path.DirectorySeparatorChar.ToString()) || projectRoot.EndsWith(Path.AltDirectorySeparatorChar.ToString())
                        ? projectRoot
                        : projectRoot + Path.DirectorySeparatorChar;

            // Use Uri for robust relative path calculation
            try
            {
                var rootUri = new System.Uri(root);
                var fullUri = new System.Uri(fullPath);

                // Ensure fullUri is indeed under rootUri before making relative
                if (!fullUri.AbsoluteUri.StartsWith(rootUri.AbsoluteUri, System.StringComparison.OrdinalIgnoreCase))
                {
                    // If not under root, return the full path or just the filename?
                    // Returning just the filename might be less confusing than a potentially weird relative path.
                    // Or return the original fullPath if it's outside the project structure.
                    return Path.GetFileName(fullPath); // Simplest approach if outside root
                                                       // Alternatively: return fullPath;
                }

                var relativeUri = rootUri.MakeRelativeUri(fullUri);
                return System.Uri.UnescapeDataString(relativeUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
            }
            catch (System.UriFormatException ex)
            {
                Debug.LogError($"[UnityLLMGather] Error creating Uri for path calculation. Root: '{root}', Full: '{fullPath}'. Error: {ex.Message}");
                // Fallback: simple string manipulation (less reliable)
                if (fullPath.StartsWith(root, System.StringComparison.OrdinalIgnoreCase))
                {
                    return fullPath.Substring(root.Length);
                }
                return Path.GetFileName(fullPath); // Fallback to filename
            }
        }


        private string GetFileContents(string filePath)
        {
            try
            {
                using (var reader = new StreamReader(filePath, Encoding.UTF8, true))
                {
                    var lines = new List<string>();
                    string line;
                    int lineCount = 0;
                    while ((line = reader.ReadLine()) != null && lineCount < maxLinesPerFile)
                    {
                        lines.Add(line);
                        lineCount++;
                    }
                    // Check if there was more content after reading maxLines
                    bool truncated = false;
                    if (lineCount == maxLinesPerFile)
                    {
                        // Try reading one more line to see if the file continues
                        if (reader.ReadLine() != null)
                        {
                            truncated = true;
                        }
                    }

                    if (truncated)
                    {
                        lines.Add("...");
                        lines.Add($"(Truncated: Maximum line count {maxLinesPerFile} reached)");
                    }
                    return string.Join("\n", lines);
                }
            }
            catch (IOException e) { return $"[Error] Failed to read file (IO): {e.Message}"; }
            catch (System.Exception e) { return $"[Error] Failed to read file: {e.Message}"; }
        }

        private bool IsBinaryFile(string filePath)
        {
            try
            {
                // Basic check for common text file extensions first
                string extension = Path.GetExtension(filePath)?.ToLowerInvariant();
                var textExtensions = new HashSet<string> {
                    ".txt", ".md", ".cs", ".js", ".ts", ".json", ".xml", ".yaml", ".yml", ".html", ".css",
                    ".shader", ".cginc", ".hlsl", ".glsl", ".py", ".java", ".cpp", ".h", ".log", ".csv", ".tsv", ".php", ".rb"
                     // Add more known text extensions as needed
                };
                if (textExtensions.Contains(extension)) return false;

                // More robust check if extension is ambiguous or unknown
                const int charsToCheck = 512;
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    var buffer = new byte[charsToCheck];
                    int bytesRead = stream.Read(buffer, 0, charsToCheck);
                    for (int i = 0; i < bytesRead; i++)
                    {
                        // Check for NUL bytes, which are common in binary files but rare in text files.
                        if (buffer[i] == 0) return true;
                        // Could add more checks here (e.g., control characters outside of \t, \n, \r)
                    }
                }
            }
            catch (IOException ex)
            {
                Debug.LogWarning($"[UnityLLMGather] Could not access file for binary check: {filePath}. Error: {ex.Message}");
                // Assume binary if we can't check? Or text? Assume text might be safer.
                return false;
            }
            catch { /* Ignore other potential errors during determination */ }
            return false; // Assume text if no NUL bytes found or check failed
        }

        private void AppendSceneSummary(List<string> outputLines)
        {
            outputLines.Add("\n# Scene Summary");
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                outputLines.Add("No active scene found or loaded.");
                return;
            }

            outputLines.Add($"## Scene: {scene.name} (Path: {scene.path})\n");
            var rootObjects = scene.GetRootGameObjects()
                .Where(go => go != null && !ignoreSceneObjectNames.Contains(go.name))
                .OrderBy(go => go.transform.GetSiblingIndex())
                .Take(MaxRootObjects).ToList();

            int totalRootObjects = scene.GetRootGameObjects().Length;
            int ignoredRootObjects = totalRootObjects - scene.GetRootGameObjects().Count(go => go != null && !ignoreSceneObjectNames.Contains(go.name));

            outputLines.Add("### Scene Structure (Root Objects)");
            outputLines.Add($"Showing {rootObjects.Count} of {totalRootObjects} root objects ({ignoredRootObjects} ignored).");
            outputLines.Add("```");
            if (rootObjects.Count == 0 && totalRootObjects > 0)
            {
                outputLines.Add("(All root objects were ignored or null)");
            }
            foreach (var rootObject in rootObjects) AppendGameObjectStructureRecursive(outputLines, rootObject, 0);
            if (totalRootObjects > MaxRootObjects) outputLines.Add($"... (and {totalRootObjects - MaxRootObjects} more root objects)");
            outputLines.Add("```\n");

            outputLines.Add("### GameObject Details");
            if (rootObjects.Count == 0 && totalRootObjects > 0)
            {
                outputLines.Add("(No details to show as all root objects were ignored or null)");
            }
            foreach (var rootObject in rootObjects) AppendGameObjectDetailsRecursive(outputLines, rootObject, 0);
            if (totalRootObjects > MaxRootObjects) outputLines.Add($"\n...(Details for {totalRootObjects - MaxRootObjects} more root objects omitted)");
        }

        private void AppendGameObjectStructureRecursive(List<string> outputLines, GameObject gameObject, int depth)
        {
            if (gameObject == null) return;
            var indent = new string(' ', depth * 2);
            outputLines.Add($"{indent}- {gameObject.name} {(gameObject.activeInHierarchy ? "" : "(Inactive)")}");

            var childTransforms = GetFilteredChildren(gameObject, MaxChildObjects);
            int totalChildren = gameObject.transform.childCount;
            int ignoredChildren = totalChildren - gameObject.transform.Cast<Transform>().Count(t => t != null && t.gameObject != null && !ignoreSceneObjectNames.Contains(t.gameObject.name));

            foreach (var child in childTransforms) AppendGameObjectStructureRecursive(outputLines, child.gameObject, depth + 1);

            if (totalChildren > childTransforms.Count)
            {
                int remaining = totalChildren - childTransforms.Count;
                int remainingIgnored = Mathf.Max(0, ignoredChildren - (totalChildren - childTransforms.Count)); // Estimate ignored among the omitted
                outputLines.Add($"{indent}  ... ({remaining} more children, approx {remainingIgnored} ignored)");
            }
            else if (ignoredChildren > 0 && totalChildren == childTransforms.Count)
            {
                // Case where all children were processed, but some were ignored earlier in the filtering
                // This count might be slightly off if MaxChildObjects was hit exactly
                // outputLines.Add($"{indent}  ({ignoredChildren} children ignored)"); // Could add this if needed
            }
        }

        private void AppendGameObjectDetailsRecursive(List<string> outputLines, GameObject gameObject, int depth)
        {
            if (gameObject == null) return;
            string prefix = new string('>', depth + 1);
            outputLines.Add($"\n{prefix} **{gameObject.name}** {(gameObject.activeSelf ? "" : "(Self Inactive)")}");
            outputLines.Add($"{prefix}   - Tag: {gameObject.tag}, Layer: {LayerMask.LayerToName(gameObject.layer)}");
            outputLines.Add($"{prefix}   - Position: {gameObject.transform.localPosition}, Rotation: {gameObject.transform.localEulerAngles}, Scale: {gameObject.transform.localScale}");
            outputLines.Add($"{prefix}   - Components:");

            var components = gameObject.GetComponents<Component>().Where(c => c != null).ToList();
            if (components.Count > 0)
            {
                // Group common components for brevity? Maybe later.
                foreach (var component in components) outputLines.Add($"{prefix}     - {component.GetType().Name}");
            }
            else { outputLines.Add($"{prefix}     (None)"); }

            var childTransforms = GetFilteredChildren(gameObject, MaxChildObjects);
            int totalChildren = gameObject.transform.childCount;
            int ignoredChildren = totalChildren - gameObject.transform.Cast<Transform>().Count(t => t != null && t.gameObject != null && !ignoreSceneObjectNames.Contains(t.gameObject.name));


            foreach (var child in childTransforms) AppendGameObjectDetailsRecursive(outputLines, child.gameObject, depth + 1);

            if (totalChildren > childTransforms.Count)
            {
                int remaining = totalChildren - childTransforms.Count;
                int remainingIgnored = Mathf.Max(0, ignoredChildren - (totalChildren - childTransforms.Count));
                outputLines.Add($"{prefix}   ... ({remaining} more children details omitted, approx {remainingIgnored} ignored)");
            }
        }

        private List<Transform> GetFilteredChildren(GameObject gameObject, int maxChildren)
        {
            // Cast<Transform>() can throw if child is destroyed during iteration? Unlikely in Editor.
            try
            {
                return gameObject.transform.Cast<Transform>()
                     .Where(t => t != null && t.gameObject != null && !ignoreSceneObjectNames.Contains(t.gameObject.name))
                     .OrderBy(t => t.GetSiblingIndex())
                     .Take(maxChildren).ToList();
            }
            catch (System.InvalidCastException)
            {
                // Handle cases where a child might not be a Transform (shouldn't happen)
                // or if iterating over destroyed objects causes issues.
                Debug.LogWarning($"[UnityLLMGather] Error iterating children of {gameObject.name}. Some children might be missing.");
                var validChildren = new List<Transform>();
                for (int i = 0; i < gameObject.transform.childCount; ++i)
                {
                    Transform child = gameObject.transform.GetChild(i);
                    if (child != null && child.gameObject != null && !ignoreSceneObjectNames.Contains(child.gameObject.name))
                    {
                        validChildren.Add(child);
                        if (validChildren.Count >= maxChildren) break;
                    }
                }
                return validChildren.OrderBy(t => t.GetSiblingIndex()).ToList(); // Order after collecting safely
            }
        }
    }
}