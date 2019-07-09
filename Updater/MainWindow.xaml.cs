using Microsoft.Win32;
using MP_Module;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Interop;
using Updater.helper;
using Updater.Properties;
using WindowPlacementHelper;

namespace Updater
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Define zone
        readonly string STARTUP_PATH = AppDomain.CurrentDomain.BaseDirectory;
        const uint MAX_FILE_SIZE = ushort.MaxValue;
        const int COMMON_TIMEOUT_MS = 3000;
        const string SETTINGS_FILE_PATH = "settings.xml";

        UpdaterSetting Settings;

        string binPath = AppDomain.CurrentDomain.BaseDirectory;
        ObservableCollection<string> uartList = new ObservableCollection<string>();
        string selectedUart;
        bool uartOpened = true;
        bool updating = false;
        bool accessible = true;
        bool updatePaused = false;
        List<byte> UartRxData = new List<byte>();

        UART Uart;
        Queue<string> qLog = new Queue<string>();
        List<byte> UpdateData = null;
        Queue<LPS50A> qLPS50A = new Queue<LPS50A>();

        public string VersionInfo { get { return "v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(); } }
        public string BinPath {
            get { if (!(File.Exists(binPath) || Directory.Exists(binPath))) { binPath = STARTUP_PATH; } return binPath; }
            set { binPath = value; OnPropertyChanged(nameof(BinPath)); UpdateData = ReadAllByteFromFile(value); }
        }
        public string SelectedUart {
            get { return selectedUart; }
            set { selectedUart = value; OnPropertyChanged(nameof(SelectedUart)); }
        }
        public bool UartOpened {
            get { return uartOpened; }
            set { if (!OpenCloseUart(value)) { return; } uartOpened = value; OnPropertyChanged(nameof(UartOpened)); Accessible = uartOpened & !updating; }
        }
        public bool Updating {
            get { return updating; }
            set { updating = value; Accessible = uartOpened & !updating; OnPropertyChanged(nameof(Updating)); }
        }
        public bool Accessible {
            get { return accessible; }
            set { accessible = value; OnPropertyChanged(nameof(Accessible)); }
        }
        public bool UpdatePaused {
            get { return updatePaused; }
            set { updatePaused = value; OnPropertyChanged(nameof(UpdatePaused)); }
        }
        public ObservableCollection<string> UartList {
            get { return uartList; }
            set {
                uartList = value;
                UartList.CollectionChanged += (sender, e) => { OnPropertyChanged(nameof(UartList)); };
                OnPropertyChanged(nameof(UartList));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        #endregion

        public MainWindow()
        {
            DataContext = this;
            InitializeComponent();
        }
        private void Init()
        {
            LoadSettings(SETTINGS_FILE_PATH);
            SearchUart();
        }

        private UpdaterSetting LoadSettings(string path)
        {
            var result = new UpdaterSetting();
            try
            {
                var content = File.ReadAllText(path);
                result = XmlHelper.Deserialize<UpdaterSetting>(content);
            }
            catch
            {
                SaveSettings(SETTINGS_FILE_PATH, result);
            }
            return result;
        }

        private void SaveSettings(string path, UpdaterSetting settings)
        {
            try
            {
                var saveString = XmlHelper.Serialize(settings);
                File.WriteAllText(path, saveString);
            }
            catch { }
        }

        void outToLog()
        {
            if (qLog.Count == 0) { return; }
            Dispatcher.Invoke(() =>
            {
                Paragraph p = new Paragraph();
                p.Inlines.Add(qLog.Dequeue());
                while (qLog.Count > 0) { p.Inlines.InsertBefore(p.Inlines.FirstInline, new Run(qLog.Dequeue() + Environment.NewLine)); }
                rtbLog.Document.Blocks.InsertBefore(rtbLog.Document.Blocks.FirstBlock, p);
            });
        }

        #region Data Process
        private List<byte> ReadAllByteFromFile(string filePath)
        {
            if (!File.Exists(filePath)) { return null; }
            var data = File.ReadAllBytes(filePath).ToList();
            if (data.Count > MAX_FILE_SIZE) { return null; }
            if (data.Count % Settings.BYTE_PER_PACK != 0) { data.AddRange(new List<byte>(Enumerable.Repeat(Settings.PADDING_BYTE, (int)(Settings.BYTE_PER_PACK - (data.Count % Settings.BYTE_PER_PACK))))); }
            return data;
        }
        private void ReceivedData(byte[] data)
        {
            OutLog("Rx Data", NumConverter.ToHexString(data));
            UartRxData.AddRange(data);
            var lps50aData = LPS50A.ParseData(UartRxData);
            if (lps50aData == null) { return; }
            foreach (var lps50a in lps50aData)
            {
                var d = new LPS50A(lps50a);
                if (d == null || !d.Formated) { continue; }

                OutLog("LPS50A Data", d.ToString());
                qLPS50A.Enqueue(d);
            }
        }
        private void TransedData(List<byte> data)
        {
            OutLog("Tx Data", NumConverter.ToHexString(data));
        }

        private void OutLog(string title, string conent)
        {
            qLog.Enqueue(WriteLog.GetFmtStr(title, conent));
        }

        private bool WaitRx(LPS50A comparer, bool abortWhenWrongRx = false, int timeout = COMMON_TIMEOUT_MS)
        {
            if (comparer == null) { throw new ArgumentNullException("comparer", "Comparer can't be null."); }
            DateTime dtStart = DateTime.Now;
            while ((DateTime.Now - dtStart).TotalMilliseconds < timeout)
            {
                while (qLPS50A.Count > 0)
                {
                    var data = qLPS50A.Dequeue();
                    if (data.Cmd == (comparer.Cmd ?? data.Cmd) &&
                        data.Statue == (comparer.Statue ?? data.Statue)
                        ) { return true; }
                    if (abortWhenWrongRx) { return false; }
                }
            }
            return false;
        }
        #endregion

        #region WindowEvent
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Task.Run(() => { while (true) { Thread.Sleep(20); outToLog(); } });
        }
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            UartOpened = false;
            Properties.Settings.Default.MainWindowPlacement = (this).GetPlacement();
            Properties.Settings.Default.Save();
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            (this).SetPlacement(Properties.Settings.Default.MainWindowPlacement);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd).AddHook(new HwndSourceHook(WndProc));

            Init();
        }
        #endregion

        #region Button Event
        private void BtnPort_Click(object sender, RoutedEventArgs e)
        {
            UartOpened = !UartOpened;
        }
        private void BtnBrowseFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog() { DefaultExt = ".bin", Filter = "Bin File (*.bin)|*.bin", InitialDirectory = BinPath };
            if (!(ofd.ShowDialog() ?? false)) { return; }
            if (!ofd.CheckFileExists) { return; }
            BinPath = ofd.FileName;
        }
        private void BtnReadVer_Click(object sender, RoutedEventArgs e)
        {
            //Uart.SendData(NumConverter.HexStringToListByte(""));
        }
        private void BtnReadyToUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (UpdateData == null) { MessageBox.Show("File not loaded"); return; }
            var dataLen = BitConverter.GetBytes((ushort)UpdateData.Count);
            if (BitConverter.IsLittleEndian) { Array.Reverse(dataLen); }
            List<byte> data = new List<byte> { 0x01, dataLen[0], dataLen[1] };
            Uart.SendData(LPS50A.PackData(LPS50A.CMD.StartUpdate, data));
        }
        private void BtnStartUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (UpdateData == null) { MessageBox.Show("File not loaded"); return; }
            ushort dataCount = 0;
            List<byte> data;
            Updating = true;
            Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < UpdateData.Count; i += (int)Settings.BYTE_PER_PACK)
                    {
                        if (!Updating) { return; }
                        if (UpdatePaused) { SpinWait.SpinUntil(() => { return !UpdatePaused || !Updating; }); }
                        var count = BitConverter.GetBytes(dataCount);
                        if (BitConverter.IsLittleEndian) { Array.Reverse(count); }
                        data = new List<byte> { count[0], count[1] };
                        data.AddRange(UpdateData.GetRange(i, (int)Settings.BYTE_PER_PACK));
                        qLPS50A.Clear();
                        Uart.SendData(LPS50A.PackData(LPS50A.CMD.TransUpdateData, data));
                        if (!Updating) { return; }
                        if (!WaitRx(new LPS50A(LPS50A.CMD.TransUpdateData, LPS50A.ErrorStatue.Success), true)) { return; }
                        dataCount++;
                    }
                }
                finally
                {
                    Updating = false;
                }
            });
        }
        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            Updating = false;
        }
        private void BtnPauseAndResume_Click(object sender, RoutedEventArgs e)
        {
            UpdatePaused = !UpdatePaused;
        }
        private void BtnFinishUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (UpdateData == null) { MessageBox.Show("File not loaded"); return; }
            var checkSum = UpdateData.Aggregate((x, y) => { return (byte)(x ^ y); });
            Uart.SendData(LPS50A.PackData(LPS50A.CMD.InstallUpdateData, checkSum));
        }
        #endregion

        #region Wnd
        const int WM_DEVICECHANGE = 0x0219;
        const int DBT_DEVICEARRIVAL = 0x8004;
        const int DBT_DEVICEREMOVECOMPLEETE = 0x8004;
        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_DEVICECHANGE:
                    SearchUart();
                    break;
            }
            return IntPtr.Zero;
        }
        #endregion

        #region UART Operation
        void SearchUart()
        {
            UartList = new ObservableCollection<string>(System.IO.Ports.SerialPort.GetPortNames());

            if (UartList.Count > 0) { SelectedUart = UartList[0]; }
            CheckUart();
        }
        private void CheckUart()
        {
            if (Uart == null) { return; }
            int index = UartList.IndexOf(Uart.PortName);
            if (index != -1)
            {
                cbxUart.SelectedIndex = index;
                return;
            }
            UartOpened = false;
        }
        private bool OpenCloseUart(bool openUart)
        {
            if (openUart) { return OpenUart(); } else { return CloseUart(); }
        }
        private bool OpenUart()
        {
            if (string.IsNullOrEmpty(SelectedUart)) { return false; }
            if (!UartList.Contains(SelectedUart)) { return false; }
            try
            {
                InitUart(SelectedUart, Settings.BAUD_RATE);
                return true;
            }
            catch { return false; }
        }
        private bool CloseUart()
        {
            if (Uart == null) { return true; }
            StopUartAction();
            Uart.StopReceiveData();
            Uart.Close();
            return true;
        }

        private void StopUartAction()
        {
            Updating = false;
            UpdatePaused = false;
        }

        private void InitUart(string selectedUart, int buadRate)
        {
            Uart = new UART(selectedUart, buadRate);
            Uart.StartReceiveData(ReceivedData);
            Uart.TxFunc += TransedData;
            Uart.Open();
            Uart.ClearBuffer();
        }
        #endregion
    }

    public class UpdaterSetting
    {
        public int BAUD_RATE { get; set; } = 115200;
        public uint BYTE_PER_PACK { get; set; } = 0x40;
        public byte PADDING_BYTE { get; set; } = 0xFF;
    }
}
