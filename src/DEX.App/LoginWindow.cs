using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using DEX.App.Account;

namespace DEX.App;

/// <summary>
/// 시작 시 로그인되어 있지 않을 때 표시되는 창. 웹사이트에서 로그인하면 127.0.0.1 루프백으로 일회용 코드를 받아 세션을 얻는다.
/// </summary>
public partial class LoginWindow : Window
{
    private readonly AccountClient _account;
    private CancellationTokenSource? _cts;
    private Storyboard? _auraStoryboard;

    public LoginWindow(AccountClient account)
    {
        _account = account;
        InitializeComponent();
        try
        {
            Emblem.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/app-icon.png"));
        }
        catch (IOException)
        {
            // 아이콘이 없어도 로그인 기능에는 영향이 없다.
        }
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation)
            return;

        var storyboard = new Storyboard();
        AddDrift("LoginAuraWarmX", "X", -110, 150, 58);
        AddDrift("LoginAuraWarmY", "Y", -70, 90, 72);
        AddDrift("LoginAuraSoftX", "X", 120, -160, 76);
        AddDrift("LoginAuraSoftY", "Y", 60, -85, 84);
        AddDrift("LoginAuraWarmScale", "ScaleX", 0.94, 1.06, 30);
        AddDrift("LoginAuraWarmScale", "ScaleY", 0.94, 1.06, 30);
        AddDrift("LoginAuraSoftScale", "ScaleX", 1.04, 0.96, 38);
        AddDrift("LoginAuraSoftScale", "ScaleY", 1.04, 0.96, 38);
        storyboard.Begin(this, true);
        _auraStoryboard = storyboard;

        void AddDrift(string targetName, string property, double from, double to, double seconds)
        {
            var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTargetName(animation, targetName);
            Storyboard.SetTargetProperty(animation, new PropertyPath(property));
            storyboard.Children.Add(animation);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _auraStoryboard?.Stop(this);
        _cts?.Cancel();
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void OnWebLogin(object sender, RoutedEventArgs e)
    {
        WebLoginButton.IsEnabled = false;
        _cts?.Cancel();
        _cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var token = _cts.Token;
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

            SetStatus("브라우저에서 로그인을 완료해 주세요…");
            Process.Start(new ProcessStartInfo($"{_account.SiteUrl}/account?desktop={port}&state={state}") { UseShellExecute = true });

            var code = await WaitForCodeAsync(listener, state, token);
            SetStatus("로그인 확인 중…");
            await _account.ExchangeCodeAsync(code, token);
            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            SetStatus("시간이 초과되었거나 취소되었습니다. 다시 시도해 주세요.");
        }
        catch (Exception ex)
        {
            SetStatus("로그인에 실패했습니다: " + ex.Message);
        }
        finally
        {
            listener?.Stop();
            WebLoginButton.IsEnabled = true;
        }
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
    }

    /// <summary>루프백 포트에서 /callback?code&amp;state 요청을 기다린다. 다른 요청은 404로 응답한다.</summary>
    private static async Task<string> WaitForCodeAsync(TcpListener listener, string state, CancellationToken ct)
    {
        using var stop = ct.Register(listener.Stop);
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            var requestLine = await reader.ReadLineAsync(ct) ?? "";
            string? line;
            do { line = await reader.ReadLineAsync(ct); } while (!string.IsNullOrEmpty(line));

            var parts = requestLine.Split(' ');
            var target = parts.Length > 1 ? parts[1] : "/";
            var path = target.Split('?')[0];
            var query = HttpUtility.ParseQueryString(target.Contains('?') ? target[(target.IndexOf('?') + 1)..] : "");

            if (path == "/callback" && query["state"] == state && !string.IsNullOrEmpty(query["code"]))
            {
                await WriteResponseAsync(stream, 200, "<h2>COMPIVOX 로그인 완료</h2><p>이 창을 닫고 COMPIVOX 앱으로 돌아가세요.</p>");
                return query["code"]!;
            }
            await WriteResponseAsync(stream, 404, "<p>Not found</p>");
        }
    }

    private static async Task WriteResponseAsync(Stream stream, int status, string body)
    {
        var html = "<!doctype html><meta charset=\"utf-8\"><title>COMPIVOX</title>" +
                   "<body style=\"background:#08090d;color:#e5c98a;font-family:system-ui,sans-serif;text-align:center;padding:60px\">" + body + "</body>";
        var payload = Encoding.UTF8.GetBytes(html);
        var reason = status == 200 ? "OK" : "Not Found";
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head);
        await stream.WriteAsync(payload);
        await stream.FlushAsync();
    }
}
