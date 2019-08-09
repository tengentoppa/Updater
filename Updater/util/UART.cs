using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Ports;

//[20180221]Create by Simon
namespace MP_Module
{
    public class UART : SerialPort
    {
        //public event SerialDataReceivedEventHandler keepProccessFunc;

        public bool KeepReceiveFlag = false;
        protected Action<byte[]> RxFunc;
        public Action<List<byte>> TxFunc;

        public UART() : base() { }
        public UART(string portName) : base(portName) { }
        public UART(IContainer container) : base(container) { }
        public UART(string portName, int baudRate) : base(portName, baudRate) { }
        public UART(string portName, int baudRate, Parity parity) : base(portName, baudRate, parity) { }
        public UART(string portName, int baudRate, Parity parity, int dataBits) : base(portName, baudRate, parity, dataBits) { }
        public UART(string portName, int baudRate, Parity parity, int dataBits, StopBits stopBits) : base(portName, baudRate, parity, dataBits, stopBits) { }

        public void ClearBuffer()
        {
            if (!IsOpen) return;
            try
            {
                DiscardInBuffer();
                DiscardOutBuffer();
            }
            catch { }
        }

        /// <summary>
        /// 將byte清單送出
        /// </summary>
        /// <param name="data">要送出的資料</param>
        /// <returns>發送狀態</returns>
        public void SendData(List<byte> data)
        {
            try
            {
                Write(data.ToArray(), 0, data.Count);
                TxFunc?.Invoke(data);
            }
            catch { throw; }
        }

        /// <summary>
        /// 從UART接收資料，單次執行
        /// </summary>
        /// <returns>UART接收到的資料</returns>
        public byte[] ReceiveAllData()
        {
            if (BytesToRead == 0) { return null; }
            byte[] buff;
            try
            {
                buff = new byte[BytesToRead];
                Read(buff, 0, BytesToRead);
            }
            catch { throw; }
            return buff;
        }

        protected void KeepReceiveData(object sender, SerialDataReceivedEventArgs e)
        {
            if (!KeepReceiveFlag)
            {
                StopReceiveData();
                return;
            }
            byte[] rcvData;
            try
            {
                rcvData = ReceiveAllData();
            }
            catch (Exception exp) { Console.WriteLine(exp.Message); return; }
            if (rcvData == null || rcvData.Length == 0) { return; }
            RxFunc?.Invoke(rcvData);
        }

        /// <summary>
        /// 持續接收資料，訂閱此事件以避免Uart當住
        /// </summary>
        public void StartReceiveData(Action<byte[]> rxFunc)
        {
            if (KeepReceiveFlag) { return; }
            RxFunc = rxFunc;
            KeepReceiveFlag = true;
            DataReceived += KeepReceiveData;
        }
        public void StopReceiveData()
        {
            KeepReceiveFlag = false;
            RxFunc = null;
            DataReceived -= KeepReceiveData;
        }
    }
}
