namespace PortableMise.Models;

public sealed record ToolOptionDefinition(
    string Key, string Label, string Hint, bool IsBoolean = false, string[]? Choices = null);

public sealed record WorkspaceOptionDefinition(
    string Key, string Label, string Hint, bool IsBoolean = false, string[]? Choices = null, bool IsUrl = false);

public sealed record CoreToolDefinition(
    string Id, string Label, string DocumentationUrl, IReadOnlyList<ToolOptionDefinition> Options,
    string WorkspaceSettings = "", IReadOnlyList<WorkspaceOptionDefinition>? WorkspaceOptions = null);

public static class CoreToolCatalog
{
    // Core language and option names are from mise.jdx.dev/core-tools.html, the linked language
    // pages, and the corresponding sections of jdx/mise/settings.toml.
    public static IReadOnlyList<CoreToolDefinition> Languages { get; } =
    [
        Tool("bun", "Bun", workspaceSettings: "Bun 使用官方发布的二进制文件。复杂安装环境可通过全局配置中的 install_env 设置。"),
        Tool("deno", "Deno", workspace:
        [
            EnvironmentValue("DENO_INSTALL_ROOT", "全局脚本目录", "自定义 Deno 安装的全局脚本目录。"),
        ]),
        Tool("dotnet", ".NET",
        [
            new("runtime", "仅安装运行时", "留空安装 SDK。windowsdesktop 适用于 Windows 桌面应用。", Choices: ["dotnet", "aspnetcore", "windowsdesktop"]),
        ], workspace:
        [
            Setting("dotnet", "registry_url", "NuGet 工具源", "用于查询 dotnet 全局工具版本的 NuGet 服务索引。", isUrl: true),
            Setting("dotnet", "isolated", "隔离安装", "为每个 SDK 版本使用独立目录。", isBoolean: true),
            Setting("dotnet", "dotnet_root", "共享 SDK 目录", "指定多个 .NET SDK 版本共同使用的安装目录。"),
            Setting("dotnet", "cli_telemetry_optout", "关闭 CLI 遥测", "设置 DOTNET_CLI_TELEMETRY_OPTOUT。", isBoolean: true),
        ]),
        Tool("elixir", "Elixir", workspace:
        [
            EnvironmentValue("MIX_HOME", "Mix 数据目录", "为不同 Elixir 版本指定共享的 Mix 数据目录。"),
            EnvironmentValue("MIX_ARCHIVES", "Mix 归档目录", "覆盖 Mix 查找归档的目录。"),
        ], workspaceSettings: "请选择与 Erlang/OTP 版本兼容的 Elixir。"),
        Tool("erlang", "Erlang", workspace:
        [
            Setting("erlang", "compile", "从源码编译", "需要支持 kerl 的平台和构建工具。原生 Windows 安装通常使用预编译包。", isBoolean: true),
            Setting("erlang", "precompiled_os", "Linux 预编译版本", "仅适用于 Linux 的 Ubuntu 预编译包。",
                choices: ["ubuntu-20.04", "ubuntu-22.04", "ubuntu-24.04", "ubuntu-26.04"]),
        ], workspaceSettings: "源码编译时可在全局配置的 install_env 中设置 KERL_CONFIGURE_OPTIONS。"),
        Tool("go", "Go", workspace:
        [
            Setting("go", "download_mirror", "Go 下载镜像", "镜像需提供 Go 安装包及对应的 SHA-256 文件。", isUrl: true),
            Setting("go", "repo", "Go 版本仓库", "mise 查询 Go 版本所用的 Git 仓库。", isUrl: true),
            EnvironmentValue("GOPROXY", "Go 模块代理", "依赖下载使用的 GOPROXY。可填写逗号分隔的回退列表。"),
            Setting("go", "set_gobin", "设置 GOBIN", "使 go install 的输出跟随已激活的 Go 版本。", isBoolean: true),
            Setting("go", "set_goroot", "设置 GOROOT", "将 GOROOT 指向当前 Go 安装。", isBoolean: true),
        ]),
        Tool("java", "Java",
        [
            new("release_type", "发布类型", "ga 为正式版，ea 为早期访问版。", Choices: ["ga", "ea"]),
        ], workspace:
        [
            Setting("java", "shorthand_vendor", "默认 JDK 发行版", "为 java@21 这类未指定发行版的版本请求选择供应商，例如 temurin 或 openjdk。"),
        ]),
        Tool("node", "Node.js", workspace:
        [
            Setting("node", "mirror_url", "Node.js 下载镜像", "镜像需提供 Node.js 安装包及 SHASUMS256.txt。", isUrl: true),
            EnvironmentValue("NPM_CONFIG_REGISTRY", "npm 软件包源", "npm 安装依赖时使用的仓库。", isUrl: true),
            Setting("node", "corepack", "启用 Corepack", "安装 Node.js 后运行 corepack enable。", isBoolean: true),
            Setting("node", "npm_shim", "npm 安装后更新 shims", "全局安装 npm 工具后重新生成 shims。", isBoolean: true),
            Setting("node", "compile", "从源码编译", "覆盖使用预编译包的默认行为。需要系统编译工具。", isBoolean: true),
            Setting("node", "flavor", "Linux 构建类型", "指定 Linux 预编译包类型，例如 musl。"),
        ]),
        Tool("python", "Python",
        [
            new("patch_sysconfig", "修正 sysconfig 路径", "仅用于 Unix 预编译 Python。通常保留 mise 默认行为。", IsBoolean: true),
        ], workspace:
        [
            EnvironmentValue("PIP_INDEX_URL", "pip 软件包源", "pip 用于下载 Python 依赖的索引。", isUrl: true),
            Setting("python", "pyenv_repo", "Python 构建仓库", "mise 获取 python-build 的 pyenv Git 仓库地址。", isUrl: true),
            Setting("python", "precompiled_arch", "预编译架构", "覆盖预编译架构，例如 x86_64_v3 或 aarch64。"),
            Setting("python", "precompiled_flavor", "预编译构建类型", "例如 install_only 或 freethreaded-install_only_stripped。"),
            Setting("python", "precompiled_os", "预编译平台标识", "覆盖预编译包的平台标识。"),
            Setting("python", "venv_stdlib", "使用标准库创建 venv", "为 mise 的 _.python.venv 设置优先使用 Python 标准库。", isBoolean: true),
        ]),
        Tool("ruby", "Ruby", workspace:
        [
            Setting("ruby", "precompiled_url", "预编译包来源", "支持 owner/repo 或 URL 模板。Windows 上由 RubyInstaller2 提供构建。"),
            Setting("ruby", "ruby_build_repo", "ruby-build 仓库", "源码构建使用的 Git 仓库或 ZIP 文件地址。", isUrl: true),
            Setting("ruby", "ruby_build_opts", "Ruby 编译参数", "传给 Ruby configure 的参数，例如 --enable-yjit。"),
            Setting("ruby", "ruby_build_cli_opts", "ruby-build 命令参数", "传给 ruby-build 自身的参数，例如 --keep。"),
            Setting("ruby", "compile", "从源码编译", "覆盖预编译包选择；原生 Windows 使用 RubyInstaller2。", isBoolean: true),
        ]),
        Tool("rust", "Rust",
        [
            new("profile", "Rustup 安装配置", "minimal 仅安装核心工具。complete 可能包含当前版本无法安装的组件。",
                Choices: ["minimal", "default", "complete"]),
            new("components", "额外组件", "填写组件名，多个以英文逗号分隔，例如 rust-src,llvm-tools。"),
            new("targets", "交叉编译目标", "填写目标三元组，多个以英文逗号分隔，例如 wasm32-unknown-unknown。"),
            new("mr_boxington", "启用 mr-boxington", "通过 mr-boxington 共享 Cargo 构建缓存，需要同时安装它。", IsBoolean: true),
        ], workspace:
        [
            EnvironmentValue("RUSTUP_DIST_SERVER", "Rustup 工具链镜像", "Rustup 的工具链下载地址，例如 https://rsproxy.cn。", isUrl: true),
            EnvironmentValue("RUSTUP_UPDATE_ROOT", "Rustup 更新地址", "rustup-init 下载使用的根地址，例如 https://rsproxy.cn/rustup。", isUrl: true),
            Setting("rust", "default_host", "默认主机三元组", "传给 rustup-init 的默认主机平台，例如 x86_64-pc-windows-msvc。"),
            Setting("rust", "rustup_home", "Rustup 数据目录", "覆盖 mise 管理的 Rust 工具链所用的 RUSTUP_HOME。"),
            Setting("rust", "cargo_home", "Cargo 数据目录", "覆盖 mise 管理的 Rust 工具链所用的 CARGO_HOME。"),
        ], workspaceSettings: "Cargo 依赖镜像单独写入 Cargo 配置。"),
        Tool("swift", "Swift", workspace:
        [
            Setting("swift", "platform", "Linux 构建类型", "指定 Linux 发行版的 Swift 构建，例如 ubuntu24.04。"),
        ], workspaceSettings: "mise 的 Swift 核心后端支持 macOS 和 Linux。可为这些平台准备配置。"),
        Tool("zig", "Zig", workspace:
        [
            Setting("zig", "use_community_mirrors", "优先社区镜像", "先尝试 ziglang.org 推荐的社区下载镜像。", isBoolean: true),
        ]),
    ];

