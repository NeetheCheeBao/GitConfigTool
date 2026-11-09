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


        private string _statusMessage = Helpers.Localization.T("rt.093");
        private bool _isBusy;
        private string _gitVersion = Helpers.Localization.T("rt.094");
        private string _ghVersion = Helpers.Localization.T("rt.094");
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
                if (!_sshService.FolderExists) return Helpers.Localization.T("rt.095");
                if (IsSshListEmpty) return Helpers.Localization.T("rt.096");
                return Helpers.Localization.T("rt.097", SshEntries.Count);
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
            AddEnvironmentLine(lines, Helpers.Localization.T("rt.098"), Helpers.AppInfo.Name + " " + Helpers.AppInfo.Version);
            AddEnvironmentLine(lines, "Git", WithPath(GitVersion, GitPath));
            AddEnvironmentLine(lines, "GitHub CLI", WithPath(GhVersion, GhPath));
            AddEnvironmentLine(lines, Helpers.Localization.T("rt.099"), SshFolderPathDisplay);
            AddEnvironmentLine(lines, Helpers.Localization.T("rt.100"), GitConfigPathDisplay);
            AddEnvironmentLine(lines, Helpers.Localization.T("rt.101"), Environment.OSVersion.VersionString + "　·　" +
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
                ? Helpers.Localization.T("rt.102")
                : Helpers.Localization.T("rt.103");
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

        public string SelectedLanguageId { get; private set; } = Helpers.Localization.DefaultLanguageId;

        public bool SelectLanguage(string id)
        {
            if (string.IsNullOrEmpty(id) || id == SelectedLanguageId) return false;
            SelectedLanguageId = id;
            return true;
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
        public string DeviceCodeText => string.IsNullOrEmpty(_deviceCode) ? Helpers.Localization.T("rt.106") : _deviceCode;

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
            RefreshGithubCommand = new AsyncRelayCommand(async _ => await RefreshGithubFromLogAsync(Helpers.Localization.T("rt.107")));
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

        public async Task InitializeAsync(IProgress<(string Text, int Percent)> progress)
        {
            IsBusy = true;
            StatusMessage = Helpers.Localization.T("rt.108");
            try
            {
                AppendCommandLog(Helpers.Localization.T("rt.109"));

                progress?.Report((Helpers.Localization.T("loading.check_tools"), 5));

                var gitCheck = await _gitService.CheckGitAvailableAsync();
                GitVersion = gitCheck.Ok ? gitCheck.Version : Helpers.Localization.T("rt.110");
                GitPath = _gitService.GitExecutablePath ?? "";
                AppendCommandLine("git --version");
                AppendResultLog("-> " + GitVersion);
                if (!string.IsNullOrEmpty(GitPath))
                    AppendResultLog(Helpers.Localization.T("rt.111") + GitPath);
                else
                    AppendErrorLog("-> " + (_gitService.DiscoveryError ?? Helpers.Localization.T("rt.112")));

                var ghCheck = await _ghService.CheckAvailableAsync();
                GhVersion = ghCheck.Ok ? ghCheck.Version : Helpers.Localization.T("rt.110");
                GhPath = _ghService.GhExecutablePath ?? "";
                AppendCommandLine("gh --version");
                AppendResultLog("-> " + GhVersion);
                if (!string.IsNullOrEmpty(GhPath))
                    AppendResultLog(Helpers.Localization.T("rt.111") + GhPath);
                else
                    AppendErrorLog("-> " + (_ghService.DiscoveryError ?? Helpers.Localization.T("rt.113")));

                progress?.Report((Helpers.Localization.T("loading.read_profiles"), 15));
                await LoadProfilesAsync();

                progress?.Report((Helpers.Localization.T("loading.current_config"), 35));
                await RefreshCurrentUserAsync();

                progress?.Report((Helpers.Localization.T("loading.full_config"), 45));
                await LoadConfigListAsync();

                await RefreshGithubFromLogAsync(Helpers.Localization.T("rt.114"), progress);

                progress?.Report((Helpers.Localization.T("loading.cleanup"), 88));
                CleanupOrphanedCredentials();

                // 数据文件从 profiles.json 迁移到 GitConfig.json 时给出提示
                if (!string.IsNullOrEmpty(_storage.MigrationNote))
                    AppendCommandLog(_storage.MigrationNote);

                progress?.Report((Helpers.Localization.T("loading.ssh"), 92));
                await LoadProfilesJsonAsync();
                LoadSshEntries();
                LoadEnvironmentText();

                progress?.Report((Helpers.Localization.T("loading.watcher"), 97));
                StartWatching();

                StatusMessage = Helpers.Localization.T("rt.115");
                progress?.Report((Helpers.Localization.T("loading.done"), 100));
            }
            catch (Exception ex)
            {
                AppendErrorLog(Helpers.Localization.T("rt.116") + ex.Message);
                StatusMessage = Helpers.Localization.T("rt.116") + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RefreshAllAsync()
        {
            // 语言在刷新流程开头加载；旧语言的命令记录是历史文本，无法追溯翻译，直接清空
            if (SelectedLanguageId != Helpers.Localization.AppliedLanguageId)
            {
                Helpers.Localization.Apply(SelectedLanguageId);
                // 版本号等表达式属性不会自己发通知，让所有绑定重新取值
                OnPropertyChanged(string.Empty);
                LogEntries.Clear();
                RebuildCommandLog();
            }

            IsBusy = true;
            StatusMessage = Helpers.Localization.T("rt.117");
            try
            {
                AppendCommandLog(Helpers.Localization.T("rt.118"));
                await LoadProfilesAsync();
                await RefreshCurrentUserAsync();
                await LoadConfigListAsync();
                await RefreshGithubFromLogAsync(Helpers.Localization.T("rt.119"));
                await LoadProfilesJsonAsync();
                LoadSshEntries();
                LoadEnvironmentText();
                AppendCommandLog(Helpers.Localization.T("rt.120"));
                StatusMessage = Helpers.Localization.T("rt.121") + DateTime.Now.ToString("HH:mm:ss");
            }
            catch (Exception ex)
            {
                AppendErrorLog(Helpers.Localization.T("rt.122") + ex.Message);
                StatusMessage = Helpers.Localization.T("rt.122") + ex.Message;
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
                StatusMessage = Helpers.Localization.T("rt.123") + DateTime.Now.ToString("HH:mm:ss");
            }
            catch (Exception ex)
            {
                AppendErrorLog(Helpers.Localization.T("rt.124") + ex.Message);
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
                StatusMessage = Helpers.Localization.T("rt.125") + DateTime.Now.ToString("HH:mm:ss");
            }
            catch (Exception ex)
            {
                AppendErrorLog(Helpers.Localization.T("rt.124") + ex.Message);
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
            StatusMessage = Helpers.Localization.T("rt.126") + DateTime.Now.ToString("HH:mm:ss");
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

            CurrentUserName = user.Name ?? Helpers.Localization.T("rt.127");
            CurrentUserEmail = user.Email ?? Helpers.Localization.T("rt.127");
            CurrentSigningKey = user.SigningKey ?? Helpers.Localization.T("rt.127");
            CurrentSigningFormat = user.SigningFormat ?? "openpgp";
            CurrentCommitSign = user.CommitSign;

            // 「当前全局配置」展示的键值列表
            CurrentConfigRows.Clear();
            AddConfigRow("user.name", Helpers.Localization.T("rt.128"), user.Name);
            AddConfigRow("user.email", Helpers.Localization.T("rt.129"), user.Email);
            AddConfigRow("user.signingkey", Helpers.Localization.T("rt.130"), user.SigningKey);
            AddConfigRow("gpg.format", Helpers.Localization.T("rt.131"), user.SigningFormat);
            AddConfigRow("commit.gpgsign", Helpers.Localization.T("rt.132"), BoolText(user.CommitSign));
            AddConfigRow("tag.gpgsign", Helpers.Localization.T("rt.133"), BoolText(user.TagSign));
            AddConfigRow("credential.helper", Helpers.Localization.T("rt.134"), user.CredentialHelper);
            AddConfigRow("http.sslBackend", Helpers.Localization.T("rt.135"), user.SslBackend);
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
            AppendCommandLog(Helpers.Localization.T("rt.136"));

            var result = _sshService.CreateWhitelistFile();
            if (result.Ok)
            {
                AppendSuccessLog("-> " + result.Message);
                LoadSshEntries();
            }
            else
            {
                AppendErrorLog("-> " + result.Message);
                MessageBox.Show(result.Message, Helpers.Localization.T("msg.create_failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
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

            AppendCommandLog(Helpers.Localization.T("rt.137"));

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
                MessageBox.Show(result.Message, Helpers.Localization.T("msg.operation_failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
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

            AppendCommandLog(Helpers.Localization.T("rt.138"));

            var result = _sshService.SaveWhitelistContent(content);
            if (result.Ok)
            {
                AppendSuccessLog("-> " + result.Message);
                LoadSshEntries();
            }
            else
            {
                AppendErrorLog("-> " + result.Message);
                MessageBox.Show(result.Message, Helpers.Localization.T("msg.save_failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            StatusMessage = result.Message;
        }

        /// <summary>
        /// 保存独立窗口里编辑过的密钥内容
        /// </summary>
        public void SaveKeyPairContent(SshKeyPairContent content)
        {
            if (content == null) return;

            AppendCommandLog(Helpers.Localization.T("rt.139") + content.Name);

            var result = _sshService.SaveKeyPairContent(content);
            if (result.Ok)
            {
                AppendSuccessLog("-> " + result.Message);
                LoadSshEntries();
            }
            else
            {
                AppendErrorLog("-> " + result.Message);
                MessageBox.Show(result.Message, Helpers.Localization.T("msg.save_failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            StatusMessage = Helpers.Localization.T("rt.140");
            try
            {
                AppendCommandLog(Helpers.Localization.T("rt.141"));
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
                    MessageBox.Show(result.Message, Helpers.Localization.T("msg.create_failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show(Helpers.Localization.T("msg.no_file_prefix") + entry.Name + Helpers.Localization.T("msg.no_file_suffix"),
                    Helpers.Localization.T("msg.title_hint"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var what = entry.IsWhitelist ? Helpers.Localization.T("rt.028") : Helpers.Localization.T("rt.142") + entry.Name + "」";
            var text = Helpers.Localization.T("msg.confirm_delete_files", what, string.Join("\n", files));

            if (entry.IsWhitelist)
                text += Helpers.Localization.T("msg.delete_whitelist_note");
            else if (entry.HasPrivateKey)
                text += Helpers.Localization.T("msg.delete_key_note");

            if (MessageBox.Show(text, Helpers.Localization.T("msg.title_confirm_delete"), MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                != MessageBoxResult.OK)
                return;

            IsBusy = true;
            StatusMessage = Helpers.Localization.T("rt.143");
            try
            {
                AppendCommandLog(Helpers.Localization.T("rt.144") + (entry.IsWhitelist ? Helpers.Localization.T("rt.032") : Helpers.Localization.T("rt.145") + entry.Name));

                var ok = await Task.Run(() => RecycleBin.MoveToRecycleBin(files));
                if (ok)
                {
                    var message = Helpers.Localization.T("rt.047") + what + Helpers.Localization.T("rt.146");
                    AppendSuccessLog("-> " + message);
                    LoadSshEntries();
                    StatusMessage = message;
                }
                else
                {
                    var message = Helpers.Localization.T("rt.147");
                    AppendErrorLog("-> " + message);
                    MessageBox.Show(message + "。", Helpers.Localization.T("msg.delete_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(Helpers.Localization.T("msg.ssh_dir_missing") + dir, Helpers.Localization.T("msg.title_hint"), MessageBoxButton.OK, MessageBoxImage.Information);
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
                StatusMessage = Helpers.Localization.T("rt.148") + dir;
            }
            catch (Exception ex)
            {
                MessageBox.Show(Helpers.Localization.T("msg.open_dir_failed") + ex.Message + Helpers.Localization.T("msg.path_suffix") + dir,
                    Helpers.Localization.T("msg.title_error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                StatusMessage = Helpers.Localization.T("rt.149") + path;
            }
            catch (Exception ex)
            {
                MessageBox.Show(Helpers.Localization.T("msg.copy_failed") + ex.Message, Helpers.Localization.T("msg.title_hint"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 刷新 gh 账号列表，不写命令记录
        /// </summary>
        private async Task RefreshGithubAsync(IProgress<(string Text, int Percent)> progress = null)
        {
            progress?.Report((Helpers.Localization.T("loading.gh_status"), 55));
            GhAuthStatus = await _ghService.GetAuthStatusRawAsync();
            progress?.Report((Helpers.Localization.T("loading.gh_accounts"), 74));
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
            StatusMessage = Helpers.Localization.T("rt.150", target.UserName);
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
                    MessageBox.Show(result.Message, Helpers.Localization.T("msg.switch_ok"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusMessage = Helpers.Localization.T("rt.151") + result.Message;
                    MessageBox.Show(result.Message, Helpers.Localization.T("msg.title_error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(Helpers.Localization.T("msg.name_email_required"), Helpers.Localization.T("msg.title_hint"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await _storage.UpsertProfileAsync(EditingProfile);
            await LoadProfilesAsync();
            await LoadProfilesJsonAsync();
            StatusMessage = Helpers.Localization.T("rt.152", EditingProfile.UserName);
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

            var result = MessageBox.Show(Helpers.Localization.T("msg.confirm_delete_profile", target.UserName),
                Helpers.Localization.T("msg.title_confirm_delete"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            await _storage.DeleteProfileAsync(target.Id);
            await LoadProfilesAsync();
            await LoadProfilesJsonAsync();
            SelectedProfile = null;
            StatusMessage = Helpers.Localization.T("rt.153", target.UserName);
        }

        private async Task CaptureCurrentAsProfileAsync()
        {
            var user = await _gitService.GetCurrentUserAsync();
            if (string.IsNullOrWhiteSpace(user.Name) || string.IsNullOrWhiteSpace(user.Email))
            {
                MessageBox.Show(Helpers.Localization.T("msg.no_current_user"), Helpers.Localization.T("msg.title_hint"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
                var r = MessageBox.Show(Helpers.Localization.T("msg.confirm_overwrite_profile", exist.UserName),
                    Helpers.Localization.T("msg.title_confirm"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
                profile.Id = exist.Id;
                profile.CreatedAt = exist.CreatedAt;
            }

            await _storage.UpsertProfileAsync(profile);
            await LoadProfilesAsync();
            await LoadProfilesJsonAsync();
            StatusMessage = Helpers.Localization.T("rt.154", profile.UserName);
        }

        /// <summary>
        /// 切换 gh 账号；account 为空时使用当前选中项
        /// </summary>
        private async Task SwitchGithubAccountAsync(GithubAccount account)
        {
            var target = account ?? SelectedGithubAccount;
            if (target == null)
            {
                MessageBox.Show(Helpers.Localization.T("msg.select_account_first"), Helpers.Localization.T("msg.title_hint"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (target.IsActive)
            {
                MessageBox.Show(Helpers.Localization.T("msg.already_active", target.Username),
                    Helpers.Localization.T("msg.title_hint"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsBusy = true;
            StatusMessage = Helpers.Localization.T("rt.155", target.Username);
            try
            {
                var result = await _ghService.SwitchAccountAsync(target.Username, target.Host);
                if (result.Success)
                {
                    await RefreshGithubAsync();
                    StatusMessage = Helpers.Localization.T("rt.156", target.Username);
                }
                else
                {
                    StatusMessage = Helpers.Localization.T("rt.157") + result.Message;
                    MessageBox.Show(result.Message + Helpers.Localization.T("msg.switch_hint"),
                        Helpers.Localization.T("msg.switch_failed"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = Helpers.Localization.T("rt.158") + ex.Message;
                MessageBox.Show(ex.Message, Helpers.Localization.T("msg.switch_exception"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(Helpers.Localization.T("msg.login_in_progress"), Helpers.Localization.T("msg.title_hint"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 清掉上一次残留的登录进程
            KillCurrentLoginProcess();

            var before = GithubAccounts.Count;
            var start = _ghService.StartLoginWithClipboard();
            if (!start.Success)
            {
                MessageBox.Show(Helpers.Localization.T("msg.login_start_failed") + start.Message, Helpers.Localization.T("msg.login_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _loginProcess = start.Process;
            _isGithubLoggingIn = true;
            _loginCancelled = false;
            DeviceCode = string.Empty;
            GithubUrl = string.Empty;
            IsDeviceAuthVisible = true;
            AppendCommandLine("gh auth login --hostname github.com --git-protocol https --web --clipboard");
            StatusMessage = Helpers.Localization.T("rt.159");

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
                            AppendSuccessLog(Helpers.Localization.T("rt.160") + code);
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
                            AppendCommandLog(Helpers.Localization.T("rt.161") + url);
                            AppendCommandLog(Helpers.Localization.T("rt.162"));
                            StatusMessage = Helpers.Localization.T("rt.163");
                        }
                        catch (Exception ex)
                        {
                            urlOpened = true;   // 不再重复尝试
                            AppendErrorLog(Helpers.Localization.T("rt.164") + ex.Message + Helpers.Localization.T("rt.165") + url);
                        }
                    }

                    // 3) 检测登录结果
                    var accounts = await _ghService.GetLoggedInAccountsAsync();
                    if (accounts.Count > before)
                    {
                        await RefreshGithubFromLogAsync();
                        AppendSuccessLog(Helpers.Localization.T("rt.166"));
                        StatusMessage = Helpers.Localization.T("rt.167");
                        return;
                    }

                    // 4) gh 进程已退出，说明本次登录结束
                    if (start.Process != null && start.Process.HasExited)
                    {
                        await RefreshGithubFromLogAsync();
                        if (GithubAccounts.Count > before)
                        {
                            AppendSuccessLog(Helpers.Localization.T("rt.166"));
                            StatusMessage = Helpers.Localization.T("rt.167");
                        }
                        else
                        {
                            var detail = _ghService.GetLoginOutput();
                            AppendErrorLog(Helpers.Localization.T("rt.168"));
                            if (!string.IsNullOrEmpty(detail)) AppendResultLog(detail);
                            StatusMessage = Helpers.Localization.T("rt.169");
                        }
                        return;
                    }
                }

                AppendErrorLog(Helpers.Localization.T("rt.170"));
                StatusMessage = Helpers.Localization.T("rt.171");
                GhAuthStatus = await _ghService.GetAuthStatusRawAsync();
                await RefreshGithubAccountsAsync();
            }
            catch (Exception ex)
            {
                AppendErrorLog(Helpers.Localization.T("rt.172") + ex.Message);
                StatusMessage = Helpers.Localization.T("rt.173") + ex.Message;
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
                MessageBox.Show(Helpers.Localization.T("msg.no_login_in_progress"), Helpers.Localization.T("msg.title_hint"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _loginCancelled = true;
            KillCurrentLoginProcess();
            _isGithubLoggingIn = false;
            IsDeviceAuthVisible = false;
            DeviceCode = string.Empty;
            GithubUrl = string.Empty;
            AppendErrorLog(Helpers.Localization.T("rt.174"));
            StatusMessage = Helpers.Localization.T("rt.175");
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
                StatusMessage = Helpers.Localization.T("rt.176") + url;
            }
            catch (Exception ex)
            {
                MessageBox.Show(Helpers.Localization.T("msg.open_browser_failed") + ex.Message + Helpers.Localization.T("msg.manual_visit") + url,
                    Helpers.Localization.T("msg.title_hint"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 把设备码复制到剪贴板
        /// </summary>
        private void CopyDeviceCode()
        {
            if (string.IsNullOrEmpty(DeviceCode))
            {
                MessageBox.Show(Helpers.Localization.T("msg.no_device_code"), Helpers.Localization.T("msg.title_hint"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                Clipboard.SetText(DeviceCode);
                StatusMessage = Helpers.Localization.T("rt.177") + DeviceCode + Helpers.Localization.T("rt.178");
            }
            catch (Exception ex)
            {
                MessageBox.Show(Helpers.Localization.T("msg.copy_failed") + ex.Message, Helpers.Localization.T("msg.title_hint"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 刷新 gh 账号列表，并把 gh 的原始输出追加到命令记录
        /// </summary>
        private async Task RefreshGithubFromLogAsync(string reason = null,
            IProgress<(string Text, int Percent)> progress = null)
        {
            AppendCommandLine("gh auth status" + (string.IsNullOrEmpty(reason) ? "" : "   （" + reason + "）"));
            await RefreshGithubAsync(progress);
            if (GithubAccounts.Count == 0)
                AppendResultLog(Helpers.Localization.T("rt.179"));
            else
                AppendResultLog(GhAuthStatus.Length > 0 ? GhAuthStatus : Helpers.Localization.T("rt.180"));
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
                    AppendSuccessLog(Helpers.Localization.T("rt.181") + target);

                return removed.Count;
            }
            catch (Exception ex)
            {
                AppendErrorLog(Helpers.Localization.T("rt.182") + ex.Message);
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
                MessageBox.Show(Helpers.Localization.T("msg.select_logout_account"), Helpers.Localization.T("msg.title_hint"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                Helpers.Localization.T("msg.confirm_logout", target.Username, target.Host),
                Helpers.Localization.T("msg.title_confirm_logout"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            IsBusy = true;
            StatusMessage = Helpers.Localization.T("rt.183", target.Username);
            try
            {
                AppendCommandLine("gh auth logout --hostname " + target.Host + " --user " + target.Username);
                var result = await _ghService.LogoutAsync(target.Username, target.Host);
                if (result.Success)
                {
                    SelectedGithubAccount = null;
                    await RefreshGithubFromLogAsync(Helpers.Localization.T("rt.184"));
                    // gh auth logout 不会清理 keyring，这里补上
                    var cleaned = CleanupOrphanedCredentials();
                    StatusMessage = cleaned > 0
                        ? Helpers.Localization.T("rt.185", target.Username, cleaned)
                        : Helpers.Localization.T("rt.186", target.Username);
                }
                else
                {
                    StatusMessage = Helpers.Localization.T("rt.187") + result.Message;
                    MessageBox.Show(result.Message, Helpers.Localization.T("msg.logout_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = Helpers.Localization.T("rt.188") + ex.Message;
                MessageBox.Show(ex.Message, Helpers.Localization.T("msg.logout_exception"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(Helpers.Localization.T("msg.bad_dir_path"), Helpers.Localization.T("msg.title_error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (!System.IO.Directory.Exists(dir))
            {
                try { System.IO.Directory.CreateDirectory(dir); }
                catch (Exception ex)
                {
                    MessageBox.Show(Helpers.Localization.T("msg.dir_create_failed") + ex.Message, Helpers.Localization.T("msg.title_error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(Helpers.Localization.T("msg.open_dir_failed") + ex.Message, Helpers.Localization.T("msg.title_error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                        MessageBox.Show(Helpers.Localization.T("msg.dir_missing") + (dir ?? path), Helpers.Localization.T("msg.title_hint"), MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "\"" + dir + "\"",
                        UseShellExecute = true
                    });
                }
                StatusMessage = Helpers.Localization.T("rt.189") + path;
            }
            catch (Exception ex)
            {
                MessageBox.Show(Helpers.Localization.T("msg.open_parent_failed") + ex.Message + Helpers.Localization.T("msg.path_suffix") + path,
                    Helpers.Localization.T("msg.title_error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SaveSelectedConfigItemAsync()
        {
            if (SelectedConfigItem == null) return;
            var result = await _gitService.SetConfigAsync(SelectedConfigItem.FullKey, SelectedConfigItem.Value);
            if (result.Success)
            {
                SelectedConfigItem.IsModified = false;
                StatusMessage = Helpers.Localization.T("rt.190", SelectedConfigItem.FullKey);
            }
            else
            {
                MessageBox.Show(result.Message, Helpers.Localization.T("msg.save_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeleteSelectedConfigItemAsync()
        {
            if (SelectedConfigItem == null) return;
            var r = MessageBox.Show(Helpers.Localization.T("msg.confirm_delete_config", SelectedConfigItem.FullKey),
                Helpers.Localization.T("msg.title_confirm"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            var result = await _gitService.UnsetConfigAsync(SelectedConfigItem.FullKey);
            if (result.Success)
            {
                ConfigItems.Remove(SelectedConfigItem);
                SelectedConfigItem = null;
                StatusMessage = Helpers.Localization.T("rt.191");
            }
            else
            {
                MessageBox.Show(result.Message, Helpers.Localization.T("msg.delete_failed"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
