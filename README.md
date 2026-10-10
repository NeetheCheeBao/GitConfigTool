<div align="center">
<h1> Git Config Tool </h1>
<img src="assets/icon.ico" width="128" alt="logo" />

> **Windows平台 `Git` 多账号快速切换工具**

[![Platform](https://img.shields.io/badge/Platform-Windows_10%2F11-0078D4?logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![Runtime](https://img.shields.io/badge/Runtime-.NET_Framework_4.8-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet-framework)
[![Language](https://img.shields.io/badge/Language-C%23-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![UI](https://img.shields.io/badge/UI-WPF-68217A?logo=dotnet&logoColor=white)](https://learn.microsoft.com/dotnet/desktop/wpf/)
[![License](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

</div>

## 📷 截图展示

![img](screenshot/demo1.png)

## 🌟 特色

| | |
| :---: | :--- |
| **Git多账号档案** | 把常用的 Git 身份存成档案，一键切换|
| **一键应用** | 把档案的 `user.name` / `user.email` / 签名配置写入全局 `.gitconfig`|
| **GitHub CLI 多账号** | 实时读取 `gh` 已登录账号，登录、切换、退出都在界面完成|

## 📚 环境要求

| 项 | 要求 |
|----|------|
| 系统 | Windows 10 / 11 |
| 运行时 | .NET Framework 4.8（系统一般已预装） |
| 必需 | Git for Windows |
| 可选 | GitHub CLI |

> [!TIP]
> 请确保 Git 安装在`默认位置`或已加入 `PATH` 环境变量

## ⬇️ 下载使用

[![Releases](https://img.shields.io/badge/Download%20Releases-7C25FF?style=for-the-badge&logoColor=white")](https://github.com/NeetheCheeBao/GitConfigTool/releases)

## ⚙️ 工作原理

**Git 身份与签名**  `.gitconfig`（`%USERPROFILE%\.gitconfig`），本工具通过调用 `git config --global` 读写，不直接编辑文件，因此不会破坏配置格式

**GitHub CLI 账号** 的`token`存在Windows`凭据管理器`中，文本配置里只有"登录了哪些账号"。因此当前生效账号只能通过执行 `gh auth status` 读取，本工具不解析任何本地文件

**残留凭据清理**：`gh auth logout` 只删除配置，无法自动删除凭据库里的 token。本工具通过 Windows API（`CredEnumerate` / `CredDelete`）在启动时和退出后清理这些`无效凭据`

## 💾 数据存放

GitConfigTool档案数据保存在：

```text
%APPDATA%\GitConfigTool\GitConfig.json
```

## 🔧 本地构建

1. 克隆仓库

```bash
git clone https://github.com/NeetheCheeBao/GitConfigTool.git
```

或者

```bash
gh repo clone NeetheCheeBao/GitConfigTool
```

2. 一键编译

```bash
.\build.bat
```

3. 检查产物：`bin\Release\GitConfigTool.exe`

## 📂 项目结构

```text
GitConfigTool
|   .gitignore                        # Git忽略配置
|   App.xaml                          # 应用入口：全局样式
|   App.xaml.cs                       # 应用类（仅声明）
|   build.bat                         # 一键构建脚本
|   GitConfigTool.csproj              # 项目文件：net48 / WPF / 单 exe 配置
|   GitConfigTool.sln                 # Visual Studio 解决方案
|   LICENSE                           # 许可证
|   README.md                         # 自述文件
|   
+---assets                            # 静态资源
|       icon.ico                      # 程序图标
|       
+---helpers                           # 通用工具类
|       CredentialCleaner.cs          # 清理 gh 在系统凭据库里的残留 token
|       ExecutableLocator.cs          # 定位 git / gh 及 Git 自带工具的真实路径
|       RelayCommand.cs               # ICommand 实现（同步 + 异步两个版本）
|       
+---models                            # 数据模型
|       ConfigDisplayRow.cs           # 「当前全局配置」展示行
|       GitConfigItem.cs              # 单条 git config 项（键 / 值 / 作用域）
|       GithubAccount.cs              # gh 账号（用户名 / 主机 / 是否生效）
|       GitProfile.cs                 # Git 账号档案（身份 + 签名 + 凭据配置）
|       LogEntry.cs                   # 命令记录条目 + 日志类型枚举（决定颜色）
|               
+---screenshot                        # 界面截图
|       demo1.png                     # 演示截图
|       
+---services                          # 业务服务层（对外部命令与文件的封装）
|       GithubCliService.cs           # gh 账号管理：状态解析 / 登录 / 切换 / 退出
|       GitService.cs                 # git 操作：配置读写 / 应用档案 / 读当前身份
|       ProfileStorageService.cs      # 档案持久化（手写 JSON 读写 + 旧文件迁移）
|       
+---viewmodels                        # 视图模型
|       MainViewModel.cs              # 主视图模型：初始化与全部命令
|       
\---views                             # 视图（窗口与对话框）
        ConfigWindow.xaml             # 完整配置独立窗口（浏览 / 编辑 config 项）
        ConfigWindow.xaml.cs          # 复用主视图模型作为数据源
        MainWindow.xaml               # 主窗口：四个标签页的全部界面
        MainWindow.xaml.cs            # 档案编辑回调、命令记录着色渲染
        ProfileEditWindow.xaml        # 档案编辑对话框（身份 / 签名 / 凭据字段）
        ProfileEditWindow.xaml.cs     # 保存前校验必填项
```

## ⭕ 已知限制

- `gh auth switch` 是**全局**切换，`gh` 不支持按仓库指定账号
- 切换 GitHub 账号**不会**联动切换 Git 提交身份，两者是独立配置
- 本工具只管理全局配置，不读写仓库级 `.git/config`
- 退出 GitHub 账号只删除Windows本地凭据，不会吊销服务端授权
- Git / GitHub CLI 只按「注册表 / `where.exe` / PATH」定位，安装在自定义目录且未加入 PATH 时无法找到

## ⚖️ 许可证

本项目采用 MIT 许可证 - 详情请参阅 [LICENSE](LICENSE) 文件