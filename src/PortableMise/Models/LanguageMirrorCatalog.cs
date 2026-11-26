namespace PortableMise.Models;

public sealed record LanguageMirrorPreset(string Id, string Label, string[] Values);

public sealed record LanguageMirrorProfile(
    string Id, string Label, string[] ToolIds, string[] Keys,
    IReadOnlyList<LanguageMirrorPreset> Presets, string? ConfigKind = null, string Description = "");

public static class LanguageMirrorCatalog
{
    // Endpoint data adapted from RubyMetric/chsrc at 03dd727e76b3510fc0f26f74c5cb1ec13702b0e0.
    // Commented-out entries in that source are intentionally omitted.
    private static readonly LanguageMirrorPreset[] Node =
    [
        new("official", "官方源", ["https://nodejs.org/dist/"]),
        new("npmmirror", "npmmirror", ["https://registry.npmmirror.com/-/binary/node"]),
        new("tuna", "TUNA", ["https://mirrors.tuna.tsinghua.edu.cn/nodejs-release/"]),
        new("bfsu", "BFSU", ["https://mirrors.bfsu.edu.cn/nodejs-release/"]),
        new("ustc", "USTC", ["https://mirrors.ustc.edu.cn/node/"]),
        new("huawei", "Huawei Cloud", ["https://mirrors.huaweicloud.com/nodejs/"]),
        new("tencent", "Tencent Cloud", ["https://mirrors.cloud.tencent.com/nodejs-release/"]),
    ];

    private static readonly LanguageMirrorPreset[] Npm =
    [
        new("official", "官方源", ["https://registry.npmjs.org/"]),
        new("npmmirror", "npmmirror", ["https://registry.npmmirror.com"]),
        new("huawei", "Huawei Cloud", ["https://mirrors.huaweicloud.com/repository/npm/"]),
        new("tencent", "Tencent Cloud", ["https://mirrors.cloud.tencent.com/npm/"]),
    ];

    private static readonly LanguageMirrorPreset[] GoProxy =
    [
        new("official", "官方源", ["https://proxy.golang.org,direct"]),
        new("goproxy-cn", "Goproxy.cn", ["https://goproxy.cn,direct"]),
        new("ali", "Alibaba Cloud", ["https://mirrors.aliyun.com/goproxy/,direct"]),
        new("goproxy-io", "Goproxy.io", ["https://goproxy.io,direct"]),
    ];

    private static readonly LanguageMirrorPreset[] PyPi =
    [
        new("official", "官方源", ["https://pypi.org/simple"]),
        new("mirrorz", "MirrorZ / CERNET", ["https://mirrors.cernet.edu.cn/pypi/web/simple"]),
        new("bfsu", "BFSU", ["https://mirrors.bfsu.edu.cn/pypi/web/simple"]),
        new("lzuoss", "LZUOSS", ["https://mirror.lzu.edu.cn/pypi/web/simple"]),
        new("sjtug-siyuan", "SJTUG Siyuan", ["https://mirror.sjtu.edu.cn/pypi/web/simple"]),
        new("tuna", "TUNA", ["https://mirrors.tuna.tsinghua.edu.cn/pypi/web/simple"]),
        new("ustc", "USTC", ["https://mirrors.ustc.edu.cn/pypi/simple"]),
        new("ali", "Alibaba Cloud", ["https://mirrors.aliyun.com/pypi/simple"]),
        new("nju", "NJU", ["https://mirror.nju.edu.cn/pypi/web/simple"]),
        new("pku", "PKU", ["https://mirrors.pku.edu.cn/pypi/web/simple"]),
        new("tencent", "Tencent Cloud", ["https://mirrors.cloud.tencent.com/pypi/simple"]),
        new("huawei", "Huawei Cloud", ["https://mirrors.huaweicloud.com/repository/pypi/simple"]),
        new("hust", "HUST", ["https://mirrors.hust.edu.cn/pypi/web/simple"]),
    ];

