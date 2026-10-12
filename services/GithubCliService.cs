using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GitConfigTool.Helpers;
using GitConfigTool.Models;

namespace GitConfigTool.Services
{
    /// <summary>
    /// GitHub CLI (gh) 账号管理服务
    /// </summary>
    public class GithubCliService
    {
        private readonly string _ghPath;

        /// <summary>探测失败时的原因，供界面提示</summary>
        public string DiscoveryError { get; private set; }

        /// <summary>探测到的 gh 可执行文件完整路径（供界面显示）</summary>
        public string GhExecutablePath => _ghPath;

        public GithubCliService()
        {
            _ghPath = FindGhExecutable();
            if (string.IsNullOrEmpty(_ghPath))
                DiscoveryError = ExecutableLocator.LastWarning ?? "未找到 GitHub CLI (gh)，请先安装。";
        }

        /// <summary>
        /// 查找 gh 可执行文件的真实路径。
        /// 注意：GitHub CLI 不写注册表，因此只能靠 where.exe 与 PATH。
        /// </summary>
        private string FindGhExecutable()
        {
            return ExecutableLocator.Locate("gh.exe", null);
        }

        public Task<(int ExitCode, string StdOut, string StdErr)> RunGhAsync(string arguments)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrEmpty(_ghPath))
                    return (-1, string.Empty, DiscoveryError ?? "未找到 gh 可执行文件");

