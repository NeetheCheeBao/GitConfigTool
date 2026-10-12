using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using GitConfigTool.Helpers;
using GitConfigTool.Models;
using GitConfigTool.Services;

namespace GitConfigTool.ViewModels
{
    /// <summary>
    /// 主窗口视图模型
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly GitService _gitService = new GitService();
        private readonly GithubCliService _ghService = new GithubCliService();
        private readonly ProfileStorageService _storage = new ProfileStorageService();
        private readonly SshService _sshService = new SshService();

        // 目录监听：检测外部改动后自动刷新
        private FolderWatcher _profilesWatcher;
        private FolderWatcher _gitConfigWatcher;
        private FolderWatcher _sshWatcher;
        private bool _autoRefreshing;


        private string _statusMessage = "就绪";
        private bool _isBusy;
        private string _gitVersion = "检测中...";
        private string _ghVersion = "检测中...";
        private string _gitPath = "";
        private string _ghPath = "";
        private string _currentUserName = "";
        private string _currentUserEmail = "";
        private string _currentSigningKey = "";
        private string _currentSigningFormat = "";
        private bool _currentCommitSign;
        private string _gitConfigPath = "";
        private string _ghAuthStatus = "";
        private bool _isGithubLoggingIn;
        private bool _loginCancelled;
        private Process _loginProcess;
        private string _deviceCode = "";
        private string _githubUrl = "";
        private bool _isDeviceAuthVisible;

        /// <summary>GitHub 设备授权页面（gh 输出里取不到时的兜底地址）</summary>
        private const string DefaultGithubDeviceUrl = "https://github.com/login/device";
        private GitProfile _selectedProfile;
        private GithubAccount _selectedGithubAccount;
        private GitConfigItem _selectedConfigItem;
        private SshListEntry _selectedSshEntry;
        private int _selectedTabIndex;

        public ObservableCollection<GitProfile> Profiles { get; } = new ObservableCollection<GitProfile>();
        public ObservableCollection<GithubAccount> GithubAccounts { get; } = new ObservableCollection<GithubAccount>();
        public ObservableCollection<GitConfigItem> ConfigItems { get; } = new ObservableCollection<GitConfigItem>();

        /// <summary>「当前全局配置」展示的键值行</summary>
        public ObservableCollection<ConfigDisplayRow> CurrentConfigRows { get; } = new ObservableCollection<ConfigDisplayRow>();

        /// <summary>SSH 目录条目列表（签名白名单置顶）</summary>
        public ObservableCollection<SshListEntry> SshEntries { get; } = new ObservableCollection<SshListEntry>();

        /// <summary>当前选中的 SSH 条目</summary>
        public SshListEntry SelectedSshEntry
        {
            get => _selectedSshEntry;
            set { _selectedSshEntry = value; OnPropertyChanged(); }
        }

        /// <summary>创建密钥对时的输入参数</summary>
        public SshKeyRequest KeyRequest { get; set; } = new SshKeyRequest();

        /// <summary>SSH 目录路径（界面显示用）</summary>
        public string SshFolderPath => _sshService.SshFolderPath;

        /// <summary>SSH 目录路径（界面显示，用 %USERPROFILE% 缩写）</summary>
        public string SshFolderPathDisplay => Helpers.PathDisplay.Abbreviate(SshFolderPath);

        /// <summary>是否一个密钥条目都没有（白名单及其占位条目不算，为空时界面显示提示文案）</summary>
        public bool IsSshListEmpty => !SshEntries.Any(e => e.IsRealEntry && !e.IsWhitelist);

        /// <summary>条目列表的状态说明：目录缺失 / 没有条目 / 共 N 项</summary>
        public string SshStatusText
        {
            get
            {
                if (!_sshService.FolderExists) return "未找到 SSH 目录";
                if (IsSshListEmpty) return "没有找到 SSH 密钥";
                return $"共 {SshEntries.Count} 项";
            }
        }

        public GitProfile EditingProfile { get; set; } = new GitProfile();

        /// <summary>应用名称</summary>
        public string AppName => Helpers.AppInfo.Name;

        /// <summary>「关于」页显示的版本文案</summary>
        public string AppVersionText => Helpers.AppInfo.VersionText;

        private string _environmentText = string.Empty;