    private static readonly LanguageMirrorPreset[] PythonBuild =
    [
        new("official", "官方源", ["https://github.com/astral-sh/python-build-standalone/releases/download"]),
        new("nju", "NJU", ["https://mirrors.nju.edu.cn/github-release/astral-sh/python-build-standalone"]),
        new("ustc", "USTC", ["https://mirrors.ustc.edu.cn/github-release/astral-sh/python-build-standalone"]),
        new("lzuoss", "LZUOSS", ["https://mirror.lzu.edu.cn/github-release/astral-sh/python-build-standalone"]),
        new("cnb", "CNB", ["https://cnb.cool/astral-sh/python-build-standalone/-/releases/download"]),
    ];

    private static readonly LanguageMirrorPreset[] Rustup =
    [
        new("official", "官方源", ["https://static.rust-lang.org", "https://static.rust-lang.org/rustup"]),
        new("mirrorz", "MirrorZ / CERNET", ["https://mirrors.cernet.edu.cn/rustup", "https://mirrors.cernet.edu.cn/rustup/rustup"]),
        new("tuna", "TUNA", ["https://mirrors.tuna.tsinghua.edu.cn/rustup", "https://mirrors.tuna.tsinghua.edu.cn/rustup/rustup"]),
        new("ustc", "USTC", ["https://mirrors.ustc.edu.cn/rust-static", "https://mirrors.ustc.edu.cn/rust-static/rustup"]),
        new("nju", "NJU", ["https://mirror.nju.edu.cn/rustup", "https://mirror.nju.edu.cn/rustup/rustup"]),
        new("zju", "ZJU", ["https://mirrors.zju.edu.cn/rustup", "https://mirrors.zju.edu.cn/rustup/rustup"]),
        new("ha", "HA", ["https://mirrors.ha.edu.cn/rustup", "https://mirrors.ha.edu.cn/rustup/rustup"]),
        new("rsproxy", "RsProxy", ["https://rsproxy.cn", "https://rsproxy.cn/rustup"]),
    ];

    private static readonly LanguageMirrorPreset[] Cargo =
    [
        new("official", "官方源", [""]),
        new("mirrorz", "MirrorZ / CERNET", ["sparse+https://mirrors.cernet.edu.cn/crates.io-index/"]),
        new("rsproxy", "RsProxy", ["sparse+https://rsproxy.cn/index/"]),
        new("ali", "Alibaba Cloud", ["sparse+https://mirrors.aliyun.com/crates.io-index/"]),
        new("zju", "ZJU", ["sparse+https://mirrors.zju.edu.cn/crates.io-index/"]),
        new("sjtug-zhiyuan", "SJTUG Zhiyuan", ["sparse+https://mirrors.sjtug.sjtu.edu.cn/crates.io-index/"]),
        new("tuna", "TUNA", ["sparse+https://mirrors.tuna.tsinghua.edu.cn/crates.io-index/"]),
        new("bfsu", "BFSU", ["sparse+https://mirrors.bfsu.edu.cn/crates.io-index/"]),
        new("ustc", "USTC", ["sparse+https://mirrors.ustc.edu.cn/crates.io-index/"]),
    ];

    private static readonly LanguageMirrorPreset[] Flutter =
    [
        new("official", "官方源", ["https://storage.googleapis.com"]),
        new("flutter-cn", "Flutter China", ["https://storage.flutter-io.cn"]),
        new("sjtug-zhiyuan", "SJTUG Zhiyuan", ["https://mirror.sjtu.edu.cn"]),
        new("tuna", "TUNA", ["https://mirrors.tuna.tsinghua.edu.cn/flutter"]),
        new("nju", "NJU", ["https://mirror.nju.edu.cn/flutter"]),
    ];

    private static readonly LanguageMirrorPreset[] Pub =
    [
        new("official", "官方源", ["https://pub.dev"]),
        new("flutter-cn", "Flutter China", ["https://pub.flutter-io.cn"]),
        new("sjtug-zhiyuan", "SJTUG Zhiyuan", ["https://mirror.sjtu.edu.cn/dart-pub"]),
        new("tuna", "TUNA", ["https://mirrors.tuna.tsinghua.edu.cn/dart-pub"]),
        new("nju", "NJU", ["https://mirror.nju.edu.cn/dart-pub"]),
    ];