    public static IReadOnlyList<ToolOptionDefinition> CommonOptions { get; } =
    [
        new("postinstall", "安装后命令", "由 mise 在安装该工具后运行。"),
        new("minimum_release_age", "最低发布时长", "等待版本发布一段时间再安装，例如 3d。"),
        new("lazy", "首次使用时安装", "运行这个工具的命令时再安装。", IsBoolean: true),
        new("auto_update", "使用前自动更新", "仅在全局配置中生效。", IsBoolean: true),
        new("bump", "参与升级版本要求", "关闭后，mise upgrade --bump 会保留现有版本要求。", IsBoolean: true),
        new("prerelease", "允许预发行版", "将预发行版本纳入版本选择，支持情况取决于后端。", IsBoolean: true),
    ];

    public static CoreToolDefinition? Find(string name)
    {
        if (name.StartsWith("core:", StringComparison.OrdinalIgnoreCase))
            name = name[5..];
        if (name.Equals("dotnet-core", StringComparison.OrdinalIgnoreCase))
            name = "dotnet";
        if (name.Equals("nodejs", StringComparison.OrdinalIgnoreCase))
            name = "node";
        return Languages.FirstOrDefault(tool => tool.Id.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static CoreToolDefinition Tool(string id, string label, IReadOnlyList<ToolOptionDefinition>? options = null,
        IReadOnlyList<WorkspaceOptionDefinition>? workspace = null, string workspaceSettings = "") =>
        new(id, label, $"https://mise.jdx.dev/lang/{id}.html", options ?? [], workspaceSettings, workspace ?? []);

    private static WorkspaceOptionDefinition Setting(string tool, string key, string label, string hint,
        bool isBoolean = false, string[]? choices = null, bool isUrl = false) =>
        new($"settings.{tool}.{key}", label, hint, isBoolean, choices, isUrl);

    private static WorkspaceOptionDefinition EnvironmentValue(string key, string label, string hint, bool isUrl = false) =>
        new($"env.{key}", label, hint, IsUrl: isUrl);
}
