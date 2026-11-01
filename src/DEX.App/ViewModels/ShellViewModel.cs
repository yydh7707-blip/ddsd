using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using DEX.Core.Builders;
using DEX.Core.History;
using DEX.Core.Installation;
using DEX.Core.Localization;
using DEX.Core.Models;
using DEX.Core.Probes;
using DEX.Core.Security;
using DEX.Core.Services;
using Microsoft.Win32;

namespace DEX.App.ViewModels;

public sealed record LanguageOption(string Code, string Name);

public sealed class ShellViewModel : INotifyPropertyChanged
{
    private readonly ProjectAnalysisService _analysis;
    private readonly BuildService _build;
    private readonly EnvironmentDetectionService _environment;
    private readonly SettingsService _settingsService = new();
    private readonly HistoryService _history = new();
    private readonly OfficialDownloadService _downloads = new();
    private AppSettings _settings;
    private CancellationTokenSource? _buildCts;

    public ShellViewModel()
    {
        var runner = new ProcessRunner();
        var locator = new ToolLocator();
        _environment = new EnvironmentDetectionService(runner, locator);
        _analysis = new ProjectAnalysisService(_environment);
        _build = new BuildService(new BuildManager(runner, new BuildVerifier(), locator), _history);
        _settings = _settingsService.Load();
        NavigateCommand = new RelayCommand(p => Page = p?.ToString() ?? "home");
        BrowseFileCommand = new RelayCommand(_ => BrowseFile());
        BrowseFolderCommand = new RelayCommand(_ => BrowseFolder());
        RecheckCommand = new AsyncRelayCommand(async _ => await RecheckAsync());
        BuildCommand = new AsyncRelayCommand(async _ => await BuildAsync(), _ => CanBuild);
        OpenToolCommand = new RelayCommand(p => OpenTool(p?.ToString(), false));
        GuideToolCommand = new RelayCommand(p => OpenTool(p?.ToString(), true));
        OpenOutputCommand = new RelayCommand(_ => OpenOutput());
        RestartCommand = new RelayCommand(_ => Restart());
        SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
        ClearLogsCommand = new RelayCommand(_ => { _history.DeleteLogs(); Status = Text.T("빌드 로그를 삭제했습니다.", "Build logs deleted."); });
        CancelBuildCommand = new RelayCommand(_ => _buildCts?.Cancel());
        UseEntryCommand = new RelayCommand(p => { SelectedEntry = p?.ToString() ?? ""; RefreshReport(); });
        BuildAgainCommand = new AsyncRelayCommand(async p => await BuildAgainAsync(p?.ToString()));
        LoginCommand = new AsyncRelayCommand(async p => await LoginAsync(p as System.Windows.Controls.PasswordBox));
        LogoutCommand = new AsyncRelayCommand(async _ => await LogoutAsync());
        OpenSiteCommand = new RelayCommand(p => OpenSite(p?.ToString()));
        CheckDeviceCommand = new AsyncRelayCommand(async _ => await CheckDeviceAsync());
        _ = RestoreAccountAsync();
        _ = LoadHomeAsync().ContinueWith(_ => Ready = true, TaskScheduler.FromCurrentSynchronizationContext());
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private readonly DEX.App.Account.AccountClient _account = new();
    private DEX.App.Account.AccountInfo? _me;
    private bool _deviceRegistered;
    private string _accountMessage = "";
    private string _loginEmail = "";
    private bool _remember = true;

    public AsyncRelayCommand LoginCommand { get; }
    public AsyncRelayCommand LogoutCommand { get; }
    public RelayCommand OpenSiteCommand { get; }
    public AsyncRelayCommand CheckDeviceCommand { get; }

    public bool SignedIn => _me is not null;
    public bool SignedOut => _me is null;
    public string LoginEmail { get => _loginEmail; set { _loginEmail = value; Notify(); } }
    public bool RememberMe { get => _remember; set { _remember = value; Notify(); } }
    public string AccountMessage { get => _accountMessage; set { _accountMessage = value; Notify(); } }
    public string AccountEmail => _me?.Email ?? "";
    public string AccountPlan => _me?.Plan ?? "";
    public string AccountVerified => _me is null ? "" : _me.EmailVerified ? Text.T("인증됨", "Verified") : Text.T("미인증", "Not verified");
    public string DeviceStatus => !SignedIn ? "" : _deviceRegistered ? Text.T("이 기기는 등록되었습니다.", "This device is registered.") : Text.T("이 기기는 아직 등록되지 않았습니다. 웹사이트에서 등록하세요.", "This device is not registered yet. Register it on the website.");
    public string NavAccount => Text.T("계정", "Account");
    public string AccountTitle => Text.T("COMPIVOX 계정", "COMPIVOX account");
    public string AccountIntro => Text.T("빌드하려면 로그인하고 이 기기를 등록해야 합니다. 비밀번호는 저장하지 않습니다.", "Sign in and register this device to build. Your password is never stored.");
    public string EmailLabel => Text.T("이메일", "Email");
    public string PasswordLabel => Text.T("비밀번호", "Password");
    public string RememberLabel => Text.T("로그인 유지", "Keep me signed in");
    public string SignInLabel => Text.T("로그인", "Sign in");
    public string SignOutLabel => Text.T("로그아웃", "Sign out");
    public string CreateAccountLabel => Text.T("계정 만들기", "Create account");
    public string ForgotLabel => Text.T("비밀번호 찾기", "Forgot password");
    public string RegisterDeviceLabel => Text.T("이 기기 등록", "Register this device");
    public string RecheckDeviceLabel => Text.T("기기 상태 확인", "Check device");
    public string PlanLabel => Text.T("요금제", "Plan");

    private void NotifyAccount()
    {
        foreach (var n in new[] { nameof(SignedIn), nameof(SignedOut), nameof(AccountEmail), nameof(AccountPlan), nameof(AccountVerified), nameof(DeviceStatus) })
            Notify(n);
    }

    private static string AccountError(DEX.App.Account.ApiException ex) => ex.Code switch
    {
        "network" => Text.T("서버에 연결할 수 없습니다. 인터넷 연결을 확인하세요.", "Cannot reach the server. Check your internet connection."),
        "invalid_credentials" => Text.T("이메일 또는 비밀번호가 올바르지 않습니다.", "Email or password is incorrect."),
        "email_not_verified" => Text.T("먼저 웹사이트에서 이메일을 인증하세요.", "Verify your email on the website first."),
        "account_temporarily_locked" or "rate_limited" => Text.T("시도가 너무 많습니다. 잠시 후 다시 시도하세요.", "Too many attempts. Try again later."),
        "network_blocked" => Text.T("이 네트워크에서는 접속할 수 없습니다.", "This network is not allowed."),
        "ip_verification_required" => Text.T("추가 네트워크 확인이 필요합니다. 웹사이트에서 로그인해 확인하세요.", "Extra network verification is required. Sign in on the website to confirm."),
        "device_not_registered" => Text.T("이 기기가 등록되지 않았습니다. 웹사이트에서 등록하세요.", "This device is not registered. Register it on the website."),
        "build_limit_reached" => Text.T("이번 달 무료 빌드 한도에 도달했습니다.", "You reached this month's FREE build limit."),
        "unauthorized" => Text.T("다시 로그인하세요.", "Please sign in again."),
        _ => Text.T("요청을 처리하지 못했습니다.", "The request could not be completed.")
    };

    private async Task LoginAsync(System.Windows.Controls.PasswordBox? box)
    {
        AccountMessage = "";
        if (box is null || string.IsNullOrWhiteSpace(LoginEmail) || box.Password.Length == 0)
        {
            AccountMessage = Text.T("이메일과 비밀번호를 입력하세요.", "Enter your email and password.");
            return;
        }
        try
        {
            _me = await _account.LoginAsync(LoginEmail.Trim(), box.Password, RememberMe);
            box.Clear();
            await CheckDeviceAsync();
            NotifyAccount();
        }
        catch (DEX.App.Account.ApiException ex) { AccountMessage = AccountError(ex); }
    }

    private async Task RestoreAccountAsync()
    {
        try
        {
            _me = await _account.RestoreAsync();
            if (_me is not null) { await CheckDeviceAsync(); NotifyAccount(); }
            else Page = "account";
        }
        catch (DEX.App.Account.ApiException) { Page = "account"; }
    }

    private async Task LogoutAsync()
    {
        await _account.LogoutAsync();
        _me = null; _deviceRegistered = false; AccountMessage = "";
        NotifyAccount();
    }

    private async Task CheckDeviceAsync()
    {
        try { _deviceRegistered = await _account.IsDeviceRegisteredAsync(); }
        catch (DEX.App.Account.ApiException ex) { _deviceRegistered = false; AccountMessage = AccountError(ex); }
        Notify(nameof(DeviceStatus));
    }

    private void OpenSite(string? kind)
    {
        var url = kind == "device" ? _account.LinkDeviceUrl : _account.SiteUrl + "/account";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public RelayCommand NavigateCommand { get; }
    public RelayCommand BrowseFileCommand { get; }
    public RelayCommand BrowseFolderCommand { get; }
    public AsyncRelayCommand RecheckCommand { get; }
    public AsyncRelayCommand BuildCommand { get; }
    public RelayCommand OpenToolCommand { get; }
    public RelayCommand GuideToolCommand { get; }
    public RelayCommand OpenOutputCommand { get; }
    public RelayCommand RestartCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand ClearLogsCommand { get; }
    public RelayCommand CancelBuildCommand { get; }
    public RelayCommand UseEntryCommand { get; }
    public AsyncRelayCommand BuildAgainCommand { get; }

    public ObservableCollection<ToolRow> Tools { get; } = new();
    public ObservableCollection<string> Recent { get; } = new();
    public ObservableCollection<HistoryRow> History { get; } = new();
    public ObservableCollection<string> Entries { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<FormatRow> Formats { get; } = new();
    public ObservableCollection<string> Methods { get; } = new();
    public ObservableCollection<string> Architectures { get; } = new();

    public string Page { get => _page; set { _page = value; Notify(); Notify(nameof(Page)); } }
    private string _page = "home";
    public string Status { get => _status; set { _status = value; Notify(); } }
    private string _status = "";
    public string ProjectName { get => _projectName; set { _projectName = value; Notify(); } }
    private string _projectName = "";
    public string LanguageName { get => _languageName; set { _languageName = value; Notify(); } }
    private string _languageName = "";
    public string Framework { get => _framework; set { _framework = value; Notify(); } }
    private string _framework = "";
    public string Structure { get => _structure; set { _structure = value; Notify(); } }
    private string _structure = "";
    public string EntryPoint { get => _entry; set { _entry = value; Notify(); } }
    private string _entry = "";
    public string OutputType { get => _outputType; set { _outputType = value; Notify(); } }
    private string _outputType = "";
    public string Compatibility { get => _compat; set { _compat = value; Notify(); } }
    private string _compat = "";
    public string Problem { get => _problem; set { _problem = value; Notify(); } }
    private string _problem = "";
    public string Resolution { get => _resolution; set { _resolution = value; Notify(); } }
    private string _resolution = "";
    public string SelectedEntry { get => _selectedEntry; set { _selectedEntry = value; Notify(); RefreshReport(); } }
    private string _selectedEntry = "";
    public string SelectedMethod { get => _method; set { _method = value; Notify(); } }
    private string _method = "";
    public string SelectedArchitecture { get => _arch; set { _arch = value; Notify(); } }
    private string _arch = "x64";
    public string OutputName { get => _outputName; set { _outputName = value; Notify(); } }
    private string _outputName = "App";
    public string OutputDirectory { get => _outputDir; set { _outputDir = value; Notify(); } }
    private string _outputDir = "";
    public string ProductName { get => _product; set { _product = value; Notify(); } }
    private string _product = "";
    public string CompanyName { get => _company; set { _company = value; Notify(); } }
    private string _company = "";
    public string ProductVersion { get => _version; set { _version = value; Notify(); } }
    private string _version = "1.0.0";
    public string IconPath { get => _icon; set { _icon = value; Notify(); } }
    private string _icon = "";
    public bool ConsoleApp { get => _console; set { _console = value; Notify(); } }
    private bool _console = true;
    public bool SingleFile { get => _single; set { _single = value; Notify(); } }
    private bool _single = true;
    public bool SelfContained { get => _self; set { _self = value; Notify(); } }
    private bool _self = true;
    public bool IncludeDependencies { get => _deps; set { _deps = value; Notify(); } }
    private bool _deps = true;
    public bool Optimize { get => _opt; set { _opt = value; Notify(); } }
    private bool _opt = true;
    public bool Compression { get => _zip; set { _zip = value; Notify(); } }
    private bool _zip;
    public bool AllowIcon { get => _allowIcon; set { _allowIcon = value; Notify(); } }
    private bool _allowIcon;
    public bool AllowConsole { get => _allowConsole; set { _allowConsole = value; Notify(); } }
    private bool _allowConsole;
    public bool AllowSingle { get => _allowSingle; set { _allowSingle = value; Notify(); } }
    private bool _allowSingle;
    public bool AllowSelf { get => _allowSelf; set { _allowSelf = value; Notify(); } }
    private bool _allowSelf;
    public bool AllowDeps { get => _allowDeps; set { _allowDeps = value; Notify(); } }
    private bool _allowDeps;
    public bool AllowOptimize { get => _allowOpt; set { _allowOpt = value; Notify(); } }
    private bool _allowOpt;
    public bool AllowCompression { get => _allowZip; set { _allowZip = value; Notify(); } }
    private bool _allowZip;
    public string MethodExplanation { get => _methodNote; set { _methodNote = value; Notify(); } }
    private string _methodNote = "";
    public bool CanBuild { get => _canBuild; set { _canBuild = value; Notify(); BuildCommand.Raise(); } }
    private bool _canBuild;
    public bool Busy { get => _busy; set { _busy = value; Notify(); } }
    private bool _busy;

    public bool Ready { get => _ready; private set { _ready = value; Notify(); } }
    private bool _ready;
    public string Stage { get => _stage; set { _stage = value; Notify(); } }
    private string _stage = "";
    public int StageIndex { get => _stageIndex; set { _stageIndex = value; Notify(); } }
    private int _stageIndex;
    public bool Consent { get => _consent; set { _consent = value; Notify(); } }
    private bool _consent;
    public string ConsentText => Text.T(
        "업로드한 프로젝트의 빌드 스크립트는 임의 명령을 실행할 수 있습니다. COMPIVOX는 Windows Sandbox를 자동 실행하지 않고, 격리된 작업 복사본에서만 빌드합니다. 위험을 이해하고 진행합니다.",
                "Uploaded build scripts can run arbitrary commands. COMPIVOX does not launch Windows Sandbox. The build runs in an isolated working copy. I understand the risk.");
    public string SandboxNote => BuildManager.WindowsSandboxPresent
        ? Text.T("Windows Sandbox 기능은 감지되었지만 이 빌드에 자동 사용하지 않습니다.", "Windows Sandbox is installed, but it is not used automatically.")
        : Text.T("Windows Sandbox를 사용할 수 없습니다. 호스트의 격리된 작업 폴더에서 빌드합니다.", "Windows Sandbox is not available. The build uses an isolated working folder on this host.");
    public string LanguageCode
    {
        get => Text.Language;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || string.Equals(Text.Language, value, StringComparison.OrdinalIgnoreCase)) return;
            _settings.Language = value;
            Text.Language = value;
            Notify(string.Empty);
        }
    }
    public IReadOnlyList<LanguageOption> LanguageOptions { get; } = new[]
    {
        new LanguageOption("en", "English"),
        new LanguageOption("ko", "한국어")
    };
    public string LanguageLabel => Text.T("언어", "Language");
    public string OutputPathLabel => Text.T("기본 출력 경로", "Default output folder");
    public string ArchitectureLabel => Text.T("기본 아키텍처", "Default architecture");
    public string ThemeName => "Holographic";

    public string NavHome => Text.T("홈", "Home");
    public string NavConverter => Text.T("변환", "Converter");
    public string NavHistory => Text.T("빌드 기록", "Build History");
    public string NavFormats => Text.T("지원 형식", "Supported Formats");
    public string NavInstall => Text.T("설치 센터", "Installation Center");
    public string NavSettings => Text.T("설정", "Settings");
    public string NavAbout => Text.T("정보", "About COMPIVOX");

    public string DropTitle => Text.T("프로젝트를 놓으세요", "Drop a project");
    public string ThemeLabel => Text.T("흰색 기반 테마", "White theme");
    public string HomeIntro => Text.T("프로젝트, 폴더, ZIP을 올리면 분석부터 시작합니다. 확장자만 바꾸지 않습니다.", "Drop a project, folder, or ZIP to start with analysis. COMPIVOX does not just rename extensions.");
    public string DropFormats => Text.T("파일  ·  폴더  ·  ZIP", "File  ·  Folder  ·  ZIP");
    public string PickFileLabel => Text.T("파일 선택", "Choose file");
    public string PickFolderLabel => Text.T("폴더 선택", "Choose folder");
    public string RecheckLabel => Text.T("다시 검사", "Check again");
    public string InstallCenterLabel => Text.T("설치 센터", "Installation Center");
    public string RestartLabel => Text.T("다시 시작", "Restart");
    public string EntryLabel => Text.T("진입점", "Entry point");
    public string BuildMethodLabel => Text.T("빌드 방식", "Build method");
    public string RebuildLabel => Text.T("다시 빌드", "Rebuild");
    public string FormatsIntro => Text.T("업로드할 수 있다는 이유만으로 지원한다고 표시하지 않습니다.", "Uploading a file does not mean it is supported.");
    public string InstallIntro => Text.T("공식 HTTPS 페이지만 엽니다. COMPIVOX는 설치 파일을 받거나 실행하지 않습니다.", "Only official HTTPS pages are opened. COMPIVOX does not download or run installers.");
    public string OfficialSiteLabel => Text.T("공식 사이트", "Official site");
    public string InstallGuideLabel => Text.T("설치 안내", "Install guide");
    public string SaveLabel => Text.T("저장", "Save");
    public string ClearLogsLabel => Text.T("로그 삭제", "Clear logs");
    public string AboutLine1 => Text.T("프로젝트 분석, 공식 도구 안내, 재검사, 실제 컴파일과 EXE 패키징을 잇는 Windows 스튜디오입니다.", "A Windows studio that connects project analysis, official tool guidance, re-checks, real compilation, and EXE packaging.");
    public string AboutLine2 => Text.T("설치 파일을 받지 않습니다. 조건이 없으면 Build EXE는 켜지지 않습니다. 지원하지 않는 파일은 지원한다고 말하지 않습니다.", "COMPIVOX does not download installers. Build EXE stays disabled until requirements are met. Unsupported files are never labeled as supported.");

    public async Task OpenPathAsync(string path)
    {
        Busy = true;
        Status = Text.T("프로젝트를 분석하는 중입니다.", "Analyzing the project.");
        Page = "converter";
        try
        {
            var opened = await _analysis.OpenAsync(path);
            Apply(opened.Intake, opened.Analysis, opened.Tools, opened.Report, opened.Options);
            Remember(path);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            CanBuild = false;
        }
        finally { Busy = false; }
    }

    private void Apply(IntakeResult intake, ProjectAnalysis analysis, IReadOnlyList<ToolStatus> tools, CompatibilityReport report, OptionAvailability options)
    {
        _current = analysis;
        _toolMap = tools.ToDictionary(t => t.ToolId, StringComparer.OrdinalIgnoreCase);
        ProjectName = analysis.ProjectName;
        LanguageName = analysis.LanguageName;
        Framework = analysis.Framework ?? Text.T("없음", "None");
        Structure = analysis.StructureSummary;
        EntryPoint = analysis.EntryPoint ?? Text.T("지정 필요", "Selection required");
        OutputType = analysis.OutputType;
        OutputName = SecurityManager.SanitizeFileName(analysis.ProjectName);
        OutputDirectory = _settings.DefaultOutputDirectory;
        ProductName = analysis.ProjectName;
        SelectedArchitecture = Text.Architecture(_settings.DefaultArchitecture);
        Entries.Clear();
        foreach (var entry in analysis.EntryCandidates)
            Entries.Add(entry.RelativePath);
        SelectedEntry = analysis.EntryPoint ?? "";
        Methods.Clear();
        foreach (var method in options.Methods)
            Methods.Add(Text.Method(method));
        SelectedMethod = Methods.FirstOrDefault() ?? "";
        MethodExplanation = DEX.Core.Compatibility.BuildConfigurationManager.Explain(analysis.RecommendedMethod);
        AllowIcon = options.Icon;
        AllowConsole = options.Console;
        AllowSingle = options.SingleFile;
        AllowSelf = options.SelfContained;
        AllowDeps = options.IncludeDependencies;
        AllowOptimize = options.Optimize;
        AllowCompression = options.Compression;
        Architectures.Clear();
        foreach (var arch in options.Architectures)
            Architectures.Add(Text.Architecture(arch));
        FillTools(tools, analysis);
        ApplyReport(report);
        Status = report.Summary;
    }

    private void ApplyReport(CompatibilityReport report)
    {
        Compatibility = report.Summary;
        Problem = report.Problem;
        Resolution = report.Resolution;
        CanBuild = report.BuildAllowed;
        if (!report.BuildAllowed)
            Consent = false;
    }

    private void FillTools(IReadOnlyList<ToolStatus> tools, ProjectAnalysis? analysis)
    {
        Tools.Clear();
        var required = analysis?.Requirements.Select(r => r.ToolId).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        foreach (var tool in OfficialToolCatalog.All)
        {
            var status = tools.FirstOrDefault(t => t.ToolId == tool.Id);
            var requirement = analysis?.Requirements.FirstOrDefault(r => r.ToolId == tool.Id);
            Tools.Add(new ToolRow
            {
                Id = tool.Id,
                Name = tool.Name,
                Vendor = tool.Vendor,
                Reason = requirement?.Reason ?? (Text.Language == "en" ? tool.DescriptionEn : tool.DescriptionKo),
                RequiredVersion = requirement?.RequiredVersion ?? "",
                Version = status?.Version ?? "",
                State = status is null ? Text.Install(InstallState.Checking) : Text.Install(status.State),
                Needed = required.Contains(tool.Id)
            });
        }
    }

    private async Task RecheckAsync()
    {
        if (_current is null)
            return;
        Busy = true;
        var before = Tools.Select(t => t.Id + t.State).ToArray();
        var tools = await _analysis.RefreshAsync(_current, CancellationToken.None);
        _toolMap = tools.ToDictionary(t => t.ToolId, StringComparer.OrdinalIgnoreCase);
        FillTools(tools, _current);
        var after = Tools.Select(t => t.Id + t.State).ToArray();
        RefreshReport();
        Status = before.SequenceEqual(after)
            ? Text.T("재검사 결과가 바뀌지 않았습니다. 설치 직후라면 COMPIVOX를 다시 시작하세요.", "Recheck did not change. If you just installed a tool, restart COMPIVOX.")
            : Text.T("재검사 결과가 반영되었습니다.", "Recheck results were applied.");
        Busy = false;
    }

    private void RefreshReport()
    {
        if (_current is null)
            return;
        var report = _analysis.Recheck(_current, _toolMap.Values.ToList(), string.IsNullOrWhiteSpace(SelectedEntry) ? null : SelectedEntry);
        ApplyReport(report);
    }

    private async Task BuildAsync()
    {
        if (_current is null || !CanBuild)
            return;
        if (!Consent)
        {
            Status = Text.T("빌드 전 위험 안내에 동의해야 합니다.", "Consent to the build risk notice is required.");
            return;
        }
        if (!SecurityManager.IsVersionToken(ProductVersion))
        {
            Status = Text.T("버전은 1.0.0 형식이어야 합니다.", "Version must look like 1.0.0.");
            return;
        }
        try { await _account.AuthorizeBuildAsync(System.IO.Path.GetFileName(_current.RootPath.TrimEnd('\\', '/'))); }
        catch (DEX.App.Account.ApiException ex)
        {
            Status = AccountError(ex);
            if (ex.Code is "unauthorized" or "device_not_registered") Page = "account";
            return;
        }
        _buildCts = new CancellationTokenSource();
        Busy = true;
        LogLines.Clear();
        var progress = new Progress<BuildProgress>(p =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Stage = Text.Stage(p.Stage);
                StageIndex = Math.Clamp((int)p.Stage, 0, 7);
                if (!string.IsNullOrWhiteSpace(p.LogLine))
                    LogLines.Add(p.LogLine);
                if (LogLines.Count > 400)
                    LogLines.RemoveAt(0);
            });
        });
        var options = new BuildOptions
        {
            OutputName = OutputName,
            OutputDirectory = OutputDirectory,
            IconPath = string.IsNullOrWhiteSpace(IconPath) ? null : IconPath,
            ProductName = ProductName,
            CompanyName = CompanyName,
            ProductVersion = ProductVersion,
            Architecture = ParseArch(SelectedArchitecture),
            Console = ConsoleApp,
            SingleFile = SingleFile,
            SelfContained = SelfContained,
            IncludeDependencies = IncludeDependencies,
            Optimize = Optimize,
            Compression = Compression,
            Method = ParseMethod(SelectedMethod),
            SelectedEntry = string.IsNullOrWhiteSpace(SelectedEntry) ? null : SelectedEntry
        };
        var result = await _build.BuildAsync(new BuildRequest
        {
            ProjectRoot = _current.RootPath,
            Analysis = _current,
            Options = options,
            Tools = _toolMap,
            ConsentGranted = true
        }, progress, _buildCts.Token);
        LastOutput = result.ExecutablePath;
        Status = result.Success
            ? Text.T("EXE를 검증하고 저장했습니다: ", "EXE verified and saved: ") + result.ExecutablePath
            : Text.T("실패 단계: ", "Failed stage: ") + result.FailureStage + " — " + result.Error;
        if (result.Success)
            Stage = Text.Stage(BuildStage.Completed);
        LoadHistory();
        Busy = false;
    }

    private async Task LoadHomeAsync()
    {
        OutputDirectory = _settings.DefaultOutputDirectory;
        foreach (var path in _settings.RecentProjects)
            Recent.Add(path);
        foreach (var format in SupportCatalog.All)
            Formats.Add(new FormatRow(format.Language, format.Extensions, Text.Support(format.Level), format.BuildMethod, format.RequiredTools, format.Caveat, string.Join(" ", format.DownloadUrls)));
        var tools = await _environment.CheckAllAsync();
        FillTools(tools, null);
        LoadHistory();
        Status = Text.T("공식 설치 상태만 표시합니다.", "Showing real installation status only.");
    }

    private void LoadHistory()
    {
        History.Clear();
        foreach (var item in _history.Load())
            History.Add(new HistoryRow(item));
    }

    private void BrowseFile()
    {
        var dialog = new OpenFileDialog { Filter = "Project or archive|*.zip;*.py;*.csproj;*.sln;*.html;*.java;*.go;*.rs;*.php;*.rb;*.ps1;*.bat|All files|*.*" };
        if (dialog.ShowDialog() == true)
            _ = OpenPathAsync(dialog.FileName);
    }

    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true)
            _ = OpenPathAsync(dialog.FolderName);
    }

    private void OpenTool(string? id, bool guide)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;
        try
        {
            if (guide) _downloads.OpenGuide(id);
            else _downloads.OpenDownload(id);
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    private void OpenOutput()
    {
        if (string.IsNullOrWhiteSpace(LastOutput))
            return;
        try { _downloads.OpenFolder(LastOutput); }
        catch (Exception ex) { Status = ex.Message; }
    }

    private async Task BuildAgainAsync(string? id)
    {
        var row = History.FirstOrDefault(h => h.Id == id);
        if (row?.ProjectPath is null || !Directory.Exists(row.ProjectPath) && !File.Exists(row.ProjectPath))
        {
            Status = Text.T("원래 프로젝트 경로가 없습니다.", "The original project path is gone.");
            return;
        }
        await OpenPathAsync(row.ProjectPath);
    }

    private void SaveSettings()
    {
        _settings.DefaultOutputDirectory = OutputDirectory;
        _settings.DefaultArchitecture = ParseArch(SelectedArchitecture);
        _settings.Language = LanguageCode;
        _settings.Theme = "Holographic";
        _settingsService.Save(_settings);
        Status = Text.T("설정을 저장했습니다.", "Settings saved.");
        Notify(string.Empty);
    }

    private void Remember(string path)
    {
        _settings.RecentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _settings.RecentProjects.Insert(0, path);
        if (_settings.RecentProjects.Count > 8)
            _settings.RecentProjects.RemoveRange(8, _settings.RecentProjects.Count - 8);
        _settingsService.Save(_settings);
        Recent.Clear();
        foreach (var item in _settings.RecentProjects)
            Recent.Add(item);
    }

    private static void Restart()
    {
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    private static ArchitectureKind ParseArch(string value) => value switch
    {
        "x86" => ArchitectureKind.X86,
        "ARM64" => ArchitectureKind.Arm64,
        _ => ArchitectureKind.X64
    };

    private static BuildMethod ParseMethod(string value)
    {
        foreach (BuildMethod method in Enum.GetValues<BuildMethod>())
        {
            if (Text.Method(method) == value)
                return method;
        }
        return BuildMethod.None;
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private ProjectAnalysis? _current;
    private Dictionary<string, ToolStatus> _toolMap = new(StringComparer.OrdinalIgnoreCase);
    public string? LastOutput { get; private set; }
}

public sealed class ToolRow
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Vendor { get; init; } = "";
    public string Reason { get; init; } = "";
    public string RequiredVersion { get; init; } = "";
    public string Version { get; init; } = "";
    public string State { get; init; } = "";
    public bool Needed { get; init; }
}

public sealed class HistoryRow
{
    public HistoryRow(HistoryEntry entry)
    {
        Id = entry.Id;
        Title = entry.ProjectName + " · " + entry.Language;
        Detail = entry.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + " · " + (entry.Success ? "OK" : "FAIL") + " · " + entry.BuildTool;
        OutputPath = entry.OutputPath;
        ProjectPath = entry.ProjectPath;
        LogPath = entry.LogPath;
        Error = entry.Error;
    }
    public string Id { get; }
    public string Title { get; }
    public string Detail { get; }
    public string? OutputPath { get; }
    public string? ProjectPath { get; }
    public string? LogPath { get; }
    public string? Error { get; }
}

public sealed record FormatRow(string Language, string Extensions, string Level, string Method, string Tools, string Caveat, string Links);
