using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using PluginCore;
using PluginCore.Helpers;
using PluginCore.Managers;
using PluginCore.Utilities;
using ProjectManager.Projects;
using ProjectManager.Projects.AS2;
using ProjectManager.Projects.AS3;
using ProjectManager.Projects.Generic;
using ProjectManager.Projects.Haxe;

namespace ProjectManager.Helpers
{
    /// <summary>
    /// Contains methods useful for working with project templates.
    /// </summary>
    public class ProjectCreator
    {
        static readonly Regex reArgs = new Regex("\\$\\(([a-z$]+)\\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        string projectName;
        string projectId;
        string packageName;
        string packagePath;
        string packageDot = "";
        string packageSlash = "";
        string defaultFlexSDK;
        Argument[] arguments;

        static readonly Hashtable projectTypes = new Hashtable();
        static readonly List<string> projectExt = new List<string>();
        static bool projectTypesSet;

        public static bool IsRunning { get; private set; }

        /// <summary>
        /// Creates a new project based on the specified template directory.
        /// </summary>
        public Project CreateProject(string templateDirectory, string projectLocation, string projectName, string packageName)
        {
            IsRunning = true;
            if (!projectTypesSet) SetInitialProjectHash();
            SetContext(projectName, packageName);
            string projectTemplate = FindProjectTemplate(templateDirectory);
            string projectPath = Path.Combine(projectLocation, projectName + Path.GetExtension(projectTemplate));
            projectPath = PathHelper.GetPhysicalPathName(projectPath);
            // notify & let a plugin handle project creation
            Hashtable para = new Hashtable();
            para["template"] = projectTemplate;
            para["location"] = projectLocation;
            para["project"] = projectPath;
            para["id"] = projectId;
            para["package"] = packageName;
            DataEvent de = new DataEvent(EventType.Command, ProjectManagerEvents.CreateProject, para);
            EventManager.DispatchEvent(this, de);
            if (!de.Handled)
            {
                Directory.CreateDirectory(projectLocation);
                // manually copy important files
                CopyFile(projectTemplate, projectPath);
                CopyProjectFiles(templateDirectory, projectLocation, true);
            }
            IsRunning = false;
            if (File.Exists(projectPath))
            {
                projectPath = PathHelper.GetPhysicalPathName(projectPath);
                de = new DataEvent(EventType.Command, ProjectManagerEvents.ProjectCreated, para);
                EventManager.DispatchEvent(this, de);
                try
                {
                    return ProjectLoader.Load(projectPath);
                }
                catch (Exception ex)
                {
                    TraceManager.Add(ex.Message);
                    return null;
                }
            }

            return null;
        }

        public static string FindProjectTemplate(string templateDirectory)
        {
            if (!projectTypesSet) SetInitialProjectHash();
            foreach (string key in projectTypes.Keys)
            {
                var path = Path.Combine(templateDirectory, key);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        void CopyProjectFiles(string sourceDir, string destDir, bool filter)
        {
            Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                if (ShouldSkip(file, filter))
                    continue;

                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(destDir, fileName);

                CopyFile(file, destFile);
            }

            var excludedDirs = new List<string>(PluginMain.Settings.ExcludedDirectories);
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(dir);
                dirName = ReplaceKeywords(dirName);
                var destSubDir = Path.Combine(destDir, dirName);
                // don't copy like .svn and stuff
                if (excludedDirs.Contains(dirName.ToLower())) continue;
                CopyProjectFiles(dir, destSubDir, false); // only filter the top directory
            }
        }

        // copy a file, if it's an .as or .fdp file, replace template keywords
        internal void CopyFile(string source, string dest)
        {
            dest = ReplaceKeywords(dest); // you can use keywords in filenames too
            var ext = Path.GetExtension(source).ToLower();
            if (FileInspector.IsProject(source, ext) || FileInspector.IsTemplate(ext))
            {
                if (FileInspector.IsTemplate(ext)) dest = dest.Substring(0, dest.LastIndexOf('.'));
                var saveBOM = PluginBase.Settings.SaveUnicodeWithBOM;
                var encoding = Encoding.GetEncoding((int)PluginBase.Settings.DefaultCodePage);
                // batch files must be encoded in ASCII
                ext = Path.GetExtension(dest).ToLower();
                if (ext == ".bat" || ext == ".cmd" || ext.StartsWithOrdinal(".php")) encoding = Encoding.ASCII;
                var src = File.ReadAllText(source);
                src = ReplaceKeywords(ProcessCodeStyleLineBreaks(src));
                FileHelper.WriteFile(dest, src, encoding, saveBOM);
            }
            else File.Copy(source, dest);
        }

        string ReplaceKeywords(string line)
        {
            if (!line.Contains('$')) return line;
            if (packageName == "") line = line.Replace(" $(PackageName)", "");
            return reArgs.Replace(line, ReplaceVars);
        }

        string ReplaceVars(Match match)
        {
            if (match.Groups.Count == 0) return match.Value;
            var name = match.Groups[1].Value.ToUpper(CultureInfo.InvariantCulture);
            switch (name)
            {
                case "CBI": return PluginBase.Settings.CommentBlockStyle == CommentBlockStyle.Indented ? " " : "";
                case "QUOTE": return "\"";
                case "CLIPBOARD": return GetClipboard();
                case "TIMESTAMP": return DateTime.Now.ToString("g");
                case "PROJECTNAME": return projectName;
                case "PROJECTNAMELOWER": return projectName.ToLower();
                case "PROJECTID": return projectId;
                case "PROJECTIDLOWER": return projectId.ToLower();
                case "PACKAGENAME": return packageName;
                case "PACKAGENAMELOWER": return packageName.ToLower();
                case "PACKAGEPATH": return packagePath;
                case "PACKAGEPATHALT": return packagePath.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                case "PACKAGEDOT": return packageDot;
                case "PACKAGESLASH": return packageSlash;
                case "PACKAGESLASHALT": return packageSlash.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                case "DOLLAR": return "$";
                case "FLEXSDK": return defaultFlexSDK ??= PathHelper.ResolvePath(PluginBase.MainForm.ProcessArgString("$(FlexSDK)")) ?? "C:\\flex_sdk";
                case "APPDIR": return PathHelper.AppDir;
                default:
                    arguments ??= PluginBase.MainForm.CustomArguments.ToArray();
                    foreach (var arg in arguments)
                        if (arg.Key.ToUpper() == name) return arg.Value;
                    break;
            }
            return match.Value;
        }

        internal void SetContext(string projectName, string packageName)
        {
            this.projectName = projectName;
            this.packageName = packageName;
            projectId = Regex.Replace(Project.RemoveDiacritics(projectName), "[^a-z0-9.-]", "", RegexOptions.IgnoreCase);
            packagePath = packageName.Replace('.', '\\');
            if (packageName.Length > 0)
            {
                packageDot = packageName + ".";
                packageSlash = packagePath + "\\";
            }
        }

        /// <summary>
        /// Gets the clipboard text
        /// </summary>
        public static string GetClipboard()
            => Clipboard.GetDataObject() is {} data && data.GetDataPresent("System.String", true)
                ? data.GetData("System.String", true).ToString()
                : string.Empty;

        static bool ShouldSkip(string path, bool isProjectRoot)
        {
            var filename = Path.GetFileName(path).ToLower();
            if (isProjectRoot)
                return projectTypes.ContainsKey(filename)
                    || filename == "project.txt"
                    || filename == "project.png";
            return filename == "dummy";
        }

        static void SetInitialProjectHash()
        {
            projectTypes["project.fdproj"] = typeof(GenericProject);
            projectTypes["project.fdp"] = typeof(AS2Project);
            projectTypes["project.as2proj"] = typeof(AS2Project);
            projectTypes["project.as3proj"] = typeof(AS3Project);
            projectTypes["project.actionscriptproperties"] = typeof(AS3Project);
            projectTypes["project.hxproj"] = typeof(HaxeProject);
            //projectTypes["project.hxml"] = typeof(HaxeProject);
            //projectTypes["project.nmml"] = typeof(HaxeProject);
            projectExt.Add("*.as2proj");
            projectExt.Add("*.as3proj");
            projectExt.Add("*.hxproj");
            projectExt.Add("*.fdproj");
            projectExt.Add("*.fdp");
            projectTypesSet = true;
        }

        public static void AppendProjectType(string templateName, Type projectType)
        {
            if (!projectTypesSet) SetInitialProjectHash();
            if (projectTypes.ContainsKey(templateName.ToLower())) return;
            projectTypes[templateName] = projectType;
            projectExt.Add(templateName.Replace("project", "*"));
        }

        public static bool IsKnownProject(string ext)
        {
            if (!projectTypesSet) SetInitialProjectHash();
            return projectTypes.ContainsKey("project" + ext);
        }

        public static Type GetProjectType(string key)
        {
            if (!projectTypesSet) SetInitialProjectHash();
            if (projectTypes.ContainsKey(key)) return (Type)projectTypes[key];
            return null;
        }

        public static string KeyForProjectPath(string path) => "project" + Path.GetExtension(path).ToLower();

        public static string GetProjectFilters()
            => "FlashDevelop Projects|" + string.Join(";", projectExt) + "|Adobe Flex Builder Project|.actionScriptProperties";

        #region ArgsProcessor duplicated code
        /// <summary>
        /// Gets the correct coding style line break chars
        /// </summary>
        public static string ProcessCodeStyleLineBreaks(string text)
        {
            const string CSLB = "$(CSLB)";
            var nextIndex = text.IndexOfOrdinal(CSLB);
            if (nextIndex == -1) return text;
            var cs = PluginBase.Settings.CodingStyle;
            if (cs == CodingStyle.BracesOnLine) return text.Replace(CSLB, string.Empty);
            var eolMode = (int)PluginBase.Settings.EOLMode;
            var lineBreak = LineEndDetector.GetNewLineMarker(eolMode);
            var result = string.Empty;
            var currentIndex = 0;
            while (nextIndex >= 0)
            {
                result += text.Substring(currentIndex, nextIndex - currentIndex) + lineBreak + GetLineIndentation(text, nextIndex);
                currentIndex = nextIndex + CSLB.Length;
                nextIndex = text.IndexOfOrdinal(CSLB, currentIndex);
            }
            return result + text.Substring(currentIndex);
        }

        /// <summary>
        /// Gets the line indentation from the text
        /// </summary>
        static string GetLineIndentation(string text, int position)
        {
            char c;
            int startPos = position;
            while (startPos > 0)
            {
                c = text[startPos];
                if (c == 10 || c == 13) break;
                startPos--;
            }
            int endPos = ++startPos;
            while (endPos < position)
            {
                c = text[endPos];
                if (c != '\t' && c != ' ') break;
                endPos++;
            }
            return text.Substring(startPos, endPos - startPos);
        }
        #endregion
    }
}