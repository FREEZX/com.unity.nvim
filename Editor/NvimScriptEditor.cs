using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Unity.CodeEditor;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NvimEditor
{
    [InitializeOnLoad]
    public class NvimScriptEditor : IExternalCodeEditor
    {
        const string nvim_server_cmd = "nvim_server_cmd";
        const string nvim_server_args = "nvim_server_args";
        const string nvim_client_cmd = "nvim_client_cmd";
        const string nvim_client_args = "nvim_client_args";
        const string nvim_remote_cmd = "nvim_remote_cmd";
        const string nvim_remote_args = "nvim_remote_args";
        const string nvim_pid = "nvim_pid";
        const string nvim_userExtensions = "nvim_userExtensions";
        static readonly GUIContent k_ResetArguments = EditorGUIUtility.TrTextContent("Reset argument");
        string m_ServerCmd;
        string m_ServerArgs;
        string m_ClientCmd;
        string m_ClientArgs;
        string m_RemoteCmd;
        string m_RemoteArgs;
        int m_ClientPid;

        IGenerator m_ProjectGeneration;

        static readonly string[] k_SupportedFileNames = { "neovide", "neovide.exe", "nvim.exe", "nvim", "lvim" };

        static bool IsOSX => Application.platform == RuntimePlatform.OSXEditor;

        static string DefaultApp => EditorPrefs.GetString("kScriptsDefaultApp");

        static string DefaultServerCmd { get; } = "nvim";
        static string DefaultServerArgs { get; } = "--headless --listen ${pipePath}";

        static string DefaultClientCmd { get; } = "alacritty";
        static string DefaultClientArgs { get; } = "-e nvim --server ${pipePath} --remote-ui";

        static string DefaultRemoteCmd { get; } = "nvr";
        static string DefaultRemoteArgs { get; } = "--servername ${pipePath} -c \"n ${filePath} | call cursor(${line},${column})<CR>\"";

        string ServerCmd
        {
            get => m_ServerCmd ?? (m_ServerCmd = EditorPrefs.GetString(nvim_server_cmd, DefaultServerCmd));
            set
            {
                m_ServerCmd = value;
                EditorPrefs.SetString(nvim_server_cmd, value);
            }
        }

        string ServerArgs
        {
            get => m_ServerArgs ?? (m_ServerArgs = EditorPrefs.GetString(nvim_server_args, DefaultServerArgs));
            set
            {
                m_ServerArgs = value;
                EditorPrefs.SetString(nvim_server_args, value);
            }
        }

        string ClientCmd
        {
            get => m_ClientCmd ?? (m_ClientCmd = EditorPrefs.GetString(nvim_client_cmd, DefaultClientCmd));
            set
            {
                m_ClientCmd = value;
                EditorPrefs.SetString(nvim_client_cmd, value);
            }
        }

        string ClientArgs
        {
            get => m_ClientArgs ?? (m_ClientArgs = EditorPrefs.GetString(nvim_client_args, DefaultClientArgs));
            set
            {
                m_ClientArgs = value;
                EditorPrefs.SetString(nvim_client_args, value);
            }
        }

        string RemoteCmd
        {
            get => m_RemoteCmd ?? (m_RemoteCmd = EditorPrefs.GetString(nvim_remote_cmd, DefaultRemoteCmd));
            set
            {
                m_RemoteCmd = value;
                EditorPrefs.SetString(nvim_remote_cmd, value);
            }
        }

        string RemoteArgs
        {
            get => m_RemoteArgs ?? (m_RemoteArgs = EditorPrefs.GetString(nvim_remote_args, DefaultRemoteArgs));
            set
            {
                m_RemoteArgs = value;
                EditorPrefs.SetString(nvim_remote_args, value);
            }
        }

        int EditorPid
        {
            get => m_ClientPid == 0 ? (m_ClientPid = EditorPrefs.GetInt(nvim_pid, 0)) : m_ClientPid;
            set
            {
                m_ClientPid = value;
                EditorPrefs.SetInt(nvim_pid, value);
            }
        }

        static string[] defaultExtensions
        {
            get
            {
                var customExtensions = new[] { "json", "asmdef", "log" };
                return EditorSettings.projectGenerationBuiltinExtensions
                    .Concat(EditorSettings.projectGenerationUserExtensions)
                    .Concat(customExtensions)
                    .Distinct().ToArray();
            }
        }

        static string[] HandledExtensions
        {
            get
            {
                return HandledExtensionsString
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.TrimStart('.', '*'))
                    .ToArray();
            }
        }

        static string HandledExtensionsString
        {
            get => EditorPrefs.GetString(nvim_userExtensions, string.Join(";", defaultExtensions));
            set => EditorPrefs.SetString(nvim_userExtensions, value);
        }

        public string ReplaceTemplate(String templateStr, String pipePath, String path, int line, int column)
        {
            templateStr = templateStr.Replace("${pipePath}", pipePath);
            templateStr = templateStr.Replace("${filePath}", path);
            templateStr = templateStr.Replace("${line}", Math.Max(line, 1).ToString());
            templateStr = templateStr.Replace("${column}", Math.Max(column, 0).ToString());
            return templateStr;
        }

        public bool TryGetInstallationForPath(string editorPath, out CodeEditor.Installation installation)
        {
            installation = default;
            if (Installations == null || Installations.Length == 0)
            {
                return false;
            }

            installation = Installations.FirstOrDefault(install => install.Path == editorPath);
            return !string.IsNullOrEmpty(installation.Name);
        }

        public void OnGUI()
        {
            ServerCmd = EditorGUILayout.TextField("Server Cmd", ServerCmd);
            ServerArgs = EditorGUILayout.TextField("Server Arguments", ServerArgs);
            ClientCmd = EditorGUILayout.TextField("Client Cmd", ClientCmd);
            ClientArgs = EditorGUILayout.TextField("Client Arguments", ClientArgs);
            RemoteCmd = EditorGUILayout.TextField("Remote Cmd", RemoteCmd);
            RemoteArgs = EditorGUILayout.TextField("Remote Arguments", RemoteArgs);
            if (GUILayout.Button(k_ResetArguments, GUILayout.Width(120)))
            {
                ServerCmd = DefaultServerCmd;
                ServerArgs = DefaultServerArgs;
                ClientCmd = DefaultClientCmd;
                ClientArgs = DefaultClientArgs;
                RemoteCmd = DefaultRemoteCmd;
                RemoteArgs = DefaultRemoteArgs;
            }

            EditorGUILayout.LabelField("Generate .csproj files for:");
            EditorGUI.indentLevel++;
            SettingsButton(ProjectGenerationFlag.Embedded, "Embedded packages", "");
            SettingsButton(ProjectGenerationFlag.Local, "Local packages", "");
            SettingsButton(ProjectGenerationFlag.Registry, "Registry packages", "");
            SettingsButton(ProjectGenerationFlag.Git, "Git packages", "");
            SettingsButton(ProjectGenerationFlag.BuiltIn, "Built-in packages", "");
#if UNITY_2019_3_OR_NEWER
            SettingsButton(ProjectGenerationFlag.LocalTarBall, "Local tarball", "");
#endif
            SettingsButton(ProjectGenerationFlag.Unknown, "Packages from unknown sources", "");
            RegenerateProjectFiles();
            EditorGUI.indentLevel--;

            HandledExtensionsString = EditorGUILayout.TextField(new GUIContent("Extensions handled: "), HandledExtensionsString);
        }

        void RegenerateProjectFiles()
        {
            var rect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(new GUILayoutOption[] { }));
            rect.width = 252;
            if (GUI.Button(rect, "Regenerate project files"))
            {
                m_ProjectGeneration.Sync();
            }
        }

        void SettingsButton(ProjectGenerationFlag preference, string guiMessage, string toolTip)
        {
            var prevValue = m_ProjectGeneration.AssemblyNameProvider.ProjectGenerationFlag.HasFlag(preference);
            var newValue = EditorGUILayout.Toggle(new GUIContent(guiMessage, toolTip), prevValue);
            if (newValue != prevValue)
            {
                m_ProjectGeneration.AssemblyNameProvider.ToggleProjectGeneration(preference);
            }
        }

        public void CreateIfDoesntExist()
        {
            if (!m_ProjectGeneration.SolutionExists())
            {
                m_ProjectGeneration.Sync();
            }
        }

        public void SyncIfNeeded(string[] addedFiles, string[] deletedFiles, string[] movedFiles, string[] movedFromFiles, string[] importedFiles)
        {
            (m_ProjectGeneration.AssemblyNameProvider as IPackageInfoCache)?.ResetPackageInfoCache();
            m_ProjectGeneration.SyncIfNeeded(addedFiles.Union(deletedFiles).Union(movedFiles).Union(movedFromFiles).ToList(), importedFiles);
        }

        public void SyncAll()
        {
            (m_ProjectGeneration.AssemblyNameProvider as IPackageInfoCache)?.ResetPackageInfoCache();
            AssetDatabase.Refresh();
            m_ProjectGeneration.Sync();
        }

        public bool OpenProject(string path, int line, int column)
        {
            if (path != "" && (!SupportsExtension(path) || !File.Exists(path))) // Assets - Open C# Project passes empty path here
            {
                return false;
            }

            int projectHash = Math.Abs(Directory.GetCurrentDirectory().GetHashCode());
            #if UNITY_EDITOR_WINDOWS
            var pipePath = $"\\\\.\\pipe\\unity-nvim-ipc-{projectHash}";
            var runningPipes = Directory.GetFiles(@"\\.\pipe\");
            var isServerRunning = runningPipes.Contains(pipePath);
            #else
            var pipePath = $"/tmp/nvimsocket_{projectHash}";
            var isServerRunning = File.Exists(pipePath);
            #endif

            if (!isServerRunning)
            {
                var serverArgs = ReplaceTemplate(ServerArgs, pipePath, path, 1, 0);
                ProcessStartInfo nvimServerStartInfo = new ProcessStartInfo
                {
                    CreateNoWindow = true,
                    Arguments = serverArgs,
                    FileName = ServerCmd,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    WorkingDirectory = Directory.GetCurrentDirectory()
                };
                Process.Start(nvimServerStartInfo);
            }

            var nvrParams = ReplaceTemplate(RemoteArgs, pipePath, path, line, column);

            ProcessStartInfo nvrStartInfo = new ProcessStartInfo
            {
                CreateNoWindow = true,
                Arguments = nvrParams,
                FileName = RemoteCmd,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            Process.Start(nvrStartInfo);

            Process process = Process.GetProcesses().FirstOrDefault(x => x.Id == EditorPid);

            if (process == null || process.HasExited)
            {
                var nvimArgs = ClientArgs;
                nvimArgs = ReplaceTemplate(nvimArgs, pipePath, path, 0, 0);

                ProcessStartInfo editorStartInfo = new ProcessStartInfo
                {
                    Arguments = nvimArgs,
                    FileName = ClientCmd,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                Process editorProcess = new Process();
                editorProcess.EnableRaisingEvents = true;
                editorProcess.StartInfo = editorStartInfo;
                editorProcess.Start();
                EditorPid = editorProcess.Id;
            }
            else
            {
                // ForceForegroundWindow(process.MainWindowHandle);
            }

            return true;

            // if (line == -1)
            //     line = 1;
            // if (column == -1)
            //     column = 0;

            // string arguments;
            // if (Arguments != DefaultNvrArgs)
            // {
            //     arguments = m_ProjectGeneration.ProjectDirectory != path
            //         ? CodeEditor.ParseArgument(Arguments, path, line, column)
            //         : m_ProjectGeneration.ProjectDirectory;
            // }
            // else
            // {
            //     arguments = $@"""{m_ProjectGeneration.ProjectDirectory}""";
            //     if (m_ProjectGeneration.ProjectDirectory != path && path.Length != 0)
            //     {
            //         arguments += $@" -g ""{path}"":{line}:{column}";
            //     }
            // }

            // if (IsOSX)
            // {
            //     return OpenOSX(arguments);
            // }

            // var app = DefaultApp;
            // var process = new Process
            // {
            //     StartInfo = new ProcessStartInfo
            //     {
            //         FileName = app,
            //         Arguments = arguments,
            //         WindowStyle = app.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
            //         CreateNoWindow = true,
            //         UseShellExecute = true,
            //     }
            // };

            // process.Start();
            // return true;
        }

        static bool OpenOSX(string arguments)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"-n \"{DefaultApp}\" --args {arguments}",
                    UseShellExecute = true,
                }
            };

            process.Start();
            return true;
        }

        static bool SupportsExtension(string path)
        {
            var extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension))
                return false;
            return HandledExtensions.Contains(extension.TrimStart('.'));
        }

        public CodeEditor.Installation[] Installations => installations;

        protected static CodeEditor.Installation[] installations;

        public NvimScriptEditor(IGenerator projectGeneration)
        {
            m_ProjectGeneration = projectGeneration;
        }

        static NvimScriptEditor()
        {
            installations = new CodeEditor.Installation[]
            {
                new CodeEditor.Installation { Name = "Neovim", Path = "/" }
            };
            var editor = new NvimScriptEditor(new ProjectGeneration(Directory.GetParent(Application.dataPath).FullName));
            CodeEditor.Register(editor);
            editor.CreateIfDoesntExist();
        }

        public void Initialize(string editorInstallationPath) { }
    }
}
