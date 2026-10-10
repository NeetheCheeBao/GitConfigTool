using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GitConfigTool.Helpers;
using GitConfigTool.Models;

namespace GitConfigTool.Services
{
    /// <summary>
    /// Git 命令执行与配置读写服务
    /// </summary>
    public class GitService
    {
        private readonly string _gitPath;

        /// <summary>探测失败时的原因，供界面提示</summary>
        public string DiscoveryError { get; private set; }

        public GitService()
        {
            _gitPath = FindGitExecutable();
            if (string.IsNullOrEmpty(_gitPath))
                DiscoveryError = ExecutableLocator.LastWarning ?? "未找到 git，请确认已安装 Git for Windows。";
        }

        /// <summary>
        /// 查找 git 可执行文件的真实路径（注册表 / where.exe / PATH）
        /// </summary>
        private string FindGitExecutable()
        {
            // Git for Windows 会写 GitForWindows 注册表项
            return ExecutableLocator.Locate("git.exe", "GitForWindows");
        }

        /// <summary>
        /// 执行 git 命令并返回输出
        /// </summary>
        public Task<(int ExitCode, string StdOut, string StdErr)> RunGitAsync(string arguments, string workingDirectory = null)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrEmpty(_gitPath))
                    return (-1, string.Empty, DiscoveryError ?? "未找到 git 可执行文件");