    private static readonly LanguageMirrorPreset[] Julia =
    [
        new("official", "官方源", ["https://pkg.julialang.org"]),
        new("pku", "PKU", ["https://mirrors.pku.edu.cn/julia"]),
        new("nju", "NJU", ["https://mirror.nju.edu.cn/julia"]),
        new("iscas", "ISCAS", ["https://mirror.iscas.ac.cn/julia"]),
    ];

    private static readonly LanguageMirrorPreset[] Maven =
    [
        new("official", "官方源", ["https://repo1.maven.org/maven2/"]),
        new("ali", "Alibaba Cloud", ["https://maven.aliyun.com/repository/public/"]),
        new("huawei", "Huawei Cloud", ["https://mirrors.huaweicloud.com/repository/maven/"]),
        new("huawei-cdn", "Huawei Cloud CDN", ["https://repo.huaweicloud.com/repository/maven/"]),
        new("tencent", "Tencent Cloud", ["https://mirrors.cloud.tencent.com/nexus/repository/maven-public/"]),
    ];

    private static readonly LanguageMirrorPreset[] NuGet =
    [
        new("official", "官方源", ["https://api.nuget.org/v3/index.json"]),
        new("huawei", "Huawei Cloud", ["https://mirrors.huaweicloud.com/repository/nuget/v3/index.json"]),
    ];

    private static readonly LanguageMirrorPreset[] RubyGems =
    [
        new("official", "官方源", ["https://rubygems.org/"]),
        new("ustc", "USTC", ["https://mirrors.ustc.edu.cn/rubygems/"]),
    ];

    private static readonly LanguageMirrorPreset[] Cran =
    [
        new("official", "官方源", ["https://cran.r-project.org/"]),
        new("sjtug-zhiyuan", "SJTUG Zhiyuan", ["https://mirrors.sjtug.sjtu.edu.cn/cran/"]),
        new("tuna", "TUNA", ["https://mirrors.tuna.tsinghua.edu.cn/CRAN/"]),
    ];

    private static readonly LanguageMirrorPreset[] Cabal =
    [
        new("official", "官方源", ["https://hackage.haskell.org"]),
        new("tuna", "TUNA", ["https://mirrors.tuna.tsinghua.edu.cn/hackage"]),
        new("bfsu", "BFSU", ["https://mirrors.bfsu.edu.cn/hackage"]),
        new("nju", "NJU", ["https://mirror.nju.edu.cn/hackage"]),
        new("ustc", "USTC", ["https://mirrors.ustc.edu.cn/hackage"]),
        new("iscas", "ISCAS", ["https://mirror.iscas.ac.cn/hackage"]),
        new("nyist", "NYIST", ["https://mirror.nyist.edu.cn/hackage"]),
    ];

    private static readonly LanguageMirrorPreset[] Composer =
    [
        new("official", "官方源", ["https://packagist.org/"]),
        new("ali", "Alibaba Cloud", ["https://mirrors.aliyun.com/composer/"]),
        new("tencent", "Tencent Cloud", ["https://mirrors.tencent.com/composer/"]),
        new("huawei", "Huawei Cloud", ["https://mirrors.huaweicloud.com/repository/php/"]),
    ];

    private static readonly LanguageMirrorPreset[] LuaRocks =
    [
        new("official", "官方源", ["https://luarocks.org"]),
        new("api7", "API7.ai", ["https://luarocks.cn"]),
    ];