        /// <summary>
        /// 「关于」页展示的本机环境信息。用纯文本承载，界面放进只读文本框，
        /// 这样既能整段复制，也能用鼠标自由划选其中一部分。
        /// </summary>
        public string EnvironmentText
        {
            get => _environmentText;
            set { _environmentText = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 汇总本机环境信息，供「关于」页展示与一键复制
        /// </summary>
        private void LoadEnvironmentText()
        {
            var lines = new List<string>();
            AddEnvironmentLine(lines, "应用", Helpers.AppInfo.Name + " " + Helpers.AppInfo.Version);
            AddEnvironmentLine(lines, "Git", WithPath(GitVersion, GitPath));
            AddEnvironmentLine(lines, "GitHub CLI", WithPath(GhVersion, GhPath));
            AddEnvironmentLine(lines, "SSH 目录", SshFolderPathDisplay);
            AddEnvironmentLine(lines, "配置文件", GitConfigPathDisplay);
            AddEnvironmentLine(lines, "系统", Environment.OSVersion.VersionString + "　·　" +
                System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);

            EnvironmentText = string.Join(Environment.NewLine, lines);
        }

        private static void AddEnvironmentLine(List<string> lines, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            lines.Add(label + "：" + value.Trim());
        }

        /// <summary>把版本号与可执行文件位置拼成一行</summary>
        private static string WithPath(string version, string path)
        {
            var v = (version ?? string.Empty).Trim();
            var p = (path ?? string.Empty).Trim();
            return p.Length == 0 ? v : v + "　·　" + p;
        }

        /// <summary>
        /// 把本机环境信息整段复制到剪贴板，方便提 issue 时粘贴
        /// </summary>
        private void CopyEnvironmentInfo()
        {
            StatusMessage = ShellHelper.CopyToClipboard(EnvironmentText)
                ? "本机信息已复制到剪贴板"
                : "复制失败，剪贴板被占用";
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        public string GitVersion
        {
            get => _gitVersion;
            set { _gitVersion = value; OnPropertyChanged(); }
        }

        public string GhVersion
        {
            get => _ghVersion;
            set { _ghVersion = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 探测到的 git 可执行文件完整路径（界面显示用）
        /// </summary>
        public string GitPath
        {
            get => _gitPath;
            set { _gitPath = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// 探测到的 gh 可执行文件完整路径（界面显示用）
        /// </summary>
        public string GhPath
        {
            get => _ghPath;
            set { _ghPath = value; OnPropertyChanged(); }
        }

        public string CurrentUserName
        {
            get => _currentUserName;
            set { _currentUserName = value; OnPropertyChanged(); }
        }

        public string CurrentUserEmail
        {
            get => _currentUserEmail;
            set { _currentUserEmail = value; OnPropertyChanged(); }
        }

        public string CurrentSigningKey
        {
            get => _currentSigningKey;
            set { _currentSigningKey = value; OnPropertyChanged(); }
        }

        public string CurrentSigningFormat
        {
            get => _currentSigningFormat;
            set { _currentSigningFormat = value; OnPropertyChanged(); }
        }

        public bool CurrentCommitSign
        {
            get => _currentCommitSign;
            set { _currentCommitSign = value; OnPropertyChanged(); }
        }

        public string GitConfigPath
        {
            get => _gitConfigPath;
            set { _gitConfigPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(GitConfigPathDisplay)); }
        }

        /// <summary>档案数据文件路径（界面显示，用 %APPDATA% 缩写）</summary>
        public string GitConfigPathDisplay => Helpers.PathDisplay.Abbreviate(_gitConfigPath);

        public string GhAuthStatus
        {
            get => _ghAuthStatus;
            set { _ghAuthStatus = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// gh 命令执行记录：每条执行的命令及其原始输出，界面按类型着色显示
        /// </summary>
        public ObservableCollection<LogEntry> LogEntries { get; } = new ObservableCollection<LogEntry>();

        /// <summary>
        /// 登录流程中从 gh 输出里捕获到的一次性设备码
        /// </summary>
        public string DeviceCode
        {
            get => _deviceCode;
            set { _deviceCode = value; OnPropertyChanged(); OnPropertyChanged(nameof(DeviceCodeText)); }
        }

        /// <summary>
        /// GitHub 设备授权页面地址
        /// </summary>
        public string GithubUrl
        {
            get => _githubUrl;
            set { _githubUrl = value; OnPropertyChanged(); OnPropertyChanged(nameof(GithubUrlDisplay)); }
        }

        /// <summary>
        /// 是否显示设备授权面板
        /// </summary>
        public bool IsDeviceAuthVisible
        {
            get => _isDeviceAuthVisible;
            set { _isDeviceAuthVisible = value; OnPropertyChanged(); }
        }

        /// <summary>设备码为空时的占位提示</summary>
        public string DeviceCodeText => string.IsNullOrEmpty(_deviceCode) ? "获取中..." : _deviceCode;

        /// <summary>授权页面地址（为空时用兜底地址）</summary>
        public string GithubUrlDisplay => string.IsNullOrEmpty(_githubUrl) ? DefaultGithubDeviceUrl : _githubUrl;

        public GitProfile SelectedProfile
        {
            get => _selectedProfile;
            set { _selectedProfile = value; OnPropertyChanged(); }
        }

        public GithubAccount SelectedGithubAccount
        {
            get => _selectedGithubAccount;
            set { _selectedGithubAccount = value; OnPropertyChanged(); }
        }

        public GitConfigItem SelectedConfigItem
        {
            get => _selectedConfigItem;
            set { _selectedConfigItem = value; OnPropertyChanged(); }
        }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set { _selectedTabIndex = value; OnPropertyChanged(); }
        }

        public ICommand RefreshCommand { get; }
        public ICommand ApplyProfileCommand { get; }
        public ICommand AddProfileCommand { get; }
        public ICommand EditProfileCommand { get; }
        public ICommand DeleteProfileCommand { get; }
        public ICommand SaveEditingProfileCommand { get; }
        public ICommand CaptureCurrentAsProfileCommand { get; }
        public ICommand SwitchGithubAccountCommand { get; }
        public ICommand SwitchAccountCommand { get; }
        public ICommand LoginGithubCommand { get; }
        public ICommand LogoutGithubAccountCommand { get; }
        public ICommand OpenGithubDevicePageCommand { get; }
        public ICommand CopyDeviceCodeCommand { get; }
        public ICommand CancelLoginCommand { get; }
        public ICommand RefreshGithubCommand { get; }
        public ICommand OpenGitConfigLocationCommand { get; }
        public ICommand OpenProfilesJsonFolderCommand { get; }
        public ICommand SaveConfigItemCommand { get; }
        public ICommand DeleteConfigItemCommand { get; }
        public ICommand RefreshConfigListCommand { get; }
        public ICommand RefreshSshCommand { get; }
        public ICommand OpenSshFolderCommand { get; }
        public ICommand CopyGitConfigPathCommand { get; }
        public ICommand CopySshPathCommand { get; }
        public ICommand AddSshKeyCommand { get; }
        public ICommand CreateSshKeyCommand { get; }
        public ICommand OpenSshEntryCommand { get; }
        public ICommand PreviewSshEntryCommand { get; }
        public ICommand DeleteSshEntryCommand { get; }
        public ICommand ReconcileWhitelistCommand { get; }
        public ICommand CreateWhitelistFileCommand { get; }
        public ICommand CopyEnvironmentCommand { get; }

        public MainViewModel()
        {
            RefreshCommand = new AsyncRelayCommand(async _ => await RefreshAllAsync());
            // 档案操作命令以行内档案为参数，为空时回退到当前选中项
            ApplyProfileCommand = new AsyncRelayCommand(async p => await ApplyProfileAsync(p as GitProfile), p => p is GitProfile);
            AddProfileCommand = new RelayCommand(_ => StartAddProfile());
            EditProfileCommand = new RelayCommand(p => StartEditProfile(p as GitProfile), p => p is GitProfile);
            DeleteProfileCommand = new AsyncRelayCommand(async p => await DeleteProfileAsync(p as GitProfile), p => p is GitProfile);
            SaveEditingProfileCommand = new AsyncRelayCommand(async _ => await SaveEditingProfileAsync());
            CaptureCurrentAsProfileCommand = new AsyncRelayCommand(async _ => await CaptureCurrentAsProfileAsync());
            SwitchGithubAccountCommand = new AsyncRelayCommand(async _ => await SwitchGithubAccountAsync(null), _ => SelectedGithubAccount != null);
            SwitchAccountCommand = new AsyncRelayCommand(
                async p => await SwitchGithubAccountAsync(p as GithubAccount),
                p => p is GithubAccount ga && ga.IsNotActive);
            LoginGithubCommand = new AsyncRelayCommand(async _ => await LoginGithubAsync());
            LogoutGithubAccountCommand = new AsyncRelayCommand(async p => await LogoutGithubAccountAsync(p as GithubAccount));
            OpenGithubDevicePageCommand = new RelayCommand(_ => OpenGithubDevicePage());
            CopyDeviceCodeCommand = new RelayCommand(_ => CopyDeviceCode());
            CancelLoginCommand = new RelayCommand(_ => CancelGithubLogin());
            RefreshGithubCommand = new AsyncRelayCommand(async _ => await RefreshGithubFromLogAsync("手动刷新"));
            OpenGitConfigLocationCommand = new RelayCommand(_ => OpenGitConfigLocation());
            OpenProfilesJsonFolderCommand = new RelayCommand(_ => OpenProfilesJsonFolder());
            SaveConfigItemCommand = new AsyncRelayCommand(async _ => await SaveSelectedConfigItemAsync(), _ => SelectedConfigItem != null);
            DeleteConfigItemCommand = new AsyncRelayCommand(async _ => await DeleteSelectedConfigItemAsync(), _ => SelectedConfigItem != null);
            RefreshConfigListCommand = new AsyncRelayCommand(async _ => await LoadConfigListAsync());
            RefreshSshCommand = new RelayCommand(_ => LoadSshEntries());
            OpenSshFolderCommand = new RelayCommand(_ => OpenSshFolder());
            CopyGitConfigPathCommand = new RelayCommand(_ => CopyPathToClipboard(GitConfigPathDisplay));
            CopySshPathCommand = new RelayCommand(_ => CopyPathToClipboard(SshFolderPathDisplay));
            // 「创建密钥对」先弹对话框，确认后由界面回调 CreateSshKeyCommand 真正执行
            AddSshKeyCommand = new RelayCommand(_ => StartCreateSshKey());
            CreateSshKeyCommand = new AsyncRelayCommand(async _ => await CreateSshKeyAsync());

            // 行内按钮：预览（只读）/ 编辑（可改）/ 删除，条目本身作为参数传进来
            OpenSshEntryCommand = new RelayCommand(parameter => OpenSshEntry(parameter as SshListEntry, false));
            PreviewSshEntryCommand = new RelayCommand(parameter => OpenSshEntry(parameter as SshListEntry, true));
            DeleteSshEntryCommand = new AsyncRelayCommand(
                async parameter => await DeleteSshEntryAsync(parameter as SshListEntry));
            CreateWhitelistFileCommand = new RelayCommand(_ => CreateWhitelistFile());
            ReconcileWhitelistCommand = new RelayCommand(_ => StartReconcileWhitelist());
            CopyEnvironmentCommand = new RelayCommand(_ => CopyEnvironmentInfo());
        }

        public async Task InitializeAsync()
        {
            IsBusy = true;
            StatusMessage = "正在加载...";
            try
            {
                AppendCommandLog("程序启动，开始读取本机状态");

                var gitCheck = await _gitService.CheckGitAvailableAsync();
                GitVersion = gitCheck.Ok ? gitCheck.Version : "未安装或不可用";
                GitPath = _gitService.GitExecutablePath ?? "";
                AppendCommandLine("git --version");
                AppendResultLog("-> " + GitVersion);
                if (!string.IsNullOrEmpty(GitPath))
                    AppendResultLog("-> 位置: " + GitPath);
                else
                    AppendErrorLog("-> " + (_gitService.DiscoveryError ?? "未找到 git"));

                var ghCheck = await _ghService.CheckAvailableAsync();
                GhVersion = ghCheck.Ok ? ghCheck.Version : "未安装或不可用";
                GhPath = _ghService.GhExecutablePath ?? "";
                AppendCommandLine("gh --version");
                AppendResultLog("-> " + GhVersion);
                if (!string.IsNullOrEmpty(GhPath))
                    AppendResultLog("-> 位置: " + GhPath);
                else
                    AppendErrorLog("-> " + (_ghService.DiscoveryError ?? "未找到 gh"));

                await LoadProfilesAsync();
                await RefreshCurrentUserAsync();
                await LoadConfigListAsync();
                await RefreshGithubFromLogAsync("启动时读取 gh 账号列表");
                CleanupOrphanedCredentials();

                // 数据文件从 profiles.json 迁移到 GitConfig.json 时给出提示
                if (!string.IsNullOrEmpty(_storage.MigrationNote))
                    AppendCommandLog(_storage.MigrationNote);

                await LoadProfilesJsonAsync();
                LoadSshEntries();
                LoadEnvironmentText();

                StartWatching();

                StatusMessage = "加载完成";
            }
            catch (Exception ex)
            {
                AppendErrorLog("初始化失败: " + ex.Message);
                StatusMessage = "初始化失败: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RefreshAllAsync()
        {
            IsBusy = true;
            StatusMessage = "刷新中...";
            try
            {
                AppendCommandLog("刷新全部：开始重新读取本机状态");
                await LoadProfilesAsync();
                await RefreshCurrentUserAsync();
                await LoadConfigListAsync();
                await RefreshGithubFromLogAsync("刷新全部");
                await LoadProfilesJsonAsync();
                LoadSshEntries();
                LoadEnvironmentText();
                AppendCommandLog("刷新全部：完成");
                StatusMessage = "刷新完成 " + DateTime.Now.ToString("HH:mm:ss");
            }
            catch (Exception ex)
            {
                AppendErrorLog("刷新失败: " + ex.Message);
                StatusMessage = "刷新失败: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 监听配置文件、全局 .gitconfig、SSH 目录的外部变化，自动刷新，省去手动点刷新
        /// </summary>
        private void StartWatching()
        {
            if (_profilesWatcher != null) return;   // 只建一次

            _profilesWatcher = new FolderWatcher(
                System.IO.Path.GetDirectoryName(_storage.StoragePath), "*.json", OnProfilesFileChanged);

            _gitConfigWatcher = new FolderWatcher(
                System.IO.Path.GetDirectoryName(_gitService.GetGlobalConfigPath()), ".gitconfig", OnGitConfigFileChanged);

            _sshWatcher = new FolderWatcher(
                _sshService.SshFolderPath, "*", OnSshFolderChanged);
        }

        /// <summary>档案数据文件被外部改动</summary>
        private void OnProfilesFileChanged()
        {
            if (IsBusy || _autoRefreshing) return;   // 程序自己在操作，结束后会自行刷新
            _ = ReloadProfilesFromWatcherAsync();
        }

        private async Task ReloadProfilesFromWatcherAsync()
        {
            _autoRefreshing = true;
            try
            {
                await LoadProfilesAsync();
                await LoadProfilesJsonAsync();
                StatusMessage = "配置文件已变化，自动刷新 " + DateTime.Now.ToString("HH:mm:ss");
            }
            catch (Exception ex)
            {
                AppendErrorLog("自动刷新失败: " + ex.Message);
            }
            finally
            {
                _autoRefreshing = false;
            }
        }

        /// <summary>全局 .gitconfig 被外部改动</summary>
        private void OnGitConfigFileChanged()
        {
            if (IsBusy || _autoRefreshing) return;
            _ = ReloadGitConfigFromWatcherAsync();
        }

        private async Task ReloadGitConfigFromWatcherAsync()
        {
            _autoRefreshing = true;
            try
            {
                await RefreshCurrentUserAsync();
                await LoadProfilesAsync();
                StatusMessage = "git 配置已变化，自动刷新 " + DateTime.Now.ToString("HH:mm:ss");
            }
            catch (Exception ex)
            {
                AppendErrorLog("自动刷新失败: " + ex.Message);
            }
            finally
            {
                _autoRefreshing = false;
            }
        }

        /// <summary>SSH 目录被外部改动</summary>
        private void OnSshFolderChanged()
        {
            if (IsBusy) return;

            LoadSshEntries();
            StatusMessage = "SSH 目录已变化，自动刷新 " + DateTime.Now.ToString("HH:mm:ss");
        }

        public void Dispose()
        {
            _profilesWatcher?.Dispose();
            _gitConfigWatcher?.Dispose();
            _sshWatcher?.Dispose();
            _profilesWatcher = null;
            _gitConfigWatcher = null;
            _sshWatcher = null;
        }

        private async Task LoadProfilesAsync()
        {
            var list = await _storage.LoadProfilesAsync();
            Profiles.Clear();
            // 按创建时间升序，新建的档案排在最后
            foreach (var p in list.OrderBy(x => x.CreatedAt))
                Profiles.Add(p);

            var user = await _gitService.GetCurrentUserAsync();
            foreach (var p in Profiles)
                p.IsActive = MatchesCurrent(p, user);
        }

        /// <summary>
        /// 判断档案是否与当前全局配置完全一致（即「使用中」）。
        /// 只比对「应用此档案」真正会写入的项：
        /// 姓名、邮箱、两个签名开关必定写入；密钥、签名格式、凭据助手、HTTPS 后端
        /// 在档案里为空时不会被修改，所以那几项只在档案有值时才比对。
        /// </summary>
        private static bool MatchesCurrent(GitProfile profile,
            (string Name, string Email, string SigningKey, string SigningFormat,
             bool CommitSign, bool TagSign, string CredentialHelper, string SslBackend) user)
        {
            if (!TextEquals(profile.UserName, user.Name)) return false;
            if (!TextEquals(profile.Email, user.Email)) return false;
            if (profile.CommitGpgsign != user.CommitSign) return false;
            if (profile.TagGpgsign != user.TagSign) return false;

            // 档案里没有签名密钥时，应用动作会把全局的签名密钥清掉，所以当前也必须为空
            if (!TextEquals(profile.SigningKey, user.SigningKey)) return false;

            if (!string.IsNullOrWhiteSpace(profile.SigningFormat) &&
                !TextEquals(profile.SigningFormat, user.SigningFormat)) return false;

            if (!string.IsNullOrWhiteSpace(profile.CredentialHelper) &&
                !TextEquals(profile.CredentialHelper, user.CredentialHelper)) return false;

            if (!string.IsNullOrWhiteSpace(profile.SslBackend) &&
                !TextEquals(profile.SslBackend, user.SslBackend)) return false;

            return true;
        }

        private static bool TextEquals(string left, string right) =>
            string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);

        private async Task RefreshCurrentUserAsync()
        {
            var user = await _gitService.GetCurrentUserAsync();

            CurrentUserName = user.Name ?? "(未设置)";
            CurrentUserEmail = user.Email ?? "(未设置)";
            CurrentSigningKey = user.SigningKey ?? "(未设置)";
            CurrentSigningFormat = user.SigningFormat ?? "openpgp";
            CurrentCommitSign = user.CommitSign;

            // 「当前全局配置」展示的键值列表
            CurrentConfigRows.Clear();
            AddConfigRow("user.name", "提交者姓名", user.Name);
            AddConfigRow("user.email", "提交者邮箱", user.Email);
            AddConfigRow("user.signingkey", "签名密钥", user.SigningKey);
            AddConfigRow("gpg.format", "签名格式", user.SigningFormat);
            AddConfigRow("commit.gpgsign", "提交时自动签名", BoolText(user.CommitSign));
            AddConfigRow("tag.gpgsign", "打标签时自动签名", BoolText(user.TagSign));
            AddConfigRow("credential.helper", "凭据助手", user.CredentialHelper);
            AddConfigRow("http.sslBackend", "HTTPS 后端", user.SslBackend);
        }

        /// <summary>
        /// 追加一行全局配置展示，值为空时不显示该行
        /// </summary>
        private void AddConfigRow(string configKey, string displayName, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            CurrentConfigRows.Add(new Models.ConfigDisplayRow
            {
                ConfigKey = configKey,
                Label = displayName,
                Value = value
            });
        }

        private static string BoolText(bool b) => b ? "True" : "False";

        private async Task LoadConfigListAsync()
        {
            var items = await _gitService.ListConfigAsync("global");
            ConfigItems.Clear();
            foreach (var item in items.OrderBy(x => x.FullKey))
                ConfigItems.Add(item);
        }

        /// <summary>重新读取 SSH 目录条目列表</summary>
        private void LoadSshEntries()
        {
            SshEntries.Clear();
            foreach (var entry in _sshService.ListEntries())
                SshEntries.Add(entry);

            SelectedSshEntry = null;
            OnPropertyChanged(nameof(IsSshListEmpty));
            OnPropertyChanged(nameof(SshStatusText));
        }

        /// <summary>
        /// 打开创建密钥对话框：密钥名默认取一个未被占用的 id_ed25519 变体
        /// </summary>
        private void StartCreateSshKey()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in SshEntries) used.Add(entry.Name);

            var name = "id_ed25519";
            for (int i = 2; used.Contains(name); i++)
                name = "id_ed25519_" + i;

            KeyRequest = new SshKeyRequest
            {
                KeyName = name,
                KeyType = "ed25519",
                Comment = string.Empty,
                Passphrase = string.Empty
            };

            OnPropertyChanged(nameof(KeyRequest));
            RequestCreateSshKey?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>请求界面弹出创建密钥对话框</summary>
        public event EventHandler RequestCreateSshKey;

        /// <summary>
        /// 读取密钥对内容，供独立窗口显示；读不到时返回 null
        /// </summary>
        public SshKeyPairContent LoadKeyPairContent(SshListEntry entry)
        {
            return entry == null ? null : _sshService.LoadKeyPairContent(entry.Name);
        }

        /// <summary>
        /// 新建空的白名单文件，成功后占位条目会被真实条目替换
        /// </summary>
        private void CreateWhitelistFile()
        {
            AppendCommandLog("创建 SSH 签名白名单文件");

            var result = _sshService.CreateWhitelistFile();
            if (result.Ok)
            {
                AppendSuccessLog("-> " + result.Message);
                LoadSshEntries();
            }
            else
            {
                AppendErrorLog("-> " + result.Message);
                MessageBox.Show(result.Message, "创建失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            StatusMessage = result.Message;
        }

        /// <summary>
        /// 请求界面弹出白名单对账窗口
        /// </summary>
        public event Action<WhitelistReconcileResult> RequestReconcileWhitelist;

        /// <summary>
        /// 比对密钥对与白名单，把结果交给界面弹窗确认
        /// </summary>
        private void StartReconcileWhitelist()
        {
            // 本机自己的邮箱：当前 git 邮箱 + 所有档案里的邮箱，用于判断白名单条目归属
            var ownEmails = new List<string> { CurrentUserEmail };
            foreach (var profile in Profiles) ownEmails.Add(profile.Email);

            AppendCommandLog("识别密钥项目：比对密钥对与白名单");

            var items = _sshService.AnalyzeWhitelist(ownEmails);
            RequestReconcileWhitelist?.Invoke(new WhitelistReconcileResult { Items = items });
        }

        /// <summary>
        /// 应用对账窗口里勾选的修改
        /// </summary>
        public void ApplyWhitelistChanges(IReadOnlyList<WhitelistReconcileItem> items)
        {
            var result = _sshService.ApplyWhitelistChanges(items);
            if (result.Ok)
            {
                AppendSuccessLog("-> " + result.Message);
                LoadSshEntries();
            }
            else
            {
                AppendErrorLog("-> " + result.Message);
                MessageBox.Show(result.Message, "操作失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            StatusMessage = result.Message;
        }

        /// <summary>
        /// 读取白名单内容，供独立窗口显示；读不到时返回 null
        /// </summary>
        public SshWhitelistContent LoadWhitelistContent()
        {
            return _sshService.LoadWhitelistContent();
        }

        /// <summary>
        /// 保存独立窗口里编辑过的白名单内容
        /// </summary>
        public void SaveWhitelistContent(SshWhitelistContent content)
        {
            if (content == null) return;

            AppendCommandLog("保存 SSH 签名白名单");

            var result = _sshService.SaveWhitelistContent(content);
            if (result.Ok)
            {
                AppendSuccessLog("-> " + result.Message);
                LoadSshEntries();
            }
            else
            {
                AppendErrorLog("-> " + result.Message);
                MessageBox.Show(result.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            StatusMessage = result.Message;
        }

        /// <summary>
        /// 保存独立窗口里编辑过的密钥内容
        /// </summary>
        public void SaveKeyPairContent(SshKeyPairContent content)
        {
            if (content == null) return;

            AppendCommandLog("保存 SSH 密钥 " + content.Name);

            var result = _sshService.SaveKeyPairContent(content);
            if (result.Ok)
            {
                AppendSuccessLog("-> " + result.Message);
                LoadSshEntries();
            }
            else
            {
                AppendErrorLog("-> " + result.Message);
                MessageBox.Show(result.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            StatusMessage = result.Message;
        }

        /// <summary>
        /// 调用 ssh-keygen 生成密钥对
        /// </summary>
        private async Task CreateSshKeyAsync()
        {
            var request = KeyRequest;
            if (request == null) return;

            IsBusy = true;
            StatusMessage = "正在生成密钥对...";
            try
            {
                AppendCommandLog("生成 SSH 密钥对");
                AppendCommandLine("ssh-keygen -t " + request.KeyType + " -f " + request.KeyName);

                var result = await Task.Run(() => _sshService.CreateKeyPair(request));
                if (result.Success)
                {
                    AppendSuccessLog("-> " + result.Message);
                    LoadSshEntries();
                }
                else
                {
                    AppendErrorLog("-> " + result.Message);
                    MessageBox.Show(result.Message, "创建失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                StatusMessage = result.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 请求界面打开某个条目的独立窗口（密钥窗口或白名单窗口）；readOnly 为 true 时只读预览
        /// </summary>
        public event Action<SshListEntry, bool> RequestOpenSshEntry;

        private void OpenSshEntry(SshListEntry entry, bool readOnly)
        {
            if (entry == null || !entry.IsRealEntry) return;
            RequestOpenSshEntry?.Invoke(entry, readOnly);
        }

        /// <summary>
        /// 删除条目：密钥对删私钥与公钥，白名单删白名单文件；文件移到回收站
        /// </summary>
        private async Task DeleteSshEntryAsync(SshListEntry entry)
        {
            if (entry == null || !entry.IsRealEntry) return;

            var files = entry.IsWhitelist
                ? _sshService.GetWhitelistFiles()
                : _sshService.GetKeyPairFiles(entry.Name);

            if (files.Count == 0)
            {
                MessageBox.Show("没有找到「" + entry.Name + "」对应的文件。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var what = entry.IsWhitelist ? "白名单" : "密钥「" + entry.Name + "」";
            var text = "确定删除" + what + "吗？\n\n" +
                       "以下文件将移到回收站（可以恢复）：\n" +
                       string.Join("\n", files);

            if (entry.IsWhitelist)
                text += "\n\n注意：删除后，本机校验提交签名会找不到信任的标识。";
            else if (entry.HasPrivateKey)
                text += "\n\n注意：私钥删除后，用该密钥签名的提交将无法再验证。";

            if (MessageBox.Show(text, "确认删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                != MessageBoxResult.OK)
                return;

            IsBusy = true;
            StatusMessage = "正在删除...";
            try
            {
                AppendCommandLog("删除 SSH " + (entry.IsWhitelist ? "签名白名单" : "密钥对 " + entry.Name));

                var ok = await Task.Run(() => RecycleBin.MoveToRecycleBin(files));
                if (ok)
                {
                    var message = "已删除" + what + "，文件在回收站";
                    AppendSuccessLog("-> " + message);
                    LoadSshEntries();
                    StatusMessage = message;
                }
                else
                {
                    var message = "移到回收站失败，文件未被删除";
                    AppendErrorLog("-> " + message);
                    MessageBox.Show(message + "。", "删除失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusMessage = message;
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 在资源管理器中打开 SSH 目录；目录不存在时给出提示
        /// </summary>
        private void OpenSshFolder()
        {
            var dir = _sshService.SshFolderPath;
            if (!_sshService.FolderExists)
            {
                MessageBox.Show("SSH 目录不存在：\n" + dir, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + dir + "\"",
                    UseShellExecute = true
                });
                StatusMessage = "已打开 SSH 目录: " + dir;
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开目录: " + ex.Message + "\n\n路径: " + dir,
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 把路径复制到剪贴板
        /// </summary>
        private void CopyPathToClipboard(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                Clipboard.SetText(path);
                StatusMessage = "已复制路径: " + path;
            }
            catch (Exception ex)
            {
                MessageBox.Show("复制失败：" + ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 刷新 gh 账号列表，不写命令记录
        /// </summary>
        private async Task RefreshGithubAsync()
        {
            GhAuthStatus = await _ghService.GetAuthStatusRawAsync();
            await RefreshGithubAccountsAsync();
        }

        /// <summary>
        /// 只重新读取账号列表
        /// </summary>
        private async Task RefreshGithubAccountsAsync()
        {
            GithubAccounts.Clear();
            var accounts = await _ghService.GetLoggedInAccountsAsync();
            foreach (var a in accounts)
                GithubAccounts.Add(a);
        }

        /// <summary>
        /// 把指定档案应用到全局 .gitconfig；profile 为空时使用当前选中项
        /// </summary>
        private async Task ApplyProfileAsync(GitProfile profile)
        {
            var target = profile ?? SelectedProfile;
            if (target == null) return;

            // 同步选中该行
            SelectedProfile = target;

            IsBusy = true;
            StatusMessage = $"正在应用档案「{target.UserName}」...";
            try
            {
                var result = await _gitService.ApplyProfileAsync(target);
                if (result.Success)
                {
                    target.LastUsedAt = DateTime.Now;
                    target.IsActive = true;
                    foreach (var p in Profiles)
                        if (p.Id != target.Id) p.IsActive = false;
                    await _storage.UpsertProfileAsync(target);
                    await RefreshCurrentUserAsync();
                    await LoadConfigListAsync();
                    StatusMessage = result.Message;
                    MessageBox.Show(result.Message, "切换成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusMessage = "应用失败: " + result.Message;
                    MessageBox.Show(result.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void StartAddProfile()
        {
            // 新建档案：所有字段留空，不预填当前 Git 配置
            EditingProfile = new GitProfile
            {
                UserName = "",
                Email = "",
                SigningKey = "",
                SigningFormat = "openpgp",
                CommitGpgsign = false,
                TagGpgsign = false,
                SshKeyPath = "",
                GithubUsername = "",
                CredentialHelper = "",
                SslBackend = ""
            };
            OnPropertyChanged(nameof(EditingProfile));
            RequestEditProfile?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 编辑指定档案；profile 为空时使用当前选中项
        /// </summary>
        private void StartEditProfile(GitProfile profile)
        {
            var target = profile ?? SelectedProfile;
            if (target == null) return;

            SelectedProfile = target;
            EditingProfile = new GitProfile
            {
                Id = target.Id,
                UserName = target.UserName,
                Email = target.Email,
                SigningKey = target.SigningKey,
                SigningFormat = target.SigningFormat,
                CommitGpgsign = target.CommitGpgsign,
                TagGpgsign = target.TagGpgsign,
                SshKeyPath = target.SshKeyPath,
                GithubUsername = target.GithubUsername,
                CredentialHelper = target.CredentialHelper,
                SslBackend = target.SslBackend,
                CreatedAt = target.CreatedAt,
                LastUsedAt = target.LastUsedAt
            };
            OnPropertyChanged(nameof(EditingProfile));
            RequestEditProfile?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler RequestEditProfile;

        private async Task SaveEditingProfileAsync()
        {
            if (string.IsNullOrWhiteSpace(EditingProfile.UserName) ||
                string.IsNullOrWhiteSpace(EditingProfile.Email))
            {
                MessageBox.Show("Git user.name 和 user.email 不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await _storage.UpsertProfileAsync(EditingProfile);
            await LoadProfilesAsync();
            await LoadProfilesJsonAsync();
            StatusMessage = $"档案「{EditingProfile.UserName}」已保存";
            RequestCloseEditDialog?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler RequestCloseEditDialog;

        /// <summary>
        /// 删除指定档案；profile 为空时使用当前选中项
        /// </summary>
        private async Task DeleteProfileAsync(GitProfile profile)
        {
            var target = profile ?? SelectedProfile;
            if (target == null) return;

            var result = MessageBox.Show($"确定删除档案「{target.UserName}」吗？", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            await _storage.DeleteProfileAsync(target.Id);
            await LoadProfilesAsync();
            await LoadProfilesJsonAsync();
            SelectedProfile = null;
            StatusMessage = $"档案「{target.UserName}」已删除";
        }

        private async Task CaptureCurrentAsProfileAsync()
        {
            var user = await _gitService.GetCurrentUserAsync();
            if (string.IsNullOrWhiteSpace(user.Name) || string.IsNullOrWhiteSpace(user.Email))
            {
                MessageBox.Show("当前 Git 用户名或邮箱未设置，无法捕获", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var profile = new GitProfile
            {
                UserName = user.Name,
                Email = user.Email,
                SigningKey = user.SigningKey ?? "",
                SigningFormat = user.SigningFormat ?? "openpgp",
                CommitGpgsign = user.CommitSign,
                TagGpgsign = user.TagSign,
                CredentialHelper = user.CredentialHelper ?? "",
                SslBackend = user.SslBackend ?? ""
            };

            var exist = Profiles.FirstOrDefault(p =>
                string.Equals(p.Email, user.Email, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.UserName, user.Name, StringComparison.OrdinalIgnoreCase));
            if (exist != null)
            {
                var r = MessageBox.Show($"已存在相同用户的档案「{exist.UserName}」，是否覆盖？", "确认",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
                profile.Id = exist.Id;
                profile.CreatedAt = exist.CreatedAt;
            }

            await _storage.UpsertProfileAsync(profile);
            await LoadProfilesAsync();
            await LoadProfilesJsonAsync();
            StatusMessage = $"已将当前配置保存为档案「{profile.UserName}」";
        }

        /// <summary>
        /// 切换 gh 账号；account 为空时使用当前选中项
        /// </summary>
        private async Task SwitchGithubAccountAsync(GithubAccount account)
        {
            var target = account ?? SelectedGithubAccount;
            if (target == null)
            {
                MessageBox.Show("请先在列表中选择要切换的账号。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (target.IsActive)
            {
                MessageBox.Show($"「{target.Username}」已经是当前生效账号，无需切换。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsBusy = true;
            StatusMessage = $"正在切换 GitHub 账号到 {target.Username}...";
            try
            {
                var result = await _ghService.SwitchAccountAsync(target.Username, target.Host);
                if (result.Success)
                {
                    await RefreshGithubAsync();
                    StatusMessage = $"已切换到 GitHub 账号 {target.Username}";
                }
                else
                {
                    StatusMessage = "切换失败: " + result.Message;
                    MessageBox.Show(result.Message + "\n\n提示：只能切换到已登录过的账号。若该账号尚未登录，请先点「登录新账号」。",
                        "切换失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "切换异常: " + ex.Message;
                MessageBox.Show(ex.Message, "切换异常", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 登录新的 GitHub 账号（浏览器设备码授权），不阻塞界面
        /// </summary>
        private async Task LoginGithubAsync()
        {
            if (_isGithubLoggingIn)
            {
                MessageBox.Show("已有登录流程正在进行，请先在浏览器中完成授权。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 清掉上一次残留的登录进程
            KillCurrentLoginProcess();

            var before = GithubAccounts.Count;
            var start = _ghService.StartLoginWithClipboard();
            if (!start.Success)
            {
                MessageBox.Show("无法启动登录流程：" + start.Message, "登录失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _loginProcess = start.Process;
            _isGithubLoggingIn = true;
            _loginCancelled = false;
            DeviceCode = string.Empty;
            GithubUrl = string.Empty;
            IsDeviceAuthVisible = true;
            AppendCommandLine("gh auth login --hostname github.com --git-protocol https --web --clipboard");
            StatusMessage = "正在向 GitHub 申请设备码...";

            var urlOpened = false;

            try
            {
                // 每 1.5 秒查一次，最多等 5 分钟；发现账号数增加即视为登录成功
                for (int i = 0; i < 200; i++)
                {
                    await Task.Delay(1500);

                    // 用户点了「取消登录」就直接结束
                    if (_loginCancelled) return;

                    // 1) 实时取设备码（gh 打印到标准错误输出）
                    if (string.IsNullOrEmpty(DeviceCode))
                    {
                        var code = _ghService.ExtractDeviceCode();
                        if (!string.IsNullOrEmpty(code))
                        {
                            DeviceCode = code;
                            AppendSuccessLog("设备码: " + code);
                        }
                    }

                    // 2) 取授权地址并打开浏览器
                    if (!urlOpened)
                    {
                        var url = _ghService.ExtractVerificationUrl() ?? DefaultGithubDeviceUrl;
                        GithubUrl = url;
                        try
                        {
                            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                            urlOpened = true;
                            AppendCommandLog("已打开浏览器: " + url);
                            AppendCommandLog("请把设备码粘贴到页面并完成授权；设备码也已复制到剪贴板（Ctrl+V 即可）。");
                            StatusMessage = "等待浏览器授权中...";
                        }
                        catch (Exception ex)
                        {
                            urlOpened = true;   // 不再重复尝试
                            AppendErrorLog("打开浏览器失败: " + ex.Message + "，请手动访问 " + url);
                        }
                    }

                    // 3) 检测登录结果
                    var accounts = await _ghService.GetLoggedInAccountsAsync();
                    if (accounts.Count > before)
                    {
                        await RefreshGithubFromLogAsync();
                        AppendSuccessLog("检测到新账号，账号列表已刷新。");
                        StatusMessage = "GitHub 账号登录成功，列表已刷新";
                        return;
                    }

                    // 4) gh 进程已退出，说明本次登录结束
                    if (start.Process != null && start.Process.HasExited)
                    {
                        await RefreshGithubFromLogAsync();
                        if (GithubAccounts.Count > before)
                        {
                            AppendSuccessLog("检测到新账号，账号列表已刷新。");
                            StatusMessage = "GitHub 账号登录成功，列表已刷新";
                        }
                        else
                        {
                            var detail = _ghService.GetLoginOutput();
                            AppendErrorLog("登录流程已结束，但未检测到新账号。");
                            if (!string.IsNullOrEmpty(detail)) AppendResultLog(detail);
                            StatusMessage = "登录流程已结束，未检测到新账号";
                        }
                        return;
                    }
                }

                AppendErrorLog("等待授权超时（5 分钟），可稍后点「刷新账号状态」查看。");
                StatusMessage = "登录等待超时，请点「刷新账号状态」确认";
                GhAuthStatus = await _ghService.GetAuthStatusRawAsync();
                await RefreshGithubAccountsAsync();
            }
            catch (Exception ex)
            {
                AppendErrorLog("登录流程异常: " + ex.Message);
                StatusMessage = "登录异常: " + ex.Message;
            }
            finally
            {
                KillCurrentLoginProcess();
                _isGithubLoggingIn = false;
                IsDeviceAuthVisible = false;
            }
        }

        /// <summary>
        /// 取消登录：终止等待中的 gh 登录进程，回到可重新登录的状态
        /// </summary>
        private void CancelGithubLogin()
        {
            if (!_isGithubLoggingIn)
            {
                MessageBox.Show("当前没有进行中的登录流程。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _loginCancelled = true;
            KillCurrentLoginProcess();
            _isGithubLoggingIn = false;
            IsDeviceAuthVisible = false;
            DeviceCode = string.Empty;
            GithubUrl = string.Empty;
            AppendErrorLog("已取消登录授权。");
            StatusMessage = "已取消登录授权，可重新点「登录新账号」";
        }

        /// <summary>
        /// 结束当前登录进程
        /// </summary>
        private void KillCurrentLoginProcess()
        {
            var proc = _loginProcess;
            _loginProcess = null;
            if (proc == null) return;
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill();
                    proc.WaitForExit(2000);
                }
            }
            catch { }
            finally
            {
                try { proc.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// 重新打开 GitHub 设备授权页面
        /// </summary>
        private void OpenGithubDevicePage()
        {
            var url = string.IsNullOrEmpty(GithubUrl) ? DefaultGithubDeviceUrl : GithubUrl;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                StatusMessage = "已打开授权页面: " + url;
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开浏览器：" + ex.Message + "\n请手动访问：\n" + url,
                    "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 把设备码复制到剪贴板
        /// </summary>
        private void CopyDeviceCode()
        {
            if (string.IsNullOrEmpty(DeviceCode))
            {
                MessageBox.Show("还没有获取到设备码，请稍候或重新点击「登录新账号」。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                Clipboard.SetText(DeviceCode);
                StatusMessage = "设备码 " + DeviceCode + " 已复制到剪贴板";
            }
            catch (Exception ex)
            {
                MessageBox.Show("复制失败：" + ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 刷新 gh 账号列表，并把 gh 的原始输出追加到命令记录
        /// </summary>
        private async Task RefreshGithubFromLogAsync(string reason = null)
        {
            AppendCommandLine("gh auth status" + (string.IsNullOrEmpty(reason) ? "" : "   （" + reason + "）"));
            await RefreshGithubAsync();
            if (GithubAccounts.Count == 0)
                AppendResultLog("(当前没有任何已登录的 GitHub 账号)");
            else
                AppendResultLog(GhAuthStatus.Length > 0 ? GhAuthStatus : "(无输出)");
        }

        /// <summary>
        /// 清理 Windows 凭据管理器里 gh 已不再登录的残留凭据。
        /// 静默执行，不弹窗。返回清理条数。
        /// </summary>
        private int CleanupOrphanedCredentials()
        {
            try
            {
                var loggedIn = new List<string>();
                foreach (var a in GithubAccounts)
                {
                    if (!string.IsNullOrEmpty(a.Username)) loggedIn.Add(a.Username);
                }

                var removed = CredentialCleaner.CleanupOrphaned(loggedIn);
                if (removed.Count == 0) return 0;

                foreach (var target in removed)
                    AppendSuccessLog("已清理残留凭据: " + target);

                return removed.Count;
            }
            catch (Exception ex)
            {
                AppendErrorLog("清理残留凭据失败: " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// 追加一条普通信息
        /// </summary>
        private void AppendCommandLog(string text)
        {
            AddLogEntry(LogKind.Info, text);
        }

        /// <summary>
        /// 追加一条命令执行记录（显示为「执行: xxx」）
        /// </summary>
        private void AppendCommandLine(string command)
        {
            AddLogEntry(LogKind.Command, command);
        }

        /// <summary>
        /// 追加一条命令原始输出（弱化显示）
        /// </summary>
        private void AppendResultLog(string output)
        {
            AddLogEntry(LogKind.Result, output);
        }

        /// <summary>
        /// 追加一条成功结果
        /// </summary>
        private void AppendSuccessLog(string text)
        {
            AddLogEntry(LogKind.Success, text);
        }

        /// <summary>
        /// 追加一条错误信息
        /// </summary>
        private void AppendErrorLog(string text)
        {
            AddLogEntry(LogKind.Error, text);
        }

        /// <summary>
        /// 追加日志条目；多行文本按行拆分，每行各自着色
        /// </summary>
        private void AddLogEntry(LogKind kind, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var now = DateTime.Now;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                LogEntries.Add(new LogEntry { Time = now, Kind = kind, Text = line });
            }
            RebuildCommandLog();
        }

        /// <summary>
        /// 日志的纯文本形式（供界面渲染）。
        /// 每行格式：[HH:mm:ss] 正文；颜色由界面按类型另行着色。
        /// </summary>
        public string CommandLog
        {
            get => _commandLog;
            private set { _commandLog = value; OnPropertyChanged(); }
        }

        private string _commandLog = "";

        /// <summary>
        /// 把条目集合拼成纯文本，交给界面渲染
        /// </summary>
        private void RebuildCommandLog()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var e in LogEntries)
            {
                sb.Append('[').Append(e.TimeText).Append("] ").Append(e.Text).Append('\n');
            }
            CommandLog = sb.ToString();
        }

        /// <summary>
        /// 退出指定 GitHub 账号；account 为空时使用当前选中项
        /// </summary>
        private async Task LogoutGithubAccountAsync(GithubAccount account)
        {
            var target = account ?? SelectedGithubAccount;
            if (target == null)
            {
                MessageBox.Show("请先选择要退出的账号。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"确定退出登录 {target.Username} @ {target.Host} 吗？\n\n退出后凭据将被删除，此操作不可撤销。",
                "确认退出登录", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            IsBusy = true;
            StatusMessage = $"正在退出 GitHub 账号 {target.Username}...";
            try
            {
                AppendCommandLine("gh auth logout --hostname " + target.Host + " --user " + target.Username);
                var result = await _ghService.LogoutAsync(target.Username, target.Host);
                if (result.Success)
                {
                    SelectedGithubAccount = null;
                    await RefreshGithubFromLogAsync("退出登录后刷新");
                    // gh auth logout 不会清理 keyring，这里补上
                    var cleaned = CleanupOrphanedCredentials();
                    StatusMessage = cleaned > 0
                        ? $"已退出登录 {target.Username}，并清理 {cleaned} 条残留凭据"
                        : $"已退出登录 {target.Username}";
                }
                else
                {
                    StatusMessage = "退出失败: " + result.Message;
                    MessageBox.Show(result.Message, "退出失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "退出异常: " + ex.Message;
                MessageBox.Show(ex.Message, "退出异常", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }
        /// <summary>
        /// 刷新档案数据文件路径
        /// </summary>
        private Task LoadProfilesJsonAsync()
        {
            GitConfigPath = _storage.StoragePath;
            return Task.CompletedTask;
        }

        private void OpenProfilesJsonFolder()
        {
            var path = _storage.StoragePath;
            var dir = System.IO.Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir))
            {
                MessageBox.Show("无法解析目录路径", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (!System.IO.Directory.Exists(dir))
            {
                try { System.IO.Directory.CreateDirectory(dir); }
                catch (Exception ex)
                {
                    MessageBox.Show("目录不存在且创建失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + dir + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开目录: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 在资源管理器中打开 .gitconfig 所在目录，并选中该文件本身
        /// </summary>
        private void OpenGitConfigLocation()
        {
            var path = _gitService.GetGlobalConfigPath();
            var dir = System.IO.Path.GetDirectoryName(path);

            try
            {
                if (System.IO.File.Exists(path))
                {
                    // explorer 的 /select 参数会打开所在目录并选中该文件
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "/select,\"" + path + "\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    // 文件还不存在（首次使用）：打开所在目录
                    if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir))
                    {
                        MessageBox.Show("目录不存在: " + (dir ?? path), "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "\"" + dir + "\"",
                        UseShellExecute = true
                    });
                }
                StatusMessage = "已在资源管理器中定位: " + path;
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开所在目录: " + ex.Message + "\n\n路径: " + path,
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SaveSelectedConfigItemAsync()
        {
            if (SelectedConfigItem == null) return;
            var result = await _gitService.SetConfigAsync(SelectedConfigItem.FullKey, SelectedConfigItem.Value);
            if (result.Success)
            {
                SelectedConfigItem.IsModified = false;
                StatusMessage = $"已保存 {SelectedConfigItem.FullKey}";
            }
            else
            {
                MessageBox.Show(result.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeleteSelectedConfigItemAsync()
        {
            if (SelectedConfigItem == null) return;
            var r = MessageBox.Show($"确定删除配置项 {SelectedConfigItem.FullKey} 吗？", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            var result = await _gitService.UnsetConfigAsync(SelectedConfigItem.FullKey);
            if (result.Success)
            {
                ConfigItems.Remove(SelectedConfigItem);
                SelectedConfigItem = null;
                StatusMessage = "配置项已删除";
            }
            else
            {
                MessageBox.Show(result.Message, "删除失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
