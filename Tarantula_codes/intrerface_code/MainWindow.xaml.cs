using System;
using System.IO;
using System.IO.Ports;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;

namespace TarantulaControl
{
    public partial class MainWindow : Window
    {
        private SerialPort _port;
        private bool _isConnected = false;
        private readonly StringBuilder _rxBuffer = new();

        private const byte CMD_FORWARD = (byte)'F';
        private const byte CMD_BACK = (byte)'B';
        private const byte CMD_LEFT = (byte)'L';
        private const byte CMD_RIGHT = (byte)'R';
        private const byte CMD_STOP = (byte)'S';
        private const byte CMD_START = (byte)'G';
        private const byte CMD_ABORT = (byte)'A';

        private bool _isAutonomMode = false;

        private double _targetLat = 0, _targetLon = 0;
        private bool _hasTarget = false;

        private double _masterLat = 0, _masterLon = 0;
        private double _masterYaw = 0;
        private bool _masterFix = false;
        private int _masterSysCal = 0;

        private double _slaveLat = 0, _slaveLon = 0;
        private double _slaveYaw = 0;
        private bool _slaveFix = false;

        private enum SlaveState { IDLE, NAVIGATING, ARRIVED }
        private SlaveState _slaveState = SlaveState.IDLE;

        private double _mYawW = 0, _mPitW = 0, _mRolW = 0;
        private double _sYawW = 0, _sPitW = 0, _sRolW = 0;

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        // Ultra hızlı akış için durum sınıfı
        private class ImageStreamState
        {
            public CancellationTokenSource Cts;
            public bool IsStreaming;
            public int FpsCount;
            public DateTime FpsLast = DateTime.Now;
        }

        private readonly ImageStreamState _rpiStream = new ImageStreamState();

        private TcpClient _unityClient;
        private NetworkStream _unityStream;
        private bool _isUnityConnected = false;

        private readonly StringBuilder _logBuf = new();

        private DispatcherTimer _clockTimer;
        private DispatcherTimer _navTimer;
        private DispatcherTimer _animTimer;
        private int _animPhase = 0;

        private static readonly System.Globalization.NumberFormatInfo Inv =
            System.Globalization.CultureInfo.InvariantCulture.NumberFormat;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshPortList();

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, ev) => ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
            _clockTimer.Start();

            _navTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _navTimer.Tick += NavTimer_Tick;
            _navTimer.Start();

            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _animTimer.Tick += AnimTimer_Tick;
            _animTimer.Start();

            KeyDown += MainWindow_KeyDown;
            SizeChanged += (s, ev) => RecalcBars();

            TxtCamUrl.Text = "http://192.168.1.100:8080/?action=stream";
            TxtUnityCamUrl.Text = "http://127.0.0.1:8080/stream";
            TxtUnityHost.Text = "127.0.0.1:7777";

