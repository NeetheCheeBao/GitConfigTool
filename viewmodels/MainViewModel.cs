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
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly GitService _gitService = new GitService();
        private readonly GithubCliService _ghService = new GithubCliService();
        private readonly ProfileStorageService _storage = new ProfileStorageService();


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
        private int _selectedTabIndex;

        public ObservableCollection<GitProfile> Profiles { get; } = new ObservableCollection<GitProfile>();
        public ObservableCollection<GithubAccount> GithubAccounts { get; } = new ObservableCollection<GithubAccount>();
        public ObservableCollection<GitConfigItem> ConfigItems { get; } = new ObservableCollection<GitConfigItem>();

        /// <summary>「当前全局配置」展示的键值行</summary>
        public ObservableCollection<ConfigDisplayRow> CurrentConfigRows { get; } = new ObservableCollection<ConfigDisplayRow>();

        public GitProfile EditingProfile { get; set; } = new GitProfile();

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
            set { _gitConfigPath = value; OnPropertyChanged(); }
        }

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
        /// 是否显示设备授权面板（开始登录即显示，便于用户随时手动打开授权页）
        /// </summary>
        public bool IsDeviceAuthVisible
        {
            get => _isDeviceAuthVisible;
            set { _isDeviceAuthVisible = value; OnPropertyChanged(); }
        }

        /// <summary>设备码为空时给出占位提示，避免文本框看起来是空的</summary>
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

        public MainViewModel()
        {
            RefreshCommand = new AsyncRelayCommand(async _ => await RefreshAllAsync());
            ApplyProfileCommand = new AsyncRelayCommand(async _ => await ApplySelectedProfileAsync(), _ => SelectedProfile != null);
            AddProfileCommand = new RelayCommand(_ => StartAddProfile());
            EditProfileCommand = new RelayCommand(_ => StartEditProfile(), _ => SelectedProfile != null);
            DeleteProfileCommand = new AsyncRelayCommand(async _ => await DeleteSelectedProfileAsync(), _ => SelectedProfile != null);
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

        private async Task LoadProfilesAsync()
        {
            var list = await _storage.LoadProfilesAsync();
            Profiles.Clear();
            foreach (var p in list.OrderByDescending(x => x.LastUsedAt))
                Profiles.Add(p);

            var user = await _gitService.GetCurrentUserAsync();
            foreach (var p in Profiles)
            {
                p.IsActive = string.Equals(p.UserName, user.Name, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(p.Email, user.Email, StringComparison.OrdinalIgnoreCase);
            }
        }

        private async Task RefreshCurrentUserAsync()
        {
            var user = await _gitService.GetCurrentUserAsync();

            CurrentUserName = user.Name ?? "(未设置)";
            CurrentUserEmail = user.Email ?? "(未设置)";
            CurrentSigningKey = user.SigningKey ?? "(未设置)";
            CurrentSigningFormat = user.SigningFormat ?? "openpgp";
            CurrentCommitSign = user.CommitSign;

            // 「当前全局配置」展示的键值列表（数据驱动，增减键只改这里）
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
        /// 追加一行全局配置展示；值为空时不显示该行。
        /// 界面只展示中文名称，configKey 仅用于标识与将来扩展。
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

        /// <summary>
        /// 刷新 gh 账号列表（不写命令记录，供初始化等场景使用）
        /// </summary>
        private async Task RefreshGithubAsync()
        {
            GhAuthStatus = await _ghService.GetAuthStatusRawAsync();
            await RefreshGithubAccountsAsync();
        }

        /// <summary>
        /// 只重新读取账号列表（gh 原始输出已在刷新前取得）
        /// </summary>
        private async Task RefreshGithubAccountsAsync()
        {
            GithubAccounts.Clear();
            var accounts = await _ghService.GetLoggedInAccountsAsync();
            foreach (var a in accounts)
                GithubAccounts.Add(a);
        }

        private async Task ApplySelectedProfileAsync()
        {
            if (SelectedProfile == null) return;
            IsBusy = true;
            StatusMessage = $"正在应用档案「{SelectedProfile.UserName}」...";
            try
            {
                var result = await _gitService.ApplyProfileAsync(SelectedProfile);
                if (result.Success)
                {
                    SelectedProfile.LastUsedAt = DateTime.Now;
                    SelectedProfile.IsActive = true;
                    foreach (var p in Profiles)
                        if (p.Id != SelectedProfile.Id) p.IsActive = false;
                    await _storage.UpsertProfileAsync(SelectedProfile);
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

        private void StartEditProfile()
        {
            if (SelectedProfile == null) return;
            EditingProfile = new GitProfile
            {
                Id = SelectedProfile.Id,
                UserName = SelectedProfile.UserName,
                Email = SelectedProfile.Email,
                SigningKey = SelectedProfile.SigningKey,
                SigningFormat = SelectedProfile.SigningFormat,
                CommitGpgsign = SelectedProfile.CommitGpgsign,
                TagGpgsign = SelectedProfile.TagGpgsign,
                SshKeyPath = SelectedProfile.SshKeyPath,
                GithubUsername = SelectedProfile.GithubUsername,
                CredentialHelper = SelectedProfile.CredentialHelper,
                SslBackend = SelectedProfile.SslBackend,
                CreatedAt = SelectedProfile.CreatedAt,
                LastUsedAt = SelectedProfile.LastUsedAt
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

        private async Task DeleteSelectedProfileAsync()
        {
            if (SelectedProfile == null) return;
            var result = MessageBox.Show($"确定删除档案「{SelectedProfile.UserName}」吗？", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            await _storage.DeleteProfileAsync(SelectedProfile.Id);
            await LoadProfilesAsync();
            await LoadProfilesJsonAsync();
            SelectedProfile = null;
            StatusMessage = "档案已删除";
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
        /// 切换 gh 账号；account 为空时使用当前选中项（顶部按钮走这条路）
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
        /// 登录新的 GitHub 账号（浏览器设备码授权）
        /// 不阻塞界面：gh 会一直等到用户完成浏览器授权，这里负责启动、读取设备码、开浏览器、轮询检测
        /// </summary>
        private async Task LoginGithubAsync()
        {
            if (_isGithubLoggingIn)
            {
                MessageBox.Show("已有登录流程正在进行，请先在浏览器中完成授权。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 清掉上一次残留的登录进程，避免堆积
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

                    // 2) 取授权地址，并由本程序打开浏览器（gh 自身打开浏览器在本机不可靠）
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

                    // 4) gh 进程自己退出了：说明本次登录结束（成功则上面已返回，走到这里就是失败）
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
        /// 结束当前登录进程（登录成功、失败或超时后都要清理，避免 gh 进程堆积）
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
        /// gh auth logout 不会删 keyring 条目，会留下 token 仍有效的孤儿凭据，因此这里补上。
        /// 静默执行，不弹窗，只在命令记录和状态栏体现。返回清理条数。
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
                    // gh auth logout 不会清理 keyring，这里补上，避免留下 token 仍有效的孤儿凭据
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
        /// 刷新档案数据文件路径（原始内容展示已随「档案数据」标签页移除，这里只保留路径）
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