                return ExecutableLocator.Run(_ghPath, arguments, null, 60000);
            });
        }

        public async Task<(bool Ok, string Version)> CheckAvailableAsync()
        {
            if (string.IsNullOrEmpty(_ghPath))
                return (false, DiscoveryError ?? "未找到 gh 可执行文件");

            var result = await RunGhAsync("--version");
            if (result.ExitCode == 0)
            {
                var lines = result.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                return (true, lines.Length > 0 ? lines[0] : result.StdOut);
            }
            return (false, result.StdErr.Length > 0 ? result.StdErr : "gh 无法执行");
        }

        public async Task<List<GithubAccount>> GetLoggedInAccountsAsync()
        {
            var accounts = new List<GithubAccount>();

            var result = await RunGhAsync("auth status");
            string output = result.StdOut + "\n" + result.StdErr;

            // 按主机名分块（主机名是行首的非空白行）
            var hostBlocks = Regex.Split(output, @"(?=^\S)", RegexOptions.Multiline);
            foreach (var block in hostBlocks)
            {
                if (string.IsNullOrWhiteSpace(block)) continue;

                // 块首行是主机名
                var hostMatch = Regex.Match(block, @"^([^\s]+)", RegexOptions.Multiline);
                if (!hostMatch.Success) continue;

                var blockHost = hostMatch.Groups[1].Value.Trim();
                if (blockHost.IndexOf("Logged", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (blockHost.StartsWith("\u2713", StringComparison.Ordinal)) continue;
                if (blockHost.StartsWith("X", StringComparison.Ordinal)) continue;

                // 一个主机下可能登录多个账号，逐行解析，每个账号独立取值
                string currentUser = null;

                foreach (var rawLine in block.Split('\n'))
                {
                    var line = rawLine.Trim();

                    // 账号行：gh 会加 ✓ 前缀，如 "✓ Logged in to <host> account <user> (keyring)"
                    var loginMatch = Regex.Match(line, @"Logged in to (\S+) account (\S+)");
                    if (loginMatch.Success)
                    {
                        currentUser = loginMatch.Groups[2].Value;
                    }
                    else
                    {
                        // 兼容旧版 gh 的措辞：Logged in to <host> as <user>
                        var legacyMatch = Regex.Match(line, @"Logged in to (\S+) as (\S+)");
                        if (legacyMatch.Success)
                            currentUser = legacyMatch.Groups[2].Value;
                    }

                    if (currentUser == null) continue;

                    // 该账号的属性行：Active account / Git operations protocol
                    var activeMatch = Regex.Match(line, @"Active account:?\s*(\S+)");
                    if (activeMatch.Success)
                    {
                        bool isActive = string.Equals(activeMatch.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);

                        accounts.Add(new GithubAccount
                        {
                            Host = blockHost,
                            Username = currentUser,
                            IsActive = isActive,
                            Protocol = "https"
                        });
                        currentUser = null;   // 该账号已收尾
                        continue;
                    }

                    var protocolMatch = Regex.Match(line, @"Git operations protocol:\s*(\S+)");
                    if (protocolMatch.Success && accounts.Count > 0)
                    {
                        var last = accounts[accounts.Count - 1];
                        if (last.Username == currentUser)
                            last.Protocol = protocolMatch.Groups[1].Value;
                    }
                }

                // 兜底：只看到账号行、没有 Active 行时也要收进列表
                if (currentUser != null)
                {
                    accounts.Add(new GithubAccount
                    {
                        Host = blockHost,
                        Username = currentUser,
                        IsActive = false,
                        Protocol = "https"
                    });
                }
            }

            if (accounts.Count == 0)
            {
                var api = await RunGhAsync("api user");
                if (api.ExitCode == 0 && !string.IsNullOrWhiteSpace(api.StdOut))
                {
                    var login = ExtractJsonString(api.StdOut, "login");
                    if (!string.IsNullOrEmpty(login))
                    {
                        accounts.Add(new GithubAccount
                        {
                            Username = login,
                            Name = ExtractJsonString(api.StdOut, "name") ?? "",
                            Email = ExtractJsonString(api.StdOut, "email") ?? "",
                            Host = "github.com",
                            IsActive = true
                        });
                    }
                }
            }

            return accounts;
        }

        private static string ExtractJsonString(string json, string key)
        {
            var pattern = "\"" + key + "\"";
            int k = json.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (k < 0) return null;
            int colon = json.IndexOf(':', k + pattern.Length);
            if (colon < 0) return null;
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return null;
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return null;
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }

        public async Task<(bool Success, string Message)> SwitchAccountAsync(string username, string host = "github.com")
        {
            var result = await RunGhAsync($"auth switch --user \"{username}\" --hostname \"{host}\"");
            if (result.ExitCode == 0)
                return (true, result.StdOut.Length > 0 ? result.StdOut : $"已切换到 {username}@{host}");
            return (false, result.StdErr.Length > 0 ? result.StdErr : result.StdOut);
        }

        /// <summary>
        /// gh 登录进程的标准错误输出（设备码与授权 URL 都在这里）。
        /// 由后台线程持续写入，界面线程只读取，避免阻塞。
        /// </summary>
        private volatile string _loginStdErr = string.Empty;

        /// <summary>当前登录进程输出快照</summary>
        public string LoginStdErr => _loginStdErr;

        /// <summary>是否有可用的登录输出流</summary>
        public bool HasLoginStream { get; private set; }

        /// <summary>
        /// 启动登录流程：浏览器设备码授权，并让 gh 把一次性设备码复制到剪贴板。
        /// 不等待进程退出；调用方轮询账号变化判断是否登录成功。
        /// </summary>
        public (bool Success, string Message, Process Process) StartLoginWithClipboard(string host = "github.com", string protocol = "https")
        {
            if (string.IsNullOrEmpty(_ghPath))
                return (false, DiscoveryError ?? "未找到 gh 可执行文件", null);

            _loginStdErr = string.Empty;
            var psi = new ProcessStartInfo
            {
                FileName = _ghPath,
                Arguments = $"auth login --hostname \"{host}\" --git-protocol {protocol} --web --clipboard",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8
            };

            try
            {
                var proc = Process.Start(psi);
                if (proc == null)
                    return (false, "无法启动 gh 进程", null);

                // 逐行异步读取，才能实时拿到设备码与授权 URL
                proc.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    _loginStdErr += e.Data + "\n";
                };
                proc.BeginErrorReadLine();
                HasLoginStream = true;
                return (true, "已在后台启动 gh 登录", proc);
            }
            catch (Exception ex)
            {
                HasLoginStream = false;
                return (false, ex.Message, null);
            }
        }

        /// <summary>
        /// 从 gh 输出中提取一次性设备码（形如 XXXX-XXXX）
        /// </summary>
        public string ExtractDeviceCode()
        {
            var m = Regex.Match(_loginStdErr ?? string.Empty, @"\b([A-Z0-9]{4}-[A-Z0-9]{4})\b");
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// 从 gh 输出中提取授权 URL
        /// </summary>
        public string ExtractVerificationUrl()
        {
            var m = Regex.Match(_loginStdErr ?? string.Empty, @"(https://\S+)");
            return m.Success ? m.Groups[1].Value.TrimEnd('.', ',', ')') : null;
        }

        /// <summary>
        /// gh 的详细输出（用于失败提示）
        /// </summary>
        public string GetLoginOutput() => (_loginStdErr ?? string.Empty).Trim();

        /// <summary>
        /// 退出指定账号：仅移除本地凭据，不会吊销服务端授权
        /// </summary>
        public async Task<(bool Success, string Message)> LogoutAsync(string username, string host = "github.com")
        {
            var result = await RunGhAsync($"auth logout --hostname \"{host}\" --user \"{username}\"");
            if (result.ExitCode == 0)
                return (true, $"已退出登录 {username}@{host}");
            return (false, result.StdErr.Length > 0 ? result.StdErr : result.StdOut);
        }

        public async Task<string> GetAuthStatusRawAsync()
        {
            var result = await RunGhAsync("auth status");
            return (result.StdOut + "\n" + result.StdErr).Trim();
        }
    }
}