            AddLog("SYS", "Tarantula Control Panel başlatıldı.", "#484F58");
            AddLog("SYS", "Raspberry kamera ve Unity görüntüsü ayrı panellerde çalışır.", "#484F58");
            AddLog("SYS", "Unity TCP için IP:7777 yaz ve BAĞLAN butonuna bas.", "#484F58");
        }

        private void RefreshPortList()
        {
            string sel = ComboPorts.SelectedItem as string;
            ComboPorts.Items.Clear();

            foreach (string p in SerialPort.GetPortNames())
                ComboPorts.Items.Add(p);

            if (sel != null && ComboPorts.Items.Contains(sel))
                ComboPorts.SelectedItem = sel;
            else if (ComboPorts.Items.Count > 0)
                ComboPorts.SelectedIndex = 0;
        }

        private void ComboPorts_DropDownOpened(object sender, EventArgs e)
        {
            RefreshPortList();
        }

        private void RecalcBars()
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (MasterYawBar.Parent is Border b1) _mYawW = b1.ActualWidth > 2 ? b1.ActualWidth - 2 : 150;
                if (MasterPitchBar.Parent is Border b2) _mPitW = b2.ActualWidth > 2 ? b2.ActualWidth - 2 : 150;
                if (MasterRollBar.Parent is Border b3) _mRolW = b3.ActualWidth > 2 ? b3.ActualWidth - 2 : 150;
                if (SlaveYawBar.Parent is Border b4) _sYawW = b4.ActualWidth > 2 ? b4.ActualWidth - 2 : 150;
                if (SlavePitchBar.Parent is Border b5) _sPitW = b5.ActualWidth > 2 ? b5.ActualWidth - 2 : 150;
                if (SlaveRollBar.Parent is Border b6) _sRolW = b6.ActualWidth > 2 ? b6.ActualWidth - 2 : 150;
            }, DispatcherPriority.Background);
        }

        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (ComboPorts.SelectedItem == null)
            {
                AddLog("ERR", "COM port seçilmedi.", "#FF4040");
                return;
            }

            string port = ComboPorts.SelectedItem.ToString();
            int baud = int.Parse(((ComboBoxItem)ComboBaud.SelectedItem).Content.ToString());

            try
            {
                _port = new SerialPort(port, baud, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 500,
                    WriteTimeout = 500,
                    Encoding = Encoding.ASCII
                };

                _port.DataReceived += Port_DataReceived;
                _port.Open();

                _isConnected = true;
                SetConnectedUI(true);
                RecalcBars();

                AddLog("CON", $"Bağlandı: {port} @ {baud} baud.", "#3FB950");
            }
            catch (Exception ex)
            {
                AddLog("ERR", $"Bağlantı hatası: {ex.Message}", "#FF4040");
            }
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            if (_port?.IsOpen == true)
            {
                _port.DataReceived -= Port_DataReceived;
                _port.Close();
                _port.Dispose();
            }

            _isConnected = false;
            SetConnectedUI(false);

            AddLog("CON", "Bağlantı kesildi.", "#FF4040");
        }

        private void SetConnectedUI(bool c)
        {
            BtnConnect.IsEnabled = !c;
            BtnDisconnect.IsEnabled = c;

            bool manualButtonsEnabled = (c || _isUnityConnected) && !_isAutonomMode;

            BtnForward.IsEnabled = manualButtonsEnabled;
            BtnBack.IsEnabled = manualButtonsEnabled;
            BtnLeft.IsEnabled = manualButtonsEnabled;
            BtnRight.IsEnabled = manualButtonsEnabled;
            BtnStop.IsEnabled = c || _isUnityConnected;

            StatusDot.Text = c ? "● BAĞLANDI" : "● BAĞLI DEĞİL";
            StatusDot.Foreground = c
                ? new SolidColorBrush(Color.FromRgb(63, 185, 80))
                : new SolidColorBrush(Color.FromRgb(72, 79, 88));

            CoordStatusText.Text = c ? "Hazır. Manuel mod aktif." : "Bağlantı bekleniyor...";
        }

        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string raw = _port.ReadExisting();
                _rxBuffer.Append(raw);

                string buf = _rxBuffer.ToString();
                int idx;

                while ((idx = buf.IndexOf('\n')) >= 0)
                {
                    string line = buf.Substring(0, idx).Trim();
                    buf = buf.Substring(idx + 1);

                    if (!string.IsNullOrEmpty(line))
                        Dispatcher.Invoke(() => ParseLine(line));
                }

                _rxBuffer.Clear();
                _rxBuffer.Append(buf);
            }
            catch
            {
            }
        }

        private void ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            line = line.Trim();

            if (line.StartsWith("SERVO:", StringComparison.OrdinalIgnoreCase))
            {
                LastRxText.Text = line.Length > 45 ? line.Substring(0, 45) + "…" : line;
                SendToUnity(line);
                AddLog("SRV", "Servo angles forwarded to Unity", "#58D6FF");
                return;
            }

            int startIndex = line.IndexOf('$');

            if (startIndex > 0)
                line = line.Substring(startIndex);
            else if (startIndex == -1)
                return;

            LastRxText.Text = line.Length > 45 ? line.Substring(0, 45) + "…" : line;

            if (line.StartsWith("$M:GPS,"))
            {
                var p = line.Substring(7).Split(',');

                if (p.Length >= 5)
                {
                    double.TryParse(p[0], Inv, out _masterLat);
                    double.TryParse(p[1], Inv, out _masterLon);

                    MasterLat.Text = $"{_masterLat:F6}";
                    MasterLon.Text = $"{_masterLon:F6}";
                    MasterAlt.Text = p[2] + " m";

                    _masterFix = p[4].Trim() == "1";
                    SetFixUI(MasterFixDot, MasterFixText, _masterFix);
                }
            }
            else if (line.StartsWith("$M:BNO,"))
            {
                var p = line.Substring(7).Split(',');

                if (p.Length >= 7)
                {
                    double.TryParse(p[0], Inv, out _masterYaw);
                    double.TryParse(p[1], Inv, out double pit);
                    double.TryParse(p[2], Inv, out double rol);
                    int.TryParse(p[3], out _masterSysCal);

                    UpdateBnoUI(
                        MasterYawBar, MasterYawText, _mYawW, _masterYaw,
                        MasterPitchBar, MasterPitchText, _mPitW, pit,
                        MasterRollBar, MasterRollText, _mRolW, rol
                    );

                    MasterCalText.Text = $"CAL: SYS:{p[3]} ACC:{p[4]} GYR:{p[5]} MAG:{p[6].Trim()}";
                    MasterCalText.Foreground = _masterSysCal < 1
                        ? new SolidColorBrush(Color.FromRgb(255, 64, 64))
                        : new SolidColorBrush(Color.FromRgb(48, 54, 61));

                    SendToUnity($"M:{_masterYaw:F1},{pit:F1},{rol:F1}");
                }
            }
            else if (line.StartsWith("$S:GPS,"))
            {
                var p = line.Substring(7).Split(',');

                if (p.Length >= 5)
                {
                    double.TryParse(p[0], Inv, out _slaveLat);
                    double.TryParse(p[1], Inv, out _slaveLon);

                    SlaveLat.Text = $"{_slaveLat:F6}";
                    SlaveLon.Text = $"{_slaveLon:F6}";
                    SlaveAlt.Text = p[2] + " m";

                    _slaveFix = p[4].Trim() == "1";
                    SetFixUI(SlaveFixDot, SlaveFixText, _slaveFix);

                    if (_hasTarget)
                    {
                        double d = Haversine(_slaveLat, _slaveLon, _targetLat, _targetLon);
                        SlaveDistText.Text = $"{d:F1} m";
                        SlaveTargetLat.Text = $"{_targetLat:F6}";
                        SlaveTargetLon.Text = $"{_targetLon:F6}";
                    }
                }
            }
            else if (line.StartsWith("$S:BNO,"))
            {
                var p = line.Substring(7).Split(',');

                if (p.Length >= 7)
                {
                    double.TryParse(p[0], Inv, out _slaveYaw);
                    double.TryParse(p[1], Inv, out double pit);
                    double.TryParse(p[2], Inv, out double rol);

                    UpdateBnoUI(
                        SlaveYawBar, SlaveYawText, _sYawW, _slaveYaw,
                        SlavePitchBar, SlavePitchText, _sPitW, pit,
                        SlaveRollBar, SlaveRollText, _sRolW, rol
                    );

                    SlaveCalText.Text = $"CAL: SYS:{p[3]} ACC:{p[4]} GYR:{p[5]} MAG:{p[6].Trim()}";
                    SendToUnity($"S:{_slaveYaw:F1},{pit:F1},{rol:F1}");
                }
            }
            else if (line.StartsWith("$QR:"))
            {
                var p = line.Substring(4).Split(',');

                if (p.Length >= 2 &&
                    double.TryParse(p[0], Inv, out double lat) &&
                    double.TryParse(p[1].Trim(), Inv, out double lon))
                {
                    _targetLat = lat;
                    _targetLon = lon;
                    _hasTarget = true;

                    TargetLatText.Text = $"{lat:F6}";
                    TargetLonText.Text = $"{lon:F6}";
                    TargetGpsText.Text = $"{lat:F4}, {lon:F4}";

                    QrStatusText.Text = "EVET";
                    QrStatusText.Foreground = new SolidColorBrush(Color.FromRgb(63, 185, 80));

                    QrSourceText.Text = "RPi → UART → STM32";
                    BtnSendTarget.IsEnabled = true;

                    AddLog("QR", $"Hedef alındı: {lat:F6}, {lon:F6}", "#F0AD4E");
                    CoordStatusText.Text = "QR hedef alındı. Master → Slave otomatik iletildi.";

                    SendToUnity($"T:{lat:F6},{lon:F6}");
                }
            }
            else if (line.StartsWith("$M:WARN,"))
            {
                AddLog("WRN", $"Master: {line.Substring(8)}", "#FF4040");
                CoordStatusText.Text = $"⚠ {line.Substring(8)}";
            }
            else if (line == "$M:ARR")
            {
                AddLog("NAV", "Master hedefe ULAŞTI!", "#3FB950");
                CoordStatusText.Text = "✓ Master hedefe ulaştı!";
            }
            else if (line == "$S:NAV")
            {
                _slaveState = SlaveState.NAVIGATING;
                UpdateSlaveStateUI();
                AddLog("SLV", "Slave navigate ediyor.", "#A78BFA");
            }
            else if (line == "$S:ARR")
            {
                _slaveState = SlaveState.ARRIVED;
                UpdateSlaveStateUI();
                AddLog("SLV", "Slave hedefe ULAŞTI!", "#3FB950");
                CoordStatusText.Text = "✓ Görev tamamlandı. Her iki robot hedefe ulaştı.";
            }
        }

        private void NavTimer_Tick(object sender, EventArgs e)
        {
            if (!_hasTarget || !_masterFix)
                return;

            double dist = Haversine(_masterLat, _masterLon, _targetLat, _targetLon);
            double bearing = Bearing(_masterLat, _masterLon, _targetLat, _targetLon);

            TargetDistText.Text = $"{dist:F1} m";
            BearingText.Text = $"{bearing:F1}°";
        }

        private void AnimTimer_Tick(object sender, EventArgs e)
        {
            if (!_isConnected && !_isUnityConnected)
                return;

            _animPhase = (_animPhase + 1) % 4;
        }

        private void BtnManuelMod_Click(object sender, RoutedEventArgs e)
        {
            _isAutonomMode = false;

            ModeText.Text = "MANUEL MOD";
            ModeBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(35, 134, 54));
            ModeText.Foreground = new SolidColorBrush(Color.FromRgb(63, 185, 80));

            bool manualButtonsEnabled = _isConnected || _isUnityConnected;

            BtnForward.IsEnabled = manualButtonsEnabled;
            BtnBack.IsEnabled = manualButtonsEnabled;
            BtnLeft.IsEnabled = manualButtonsEnabled;
            BtnRight.IsEnabled = manualButtonsEnabled;
            BtnStop.IsEnabled = manualButtonsEnabled;

            AddLog("MOD", "Manuel mod aktif.", "#3FB950");
        }

        private void BtnAutonomMod_Click(object sender, RoutedEventArgs e)
        {
            if (!_hasTarget)
            {
                AddLog("ERR", "QR hedef gerekli!", "#FF4040");
                return;
            }

            /*if (!_masterFix)
            {
                AddLog("ERR", "Master GPS fix gerekli!", "#FF4040");
                return;
            }*/

            if (_masterSysCal < 1)
                AddLog("WRN", $"BNO055 kalibrasyonu düşük (SYS={_masterSysCal})", "#F0AD4E");

            _isAutonomMode = true;

            ModeText.Text = "OTONOM MOD";
            ModeBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(125, 78, 0));
            ModeText.Foreground = new SolidColorBrush(Color.FromRgb(240, 173, 78));

            BtnForward.IsEnabled = false;
            BtnBack.IsEnabled = false;
            BtnLeft.IsEnabled = false;
            BtnRight.IsEnabled = false;

            SendCmd(CMD_START);
            SendToUnity("G");

            AddLog("MOD", "Otonom mod aktif. Master hedefe gidiyor.", "#F0AD4E");
        }

        private void BtnSendTarget_Click(object sender, RoutedEventArgs e)
        {
            if (!_hasTarget || !_isConnected)
                return;

            string msg = $"$T:{_targetLat:F6},{_targetLon:F6}\n";

            try
            {
                _port.Write(Encoding.ASCII.GetBytes(msg), 0, msg.Length);
                LastTxText.Text = "$T:override";
                AddLog("CRD", $"[OVERRIDE] Slave'e hedef gönderildi: {_targetLat:F6}, {_targetLon:F6}", "#A78BFA");
            }
            catch (Exception ex)
            {
                AddLog("ERR", ex.Message, "#FF4040");
            }
        }

        private void BtnCamConnect_Click(object sender, RoutedEventArgs e)
        {
            StartImageStream(
                TxtCamUrl.Text.Trim(),
                CamImage,
                CamPlaceholder,
                BtnCamConnect,
                BtnCamStop,
                FpsText,
                _rpiStream,
                "RPI"
            );
        }

        private void BtnCamStop_Click(object sender, RoutedEventArgs e)
        {
            StopImageStream(
                CamImage,
                CamPlaceholder,
                BtnCamConnect,
                BtnCamStop,
                FpsText,
                _rpiStream,
                "RPI"
            );
        }

        private async void BtnUnityCamConnect_Click(object sender, RoutedEventArgs e)
        {
            string url = TxtUnityCamUrl.Text.Trim();

            if (string.IsNullOrEmpty(url))
            {
                AddLog("ERR", "UCAM URL boş.", "#FF4040");
                return;
            }

            try
            {
                await UnityLiveView.EnsureCoreWebView2Async();

                UnityLiveView.Visibility = Visibility.Visible;
                UnityCamPlaceholder.Visibility = Visibility.Collapsed;

                UnityLiveView.CoreWebView2.Navigate(url);

                BtnUnityCamConnect.IsEnabled = false;
                BtnUnityCamStop.IsEnabled = true;

                UnityCamFpsText.Text = "LIVE";

                AddLog("UCAM", $"Unity canlı görüntü açıldı: {url}", "#F0AD4E");
            }
            catch (Exception ex)
            {
                AddLog("ERR", "Unity görüntüsü açılamadı: " + ex.Message, "#FF4040");
            }
        }

        private void BtnUnityCamStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (UnityLiveView.CoreWebView2 != null)
                    UnityLiveView.CoreWebView2.Navigate("about:blank");

                UnityLiveView.Visibility = Visibility.Collapsed;
                UnityCamPlaceholder.Visibility = Visibility.Visible;
            }
            catch
            {
            }

            UnityCamFpsText.Text = "— FPS";

            BtnUnityCamConnect.IsEnabled = true;
            BtnUnityCamStop.IsEnabled = false;

            AddLog("UCAM", "Unity canlı görüntü durduruldu.", "#484F58");
        }

        // ULTRA PERFORMANSLI YENİ AKIŞ MOTORU (Limitsiz FPS)
        private void StartImageStream(
            string url,
            Image targetImage,
            FrameworkElement placeholder,
            Button startButton,
            Button stopButton,
            TextBlock fpsText,
            ImageStreamState state,
            string tag)
        {
            if (string.IsNullOrEmpty(url))
            {
                AddLog("ERR", $"{tag} URL boş.", "#FF4040");
                return;
            }

            state.IsStreaming = true;
            state.FpsCount = 0;
            state.FpsLast = DateTime.Now;
            state.Cts = new CancellationTokenSource();

            targetImage.Source = null;
            targetImage.Visibility = Visibility.Visible;
            placeholder.Visibility = Visibility.Collapsed;

            startButton.IsEnabled = false;
            stopButton.IsEnabled = true;
            fpsText.Text = "0 FPS";

            AddLog(tag, $"Tüm hız limitleri kaldırıldı (UNLIMITED FPS) akış başlıyor: {url}", "#F0AD4E");

            Task.Run(async () =>
            {
                CancellationToken token = state.Cts.Token;

                while (state.IsStreaming && !token.IsCancellationRequested)
                {
                    try
                    {
                        string reqUrl = url.Contains("?")
                            ? $"{url}&t={DateTime.Now.Ticks}"
                            : $"{url}?t={DateTime.Now.Ticks}";

                        // AĞ BEKLEMESİ: Tek limitör artık Wi-Fi ağınızın tepki süresi.
                        byte[] data = await _http.GetByteArrayAsync(reqUrl, token);

                        if (data != null && data.Length > 0 && state.IsStreaming)
                        {
                            using MemoryStream ms = new MemoryStream(data);
                            BitmapImage bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.StreamSource = ms;
                            bmp.EndInit();
                            bmp.Freeze();

                            // Ekrana bas
                            await Dispatcher.InvokeAsync(() =>
                            {
                                if (!state.IsStreaming) return;

                                targetImage.Source = bmp;
                                state.FpsCount++;

                                double elapsed = (DateTime.Now - state.FpsLast).TotalSeconds;
                                if (elapsed >= 1.0)
                                {
                                    fpsText.Text = $"{(state.FpsCount / elapsed):F1} FPS";
                                    state.FpsCount = 0;
                                    state.FpsLast = DateTime.Now;
                                }
                            }, DispatcherPriority.Render);
                        }

                        // ZAMANLAYICI YOK: Windows gecikmesi (Task.Delay) tamamen silindi.
                        // Thread'in kilitlenmemesi için sadece sistemin nefes almasına izin veriyoruz (0 ms gecikme)
                        await Task.Yield();
                    }
                    catch (TaskCanceledException) { break; }
                    catch (Exception ex)
                    {
                        await Dispatcher.InvokeAsync(() => AddLog("ERR", $"{tag} Akış Hatası: {ex.Message}", "#FF4040"));
                        await Task.Delay(1000, token); // Sadece internet koparsa 1 saniye bekle
                    }
                }
            });
        }

        private void StopImageStream(
            Image targetImage,
            FrameworkElement placeholder,
            Button startButton,
            Button stopButton,
            TextBlock fpsText,
            ImageStreamState state,
            string tag)
        {
            state.IsStreaming = false;
            state.Cts?.Cancel(); // Arka plan thread döngüsünü tamamen kırar

            targetImage.Source = null;
            targetImage.Visibility = Visibility.Collapsed;
            placeholder.Visibility = Visibility.Visible;

            fpsText.Text = "— FPS";

            startButton.IsEnabled = true;
            stopButton.IsEnabled = false;

            AddLog(tag, "Görüntü akışı durduruldu.", "#484F58");
        }

        private void BtnUnityConnect_Click(object sender, RoutedEventArgs e)
        {
            string hostPort = TxtUnityHost.Text.Trim();
            var parts = hostPort.Split(':');

            if (parts.Length != 2 || !int.TryParse(parts[1], out int port))
            {
                AddLog("ERR", "Unity host formatı yanlış. Örnek: 127.0.0.1:7777", "#FF4040");
                return;
            }

            try
            {
                _unityClient = new TcpClient();
                _unityClient.Connect(parts[0], port);
                _unityStream = _unityClient.GetStream();
                _isUnityConnected = true;

                BtnUnityConnect.IsEnabled = false;
                BtnUnityDisconnect.IsEnabled = true;

                UnityStatusText.Text = "● BAĞLANDI";
                UnityStatusText.Foreground = new SolidColorBrush(Color.FromRgb(63, 185, 80));

                UnityOfflineText.Visibility = Visibility.Collapsed;
                UnityConnectLabel.Text = "TCP: " + hostPort;

                if (!_isAutonomMode)
                {
                    BtnForward.IsEnabled = true;
                    BtnBack.IsEnabled = true;
                    BtnLeft.IsEnabled = true;
                    BtnRight.IsEnabled = true;
                }

                BtnStop.IsEnabled = true;

                AddLog("UNI", $"Unity bağlandı: {hostPort}", "#3FB950");
            }
            catch (Exception ex)
            {
                _isUnityConnected = false;
                AddLog("ERR", $"Unity bağlantısı başarısız: {ex.Message}", "#FF4040");
            }
        }

        private void BtnUnityDisconnect_Click(object sender, RoutedEventArgs e)
        {
            SendToUnity("S");

            _unityStream?.Close();
            _unityClient?.Close();

            _unityStream = null;
            _unityClient = null;
            _isUnityConnected = false;

            BtnUnityConnect.IsEnabled = true;
            BtnUnityDisconnect.IsEnabled = false;

            UnityStatusText.Text = "● BAĞLI DEĞİL";
            UnityStatusText.Foreground = new SolidColorBrush(Color.FromRgb(48, 54, 61));

            UnityOfflineText.Visibility = Visibility.Visible;

            if (!_isConnected)
            {
                BtnForward.IsEnabled = false;
                BtnBack.IsEnabled = false;
                BtnLeft.IsEnabled = false;
                BtnRight.IsEnabled = false;
                BtnStop.IsEnabled = false;
            }

            AddLog("UNI", "Unity bağlantısı kesildi.", "#484F58");
        }

        private void SendToUnity(string msg)
        {
            if (!_isUnityConnected || _unityStream == null)
            {
                AddLog("UNI", $"Unity bağlı değil, gönderilmedi: {msg}", "#484F58");
                return;
            }

            try
            {
                byte[] data = Encoding.ASCII.GetBytes(msg + "\n");
                _unityStream.Write(data, 0, data.Length);
                _unityStream.Flush();

                AddLog("UTX", $"Unity ← {msg}", "#58D6FF");
            }
            catch (Exception ex)
            {
                _isUnityConnected = false;
                AddLog("ERR", $"Unity gönderim hatası: {ex.Message}", "#FF4040");

                UnityStatusText.Text = "● BAĞLI DEĞİL";
                UnityStatusText.Foreground = new SolidColorBrush(Color.FromRgb(48, 54, 61));
                UnityOfflineText.Visibility = Visibility.Visible;

                BtnUnityConnect.IsEnabled = true;
                BtnUnityDisconnect.IsEnabled = false;
            }
        }

        private void SendCmd(byte cmd)
        {
            if (!_isConnected || _port?.IsOpen != true)
            {
                AddLog("COM", $"STM bağlı değil, sadece Unity komutu kullanılabilir: {(char)cmd}", "#484F58");
                return;
            }

            try
            {
                _port.Write(new byte[] { cmd }, 0, 1);
                LastTxText.Text = $"0x{cmd:X2} '{(char)cmd}'";
            }
            catch (Exception ex)
            {
                AddLog("ERR", ex.Message, "#FF4040");
            }
        }

        private void BtnForward_Click(object sender, RoutedEventArgs e)
        {
            SendCmd(CMD_FORWARD);
            SendToUnity("F");

            AddLog("CMD", "Master/Unity ← İLERİ", "#58D6FF");
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            SendCmd(CMD_BACK);
            SendToUnity("B");

            AddLog("CMD", "Master/Unity ← GERİ", "#C9D1D9");
        }

        private void BtnLeft_Click(object sender, RoutedEventArgs e)
        {
            SendCmd(CMD_LEFT);
            SendToUnity("L");

            AddLog("CMD", "Master/Unity ← SOL", "#C9D1D9");
        }

        private void BtnRight_Click(object sender, RoutedEventArgs e)
        {
            SendCmd(CMD_RIGHT);
            SendToUnity("R");

            AddLog("CMD", "Master/Unity ← SAĞ", "#C9D1D9");
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            SendCmd(CMD_STOP);
            SendToUnity("S");

            AddLog("CMD", "Master/Unity ← DUR", "#FF4040");

            if (_isAutonomMode)
            {
                _isAutonomMode = false;
                BtnManuelMod_Click(null, null);
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (!_isConnected && !_isUnityConnected)
                return;

            if (_isAutonomMode && e.Key != Key.Escape && e.Key != Key.Space)
                return;

            switch (e.Key)
            {
                case Key.W:
                    BtnForward_Click(null, null);
                    break;

                case Key.A:
                    BtnLeft_Click(null, null);
                    break;

                case Key.D:
                    BtnRight_Click(null, null);
                    break;

                case Key.X:
                    BtnBack_Click(null, null);
                    break;

                case Key.Space:
                    BtnStop_Click(null, null);
                    break;

                case Key.Escape:
                    if (_isAutonomMode)
                    {
                        SendCmd(CMD_ABORT);
                        SendToUnity("S");
                        BtnManuelMod_Click(null, null);
                        AddLog("CMD", "OTONOM İPTAL — Manuel moda geçildi.", "#FF4040");
                    }
                    break;
            }
        }

        private void SetFixUI(Ellipse dot, TextBlock txt, bool fix)
        {
            var green = new SolidColorBrush(Color.FromRgb(63, 185, 80));
            var gray = new SolidColorBrush(Color.FromRgb(72, 79, 88));

            dot.Fill = fix ? green : gray;
            txt.Text = fix ? "GPS FIX" : "NO FIX";
            txt.Foreground = fix ? green : gray;
        }

        private void UpdateBnoUI(
            Border yBar, TextBlock yTxt, double yW, double yaw,
            Border pBar, TextBlock pTxt, double pW, double pitch,
            Border rBar, TextBlock rTxt, double rW, double roll)
        {
            if (yW > 0)
                yBar.Width = yW * (yaw / 360.0);

            yTxt.Text = $"{yaw:F1}°";

            if (pW > 0)
                pBar.Width = pW * Math.Abs(pitch) / 90.0;

            pTxt.Text = $"{pitch:F1}°";

            if (rW > 0)
                rBar.Width = rW * Math.Abs(roll) / 90.0;

            rTxt.Text = $"{roll:F1}°";
        }

        private void UpdateSlaveStateUI()
        {
            var purple = new SolidColorBrush(Color.FromRgb(167, 139, 250));
            var green = new SolidColorBrush(Color.FromRgb(63, 185, 80));
            var gray = new SolidColorBrush(Color.FromRgb(72, 79, 88));

            switch (_slaveState)
            {
                case SlaveState.IDLE:
                    SlaveStateText.Text = "IDLE";
                    SlaveStateText.Foreground = gray;
                    SlaveStateDot.Fill = gray;
                    SlaveStateDetailText.Text = "IDLE — Hedef bekleniyor";
                    SlaveStateDetailText.Foreground = gray;
                    break;

                case SlaveState.NAVIGATING:
                    SlaveStateText.Text = "NAV";
                    SlaveStateText.Foreground = purple;
                    SlaveStateDot.Fill = purple;
                    SlaveStateDetailText.Text = "NAVİGASYON — Hedefe gidiyor";
                    SlaveStateDetailText.Foreground = purple;
                    break;

                case SlaveState.ARRIVED:
                    SlaveStateText.Text = "VAR";
                    SlaveStateText.Foreground = green;
                    SlaveStateDot.Fill = green;
                    SlaveStateDetailText.Text = "ARRIVED — Hedef noktasında";
                    SlaveStateDetailText.Foreground = green;
                    break;
            }
        }

        public static double Haversine(double la1, double lo1, double la2, double lo2)
        {
            const double R = 6371000;

            double dLat = ToRad(la2 - la1);
            double dLon = ToRad(lo2 - lo1);

            double a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(la1)) * Math.Cos(ToRad(la2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        public static double Bearing(double la1, double lo1, double la2, double lo2)
        {
            double dLon = ToRad(lo2 - lo1);

            double y = Math.Sin(dLon) * Math.Cos(ToRad(la2));
            double x =
                Math.Cos(ToRad(la1)) * Math.Sin(ToRad(la2)) -
                Math.Sin(ToRad(la1)) * Math.Cos(ToRad(la2)) * Math.Cos(dLon);

            return (ToDeg(Math.Atan2(y, x)) + 360) % 360;
        }

        private static double ToRad(double d)
        {
            return d * Math.PI / 180;
        }

        private static double ToDeg(double r)
        {
            return r * 180 / Math.PI;
        }

        private void AddLog(string tag, string msg, string hex)
        {
            Dispatcher.Invoke(() =>
            {
                string ts = DateTime.Now.ToString("HH:mm:ss.fff");

                _logBuf.AppendLine($"{ts}  [{tag,-3}]  {msg}");

                Color c = (Color)ColorConverter.ConvertFromString(hex);

                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 1, 0, 1)
                };

                row.Children.Add(new TextBlock
                {
                    Text = ts + "  ",
                    Foreground = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11
                });

                row.Children.Add(new TextBlock
                {
                    Text = $"[{tag,-3}]  ",
                    Foreground = new SolidColorBrush(c),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold
                });

                row.Children.Add(new TextBlock
                {
                    Text = msg,
                    Foreground = new SolidColorBrush(Color.FromRgb(201, 209, 217)),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                });

                LogPanel.Children.Add(row);

                while (LogPanel.Children.Count > 400)
                    LogPanel.Children.RemoveAt(0);

                LogScroll.ScrollToEnd();
            });
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            LogPanel.Children.Clear();
            _logBuf.Clear();

            AddLog("SYS", "Log temizlendi.", "#484F58");
        }

        private void BtnSaveLog_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "Text (*.txt)|*.txt",
                FileName = $"tarantula_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(dlg.FileName, _logBuf.ToString(), Encoding.UTF8);
                    LogFilePathText.Text = $"Kaydedildi: {System.IO.Path.GetFileName(dlg.FileName)}";
                    AddLog("SYS", "Log kaydedildi.", "#3FB950");
                }
                catch (Exception ex)
                {
                    AddLog("ERR", ex.Message, "#FF4040");
                }
            }
        }

        private void BtnOpenLog_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Text (*.txt)|*.txt" };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    LogPanel.Children.Clear();

                    foreach (string line in File.ReadAllLines(dlg.FileName))
                    {
                        LogPanel.Children.Add(new TextBlock
                        {
                            Text = line,
                            FontFamily = new FontFamily("Consolas"),
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.FromRgb(100, 110, 120)),
                            TextWrapping = TextWrapping.Wrap
                        });
                    }

                    LogFilePathText.Text = $"Görüntüleniyor: {System.IO.Path.GetFileName(dlg.FileName)}";
                    LogScroll.ScrollToEnd();
                }
                catch (Exception ex)
                {
                    AddLog("ERR", ex.Message, "#FF4040");
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _clockTimer?.Stop();
            _navTimer?.Stop();
            _animTimer?.Stop();

            _rpiStream.IsStreaming = false;
            _rpiStream.Cts?.Cancel(); // Kapatırken arka plan thread'ini bitir

            try
            {
                if (UnityLiveView.CoreWebView2 != null)
                    UnityLiveView.CoreWebView2.Navigate("about:blank");
            }
            catch
            {
            }

            try
            {
                SendToUnity("S");
            }
            catch
            {
            }

            _unityStream?.Close();
            _unityClient?.Close();

            if (_port?.IsOpen == true)
            {
                _port.Close();
                _port.Dispose();
            }

            _http.Dispose();

            base.OnClosed(e);
        }
    }
}