                return ExecutableLocator.Run(_gitPath, arguments, workingDirectory, 60000);
            });
        }

        /// <summary>探测到的 git 可执行文件完整路径（供界面显示，便于排查）</summary>
        public string GitExecutablePath => _gitPath;

        /// <summary>
        /// 检查 Git 是否可用
        /// </summary>
        public async Task<(bool Ok, string Version)> CheckGitAvailableAsync()
        {
            if (string.IsNullOrEmpty(_gitPath))
                return (false, DiscoveryError ?? "未找到 git 可执行文件");

            var result = await RunGitAsync("--version");
            if (result.ExitCode == 0)
                return (true, result.StdOut);
            return (false, result.StdErr.Length > 0 ? result.StdErr : "git 无法执行");
        }

        /// <summary>
        /// 读取全局 git config 中的指定键
        /// </summary>
        public async Task<string> GetConfigAsync(string key, string scope = "global")
        {
            string scopeArg = scope == "system" ? "--system" : scope == "local" ? "--local" : "--global";
            var result = await RunGitAsync($"config {scopeArg} --get {key}");
            return result.ExitCode == 0 ? result.StdOut : null;
        }

        /// <summary>
        /// 设置全局 git config 键值
        /// </summary>
        public async Task<(bool Success, string Message)> SetConfigAsync(string key, string value, string scope = "global")
        {
            string scopeArg = scope == "system" ? "--system" : scope == "local" ? "--local" : "--global";
            var result = await RunGitAsync($"config {scopeArg} --replace-all {key} \"{value}\"");
            if (result.ExitCode == 0)
                return (true, "设置成功");
            return (false, result.StdErr);
        }

        /// <summary>
        /// 删除配置项
        /// </summary>
        public async Task<(bool Success, string Message)> UnsetConfigAsync(string key, string scope = "global")
        {
            string scopeArg = scope == "system" ? "--system" : scope == "local" ? "--local" : "--global";
            var result = await RunGitAsync($"config {scopeArg} --unset-all {key}");
            if (result.ExitCode == 0 || result.ExitCode == 5)
                return (true, "已删除");
            return (false, result.StdErr);
        }

        /// <summary>
        /// 列出所有全局配置
        /// </summary>
        public async Task<List<GitConfigItem>> ListConfigAsync(string scope = "global")
        {
            string scopeArg = scope == "system" ? "--system" : scope == "local" ? "--local" : "--global";
            var result = await RunGitAsync($"config {scopeArg} --list");
            var list = new List<GitConfigItem>();
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StdOut))
                return list;

            foreach (var line in result.StdOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = line.IndexOf('=');
                if (idx <= 0) continue;
                var fullKey = line.Substring(0, idx);
                var value = line.Substring(idx + 1);
                var lastDot = fullKey.LastIndexOf('.');
                string section = lastDot > 0 ? fullKey.Substring(0, lastDot) : string.Empty;
                string key = lastDot > 0 ? fullKey.Substring(lastDot + 1) : fullKey;
                list.Add(new GitConfigItem
                {
                    Section = section,
                    Key = key,
                    Value = value,
                    Scope = scope,
                    IsModified = false
                });
            }
            return list;
        }

        /// <summary>
        /// 读取当前生效的用户信息
        /// </summary>
        public async Task<(string Name, string Email, string SigningKey, string SigningFormat,
                           bool CommitSign, bool TagSign, string CredentialHelper, string SslBackend)>
            GetCurrentUserAsync()
        {
            var name = await GetConfigAsync("user.name");
            var email = await GetConfigAsync("user.email");
            var signingKey = await GetConfigAsync("user.signingkey");
            var signingFormat = await GetConfigAsync("gpg.format") ?? "openpgp";

            var commitSignStr = await GetConfigAsync("commit.gpgsign");
            bool commitSign = string.Equals(commitSignStr, "true", StringComparison.OrdinalIgnoreCase);

            var tagSignStr = await GetConfigAsync("tag.gpgsign");
            bool tagSign = string.Equals(tagSignStr, "true", StringComparison.OrdinalIgnoreCase);

            var credentialHelper = await GetConfigAsync("credential.helper");
            var sslBackend = await GetConfigAsync("http.sslBackend");

            return (name, email, signingKey, signingFormat, commitSign, tagSign, credentialHelper, sslBackend);
        }

        /// <summary>
        /// 应用档案到全局 gitconfig
        /// </summary>
        public async Task<(bool Success, string Message)> ApplyProfileAsync(GitProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.UserName) || string.IsNullOrWhiteSpace(profile.Email))
                return (false, "用户名和邮箱不能为空");

            var results = new List<string>();

            var r1 = await SetConfigAsync("user.name", profile.UserName);
            if (!r1.Success) return r1;
            results.Add("user.name");

            var r2 = await SetConfigAsync("user.email", profile.Email);
            if (!r2.Success) return r2;
            results.Add("user.email");

            if (!string.IsNullOrWhiteSpace(profile.SigningKey))
            {
                await SetConfigAsync("user.signingkey", profile.SigningKey);
                results.Add("user.signingkey");
            }
            else
            {
                await UnsetConfigAsync("user.signingkey");
            }

            if (!string.IsNullOrWhiteSpace(profile.SigningFormat))
            {
                await SetConfigAsync("gpg.format", profile.SigningFormat);
                results.Add("gpg.format");
            }

            await SetConfigAsync("commit.gpgsign", profile.CommitGpgsign ? "true" : "false");
            results.Add("commit.gpgsign");

            await SetConfigAsync("tag.gpgsign", profile.TagGpgsign ? "true" : "false");
            results.Add("tag.gpgsign");

            if (!string.IsNullOrWhiteSpace(profile.CredentialHelper))
            {
                await SetConfigAsync("credential.helper", profile.CredentialHelper);
                results.Add("credential.helper");
            }

            // HTTPS 后端：留空表示不修改（避免误改现有值）
            if (!string.IsNullOrWhiteSpace(profile.SslBackend))
            {
                await SetConfigAsync("http.sslBackend", profile.SslBackend);
                results.Add("http.sslBackend");
            }

            return (true, $"已应用档案「{profile.UserName}」，更新项：" + string.Join(", ", results));
        }


        /// <summary>
        /// 获取全局 gitconfig 文件路径
        /// </summary>
        public string GetGlobalConfigPath()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".gitconfig");
        }
    }
}