    private static readonly LanguageMirrorPreset[] Clojars =
    [
        new("official", "官方源", ["https://repo.clojars.org/"]),
        new("mirrorz", "MirrorZ / CERNET", ["https://mirrors.cernet.edu.cn/clojars/"]),
        new("tuna", "TUNA", ["https://mirrors.tuna.tsinghua.edu.cn/clojars/"]),
        new("nju", "NJU", ["https://mirror.nju.edu.cn/clojars/"]),
        new("nyist", "NYIST", ["https://mirror.nyist.edu.cn/clojars/"]),
        new("ustc", "USTC", ["https://mirrors.ustc.edu.cn/clojars/"]),
        new("iscas", "ISCAS", ["https://mirror.iscas.ac.cn/clojars/"]),
    ];

    private static readonly LanguageMirrorPreset[] Cpan =
    [
        new("official", "官方源", ["https://www.cpan.org/"]),
        new("mirrorz", "MirrorZ / CERNET", ["https://mirrors.cernet.edu.cn/CPAN/"]),
        new("tuna", "TUNA", ["https://mirrors.tuna.tsinghua.edu.cn/CPAN/"]),
        new("bfsu", "BFSU", ["https://mirrors.bfsu.edu.cn/CPAN/"]),
        new("bjtu", "BJTU", ["https://mirror.bjtu.edu.cn/cpan/"]),
        new("nju", "NJU", ["https://mirrors.nju.edu.cn/CPAN/"]),
        new("nyist", "NYIST", ["https://mirror.nyist.edu.cn/CPAN/"]),
        new("ustc", "USTC", ["https://mirrors.ustc.edu.cn/CPAN/"]),
        new("sjtug-siyuan", "SJTUG Siyuan", ["https://mirror.sjtu.edu.cn/cpan/"]),
        new("iscas", "ISCAS", ["https://mirror.iscas.ac.cn/CPAN/"]),
        new("zju", "ZJU", ["https://mirrors.zju.edu.cn/CPAN/"]),
        new("lzuoss", "LZUOSS", ["https://mirrors.lzu.edu.cn/CPAN/"]),
        new("hust", "HUST", ["https://mirrors.hust.edu.cn/CPAN/"]),
        new("ali", "Alibaba Cloud", ["https://mirrors.aliyun.com/CPAN/"]),
    ];

    private static readonly LanguageMirrorPreset[] Opam =
    [
        new("official", "官方源", ["https://opam.ocaml.org/"]),
        new("sjtug-zhiyuan", "SJTUG Zhiyuan", ["https://mirrors.sjtug.sjtu.edu.cn/git/opam-repository.git"]),
    ];

