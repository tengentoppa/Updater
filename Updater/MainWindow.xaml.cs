using Microsoft.Win32;
using MP_Module;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Interop;
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
        const int BAUD_RATE = 115200;
        const uint MAX_FILE_SIZE = ushort.MaxValue;
        const uint BytePerPack = 0x10;
        const byte MakeupByte = 0xFF;

        string binPath = AppDomain.CurrentDomain.BaseDirectory;
        ObservableCollection<string> uartList = new ObservableCollection<string>();
        string selectedUart;
        bool uartOpened = false;
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
        public string SelectedUart { get { return selectedUart; } set { selectedUart = value; OnPropertyChanged(nameof(SelectedUart)); } }
        public bool UartOpened { get { return uartOpened; } set { uartOpened = value; OnPropertyChanged(nameof(UartOpened)); } }
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
            SearchUart();
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

        private List<byte> ReadAllByteFromFile(string filePath)
        {
            if (!File.Exists(filePath)) { return null; }
            var data = File.ReadAllBytes(filePath).ToList();
            if (data.Count > MAX_FILE_SIZE) { return null; }
            if (data.Count % BytePerPack != 0) { data.AddRange(new List<byte>(Enumerable.Repeat(MakeupByte, (int)(BytePerPack - (data.Count % BytePerPack))))); }
            return data;
        }
        private void ReceivedData(byte[] data)
        {
            if (data == null || data.Length == 0) { return; }
            OutLog("Rx Data", NumConverter.ToHexString(data));
            UartRxData.AddRange(data);
            var lps50aData = LPS50A.ParseData(UartRxData);
            foreach (var lps50a in lps50aData)
            {
                var d = new LPS50A(lps50a);
                if (!d.Formated) { continue; }

                qLPS50A.Enqueue(d);
            }
        }

        private void OutLog(string title, string conent)
        {
            qLog.Enqueue(WriteLog.GetFmtStr("Rx Data", conent));
        }

        private bool WaitRx(LPS50A.CMD cmd, int timeout)
        {
            DateTime dtStart = DateTime.Now;
            while ((DateTime.Now - dtStart).TotalMilliseconds < timeout)
            {
                while (qLPS50A.Count > 0)
                {
                    var data = qLPS50A.Dequeue();
                    if (data.Cmd == cmd && data.Statue == LPS50A.ErrorStatue.Success) { return true; }
                }
            }
            return false;
        }
        #region WindowEvent
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Task.Run(() => { while (true) { Thread.Sleep(10); outToLog(); } });
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            CloseUart();
            Settings.Default.MainWindowPlacement = this.GetPlacement();
            Settings.Default.Save();
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            this.SetPlacement(Settings.Default.MainWindowPlacement);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd).AddHook(new HwndSourceHook(WndProc));

            Init();
        }
        #endregion

        #region ButtonEvent
        private void BtnPort_Click(object sender, RoutedEventArgs e)
        {
#if DEBUG
            UartOpened = !UartOpened;
#endif
#if !DEBUG
            if (UartOpened) { CloseUart(); } else { OpenUart(); }
#endif
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
            btnPort.IsEnabled = false;
            btnFinishUpdate.IsEnabled = false;
            btnReadVer.IsEnabled = false;
            btnReadyToUpdate.IsEnabled = false;
            btnStartUpdate.IsEnabled = false;

            if (UpdateData == null) { MessageBox.Show("File not loaded"); return; }
            ushort dataCount = 0;
            List<byte> data;
            Task.Run(() =>
            {
                try
                {
                    for (int i = 0; i < UpdateData.Count; i += (int)BytePerPack)
                    {
                        var count = BitConverter.GetBytes(dataCount);
                        if (BitConverter.IsLittleEndian) { Array.Reverse(count); }
                        data = new List<byte> { count[0], count[1] };
                        data.AddRange(UpdateData.GetRange(i, (int)BytePerPack));
                        qLPS50A.Clear();
                        Uart.SendData(LPS50A.PackData(LPS50A.CMD.TransUpdateData, data));

                        if (!WaitRx(LPS50A.CMD.TransUpdateData, 3000)) { return; }
                        dataCount++;
                    }
                }
                finally
                {
                    Dispatcher.Invoke(() =>
                    {
                        btnPort.IsEnabled = true;
                        btnFinishUpdate.IsEnabled = true;
                        btnReadVer.IsEnabled = true;
                        btnReadyToUpdate.IsEnabled = true;
                        btnStartUpdate.IsEnabled = true;
                    });
                }
            });
        }

        private void BtnFinishUpdate_Click(object sender, RoutedEventArgs e)
        {
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
            CloseUart();
        }
        private void OpenUart()
        {
            if (string.IsNullOrEmpty(SelectedUart)) { return; }
            if (!UartList.Contains(SelectedUart)) { return; }
            try
            {
                InitUart(SelectedUart, BAUD_RATE);
            }
            catch { return; }
            UartOpened = true;
        }
        private void CloseUart()
        {
            UartOpened = false;
            if (Uart == null) { return; }
            Uart.StopReceiveData();
            Uart.Close();
        }
        private void InitUart(string selectedUart, int buadRate)
        {
            Uart = new UART(selectedUart, buadRate);
            Uart.StartReceiveData(ReceivedData); //TODO
            Uart.TxFunc += TransedData;
            Uart.Open();
            Uart.ClearBuffer();
        }

        private void TransedData(List<byte> obj)
        {
            throw new NotImplementedException();
        }
        #endregion
    }

    #region Binding Converter
    [ValueConversion(typeof(bool), typeof(bool))]
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType != typeof(bool)) { throw new InvalidOperationException("The target must be a boolean"); }
            return !(bool)value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    [ValueConversion(typeof(bool), typeof(object))]
    public class BoolPortStatueToStrConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (targetType != typeof(object)) { throw new InvalidOperationException("The target must be a object"); }
            return (!(bool)value) ? "Open" : "Close";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
    #endregion
}
