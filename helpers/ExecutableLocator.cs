using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GitConfigTool.Helpers
{
    /// <summary>
    /// 外部命令（git / gh）定位工具，按三层依次探测：
    ///   1) 注册表记录的安装位置（Git for Windows 会写，GitHub CLI 不写）
    ///   2) where.exe（Windows 原生解析器）
    ///   3) 系统 PATH 逐目录查找
    /// 每个候选都会实际执行一次 --version 验证。
    /// </summary>
    public static class ExecutableLocator
    {
        /// <summary>每个候选的最大执行等待时间（毫秒）</summary>
        private const int PerCandidateTimeoutMs = 4000;

        /// <summary>找不到时的警告信息（用于界面提示）</summary>
        public static string LastWarning { get; private set; }

        /// <summary>
        /// 探测可执行文件的真实路径，找不到返回 null
        /// </summary>
        /// <param name="exeName">可执行文件名，例如 git.exe / gh.exe</param>
        /// <param name="registryKey">该软件记录安装位置的注册表项（可为空）</param>
        public static string Locate(string exeName, string registryKey)
        {
            LastWarning = null;

            // 1) 注册表记录的安装路径
            foreach (var path in FromRegistry(exeName, registryKey))
            {
                if (IsValid(path)) return path;
            }

            // 2) where.exe（Windows 原生）
            var where = FromWhere(exeName);
            if (IsValid(where)) return where;

            // 3) PATH 逐目录
            foreach (var path in FromPath(exeName))
            {
                if (IsValid(path)) return path;
            }

            LastWarning = "未在注册表、where.exe 及 PATH 中找到 " + exeName +
                          "。若已安装，请确认安装时勾选了“加入 PATH”。";
            return null;
        }

        /// <summary>
        /// 定位 Git 自带的工具（ssh-keygen / gpg / ssh 等）：
        /// 先从注册表记录的 Git 安装根目录派生，再回退到通用探测。
        /// </summary>
        /// <param name="exeName">工具文件名，例如 ssh-keygen.exe</param>
        public static string LocateGitTool(string exeName)
        {
            LastWarning = null;

            // Git for Windows 把工具放在 usr\bin 与 mingw64\bin 下
            foreach (var root in GitInstallRoots())
            {
                foreach (var sub in new[] { @"usr\bin", @"mingw64\bin", @"mingw32\bin", "bin" })
                {
                    var candidate = Path.Combine(root, sub, exeName);
                    if (IsValid(candidate)) return candidate;
                }
            }

            // 回退：通用探测（PATH / where）
            return Locate(exeName, null);
        }

        /// <summary>
        /// 枚举本机 Git 安装根目录（来自注册表，可能多个）
        /// </summary>
        private static IEnumerable<string> GitInstallRoots()
        {
            var roots = new List<string>();

            var keys = new[]
            {
                @"SOFTWARE\GitForWindows",
                @"SOFTWARE\WOW6432Node\GitForWindows"
            };
            var rootsRegistry = new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser };

            foreach (var regRoot in rootsRegistry)
            {
                foreach (var key in keys)
                {
                    try
                    {
                        using (var sub = regRoot.OpenSubKey(key))
                        {
                            if (sub == null) continue;
                            var p = sub.GetValue("InstallPath") as string;
                            if (string.IsNullOrWhiteSpace(p)) continue;
                            p = Unquote(p).TrimEnd('\\', '/');
                            if (p.Length > 0 && !roots.Contains(p)) roots.Add(p);
                        }
                    }
                    catch { }
                }
            }

            return roots;
        }

        /// <summary>
        /// 运行程序并取版本号，成功时返回 true 并把版本写入 version
        /// </summary>
        public static bool TryRunVersion(string exePath, out string version)
        {
            version = null;

            // 大多数工具支持 --version
            var r = Run(exePath, "--version", null, PerCandidateTimeoutMs);
            if (r.ExitCode == 0)
            {
                var text = (r.StdOut ?? string.Empty).Trim();
                if (text.Length == 0) text = (r.StdErr ?? string.Empty).Trim();
                if (text.Length > 0)
                {
                    var nl = text.IndexOf('\n');
                    version = (nl >= 0 ? text.Substring(0, nl) : text).Trim();
                    if (version.Length > 0) return true;
                }
            }

            // 部分工具（如 ssh-keygen）不认 --version，改用 -? 探测；
            // 这类工具通常把用法输出到 stderr 并非零退出，因此只要有输出即视为可用
            foreach (var probe in new[] { "-?", "-V" })
            {
                var r2 = Run(exePath, probe, null, PerCandidateTimeoutMs);
                var out2 = (r2.StdOut ?? string.Empty).Trim();
                var err2 = (r2.StdErr ?? string.Empty).Trim();

                if (out2.Length == 0 && err2.Length == 0) continue;
                if (err2.IndexOf("not recognized", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                version = "可用（" + Path.GetFileName(exePath) + "）";
                return true;
            }

            return false;
        }

        /// <summary>
        /// 执行外部命令并捕获输出；返回 (-1, "", 错误) 表示启动失败
        /// </summary>
        public static (int ExitCode, string StdOut, string StdErr) Run(
            string fileName, string arguments, string workingDirectory, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                if (!string.IsNullOrEmpty(workingDirectory))
                    psi.WorkingDirectory = workingDirectory;

                using (var proc = Process.Start(psi))
                {
                    if (proc == null)
                        return (-1, string.Empty, "无法启动进程: " + fileName);

                    var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                    var stderrTask = proc.StandardError.ReadToEndAsync();

                    if (!proc.WaitForExit(timeoutMs))
                    {
                        try { proc.Kill(); } catch { }
                        return (-1, string.Empty, "执行超时: " + fileName);
                    }

                    // 进程已退出，输出流已读完
                    return (proc.ExitCode, (stdoutTask.Result ?? string.Empty).Trim(), (stderrTask.Result ?? string.Empty).Trim());
                }
            }
            catch (Exception ex)
            {
                return (-1, string.Empty, ex.Message);
            }
        }

        /// <summary>
        /// 执行 --version 验证该文件是否可用
        /// </summary>
        private static bool IsValid(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (!File.Exists(path)) return false;
            string version;
            return TryRunVersion(path, out version);
        }

        /// <summary>
        /// 去掉 Windows 会剥离的引号
        /// </summary>
        private static string Unquote(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                s = s.Substring(1, s.Length - 2);
            return s.Trim();
        }

        /// <summary>
        /// 从注册表读取安装位置（Git for Windows 会写 InstallPath）
        /// </summary>
        private static IEnumerable<string> FromRegistry(string exeName, string registryKey)
        {
            if (string.IsNullOrEmpty(registryKey)) yield break;

            var keys = new[]
            {
                @"SOFTWARE\" + registryKey,
                @"SOFTWARE\WOW6432Node\" + registryKey
            };
            var valueNames = new[] { "InstallPath", "InstallLocation", "Path" };
            var roots = new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser };

            foreach (var root in roots)
            {
                foreach (var key in keys)
                {
                    string installPath = null;
                    try
                    {
                        using (var sub = root.OpenSubKey(key))
                        {
                            if (sub == null) continue;
                            foreach (var name in valueNames)
                            {
                                var v = sub.GetValue(name) as string;
                                if (!string.IsNullOrWhiteSpace(v)) { installPath = v; break; }
                            }
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(installPath)) continue;

                    var baseDir = Unquote(installPath).TrimEnd('\\', '/');
                    if (baseDir.Length == 0) continue;

                    // Git for Windows：cmd 目录下是命令行入口；安装根目录也可能直接有 exe
                    yield return Path.Combine(baseDir, "cmd", exeName);
                    yield return Path.Combine(baseDir, "bin", exeName);
                    yield return Path.Combine(baseDir, exeName);
                }
            }
        }

        /// <summary>
        /// 使用 where.exe 定位（Windows 原生解析器）
        /// </summary>
        private static string FromWhere(string exeName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = exeName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return null;
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.StandardError.ReadToEnd();
                    if (!proc.WaitForExit(5000))
                    {
                        try { proc.Kill(); } catch { }
                        return null;
                    }
                    if (proc.ExitCode != 0) return null;

                    foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var p = Unquote(line);
                        if (p.Length > 0) return p;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 遍历 PATH 环境变量中的每个目录
        /// </summary>
        private static IEnumerable<string> FromPath(string exeName)
        {
            var pathVar = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVar)) yield break;

            foreach (var raw in pathVar.Split(';'))
            {
                var dir = Unquote(raw).TrimEnd('\\', '/');
                if (dir.Length == 0) continue;
                yield return Path.Combine(dir, exeName);
            }
        }
    }
}