    public static IReadOnlyList<LanguageMirrorProfile> All { get; } =
    [
        new("node-binary", "Node.js 下载镜像", ["node"], ["settings.node.mirror_url"], Node,
            Description: "用于 mise 安装 Node.js，镜像需提供安装包及 SHASUMS256.txt。"),
        new("npm-registry", "npm / pnpm 软件包源", ["node", "npm", "pnpm"], ["env.NPM_CONFIG_REGISTRY"], Npm,
            Description: "用于 npm 和 pnpm 软件包下载，独立于 Node.js 本体的下载源。"),
        new("yarn-registry", "Yarn 软件包源", ["node", "yarn"], ["env.YARN_NPM_REGISTRY_SERVER"], Npm,
            Description: "适用于现代 Yarn。Yarn Classic 可使用 npm / pnpm 软件包源。"),
        new("bun-registry", "Bun 软件包源", ["bun"], [], Npm, "bun",
            "用于 bun install，通过生成的工作区 Bun 配置使用。"),
        new("nvm-node-binary", "nvm 下载镜像", ["nvm"], ["env.NVM_NODEJS_ORG_MIRROR"], Node,
            Description: "适用于 nvm-sh。mise 安装 Node.js 时请选择 Node.js 下载镜像。"),
        new("go-download", "Go 下载镜像", ["go"], ["settings.go.download_mirror"],
            [
                new("official", "官方源", ["https://dl.google.com/go"]),
                new("google-cn", "Google 中国", ["https://golang.google.cn/dl"]),
            ],
            Description: "用于 mise 下载 Go 本体，镜像需提供安装包及对应的 SHA-256 文件。"),
        new("go-proxy", "Go 模块代理", ["go"], ["env.GOPROXY"], GoProxy,
            Description: "用于 Go 模块依赖下载，保留 direct 回退。"),
        new("pip-index", "pip 软件包源", ["python", "pip", "pipx"], ["env.PIP_INDEX_URL"], PyPi,
            Description: "用于 pip 安装依赖，独立于 Python 本体的下载源。"),
        new("uv-index", "uv 软件包源", ["python", "uv"], ["env.UV_DEFAULT_INDEX"], PyPi,
            Description: "用于 uv 安装 Python 软件包。"),
        new("uv-python-build", "uv Python 下载镜像", ["python", "uv"], ["env.UV_PYTHON_INSTALL_MIRROR"], PythonBuild,
            Description: "用于 uv python install，不改变 mise 的 Python 下载源。"),
        new("pdm-index", "PDM 软件包源", ["python", "pdm"], ["env.PDM_PYPI_URL"], PyPi),
        new("poetry-index", "Poetry 软件包源", ["python", "poetry"], [], PyPi, "poetry",
            "Poetry 镜像属于具体项目。生成使用说明，可在所需项目中应用。"),
        new("rye-index", "Rye 软件包源", ["python", "rye"], [], PyPi, "rye"),
        new("rustup", "Rustup 下载镜像", ["rust", "rustup"], ["env.RUSTUP_DIST_SERVER", "env.RUSTUP_UPDATE_ROOT"], Rustup,
            Description: "成对设置 Rustup 工具链地址和更新地址。"),
        new("cargo", "Cargo 依赖镜像", ["rust", "cargo"], ["cargo-mirror"], Cargo,
            Description: "用于 Cargo 依赖下载。选回官方源会移除工具箱生成的替代源。"),
        new("flutter-storage", "Flutter 下载镜像", ["flutter"], ["env.FLUTTER_STORAGE_BASE_URL"], Flutter,
            Description: "用于 Flutter 下载组件和构建产物。"),
        new("dart-pub", "Dart / Flutter 软件包源", ["dart", "flutter"], ["env.PUB_HOSTED_URL"], Pub),
        new("julia-pkg", "Julia 软件包源", ["julia"], ["env.JULIA_PKG_SERVER"], Julia),
        new("maven", "Maven / Gradle 镜像", ["java", "maven", "mvn", "mvnd", "gradle"], [], Maven, "maven",
            "生成 Maven 设置文件和 Gradle 初始化脚本，独立于 JDK 下载。"),
        new("nuget", "NuGet 软件包源", ["dotnet", "nuget"], [], NuGet, "nuget",
            "生成供 dotnet restore 使用的配置文件，独立于 .NET SDK 下载。"),
        new("rubygems", "RubyGems 镜像", ["ruby", "gem", "bundler"], [], RubyGems, "rubygems"),
        new("cran", "R / CRAN 镜像", ["r", "r-lang"], [], Cran, "r"),
        new("hackage", "Haskell / Hackage 镜像", ["haskell", "ghc", "cabal", "stack"], [], Cabal, "haskell"),
        new("packagist", "Composer 软件包源", ["php", "composer"], [], Composer, "composer"),
        new("luarocks", "LuaRocks 镜像", ["lua", "luarocks"], [], LuaRocks, "luarocks"),
        new("clojars", "Clojure / Clojars 镜像", ["clojure", "leiningen", "lein"], [], Clojars, "clojure"),
        new("cpan", "Perl / CPAN 镜像", ["perl", "cpan", "cpanm"], [], Cpan, "perl"),
        new("opam", "OCaml / opam 镜像", ["ocaml", "opam"], [], Opam, "opam"),
    ];

    public static IEnumerable<LanguageMirrorProfile> ForTool(string id)
    {
        var core = CoreToolCatalog.Find(id);
        string normalized = core?.Id ?? id;
        return All.Where(profile => profile.ToolIds.Contains(normalized, StringComparer.OrdinalIgnoreCase));
    }
}
