using System.Collections.Generic;
using System.Linq;

//[20180221]Create by Simon
namespace MP_Module
{
    class LPS50A
    {
        //STX(1) + CMD(1) + Data(N) + CRC(1){ XOR From STX to Data } + ETX(1)
        public enum CMD
        {
            StartUpdate = 0xE0,
            TransUpdateData = 0xE1,
            InstallUpdateData = 0xE2
        };
        public enum ErrorStatue
        {
            None = 0xFF,
            Success = 0x00,
            Fail = 0x01,
        }
        const byte SSTX = 0x80;
        const byte RSTX = 0x80;

        #region Static Zone
        /// <summary>
        /// Pack data
        /// </summary>
        /// <param name="cmd">CMD</param>
        /// <param name="data">Data</param>
        /// <returns>Pakage</returns>
        public static List<byte> PackData(CMD cmd, List<byte> data)
        {
            byte len = 0;
            if (data != null) len += (byte)data.Count;
            List<byte> result = new List<byte>();
            result.Add(SSTX);
            result.Add((byte)cmd);
            result.Add(len);
            if (data != null) result.AddRange(data);
            result.Add(MakeCrc(result));
            return result;
        }
        /// <summary>
        /// Pack data
        /// </summary>
        /// <param name="cmd">CMD</param>
        /// <param name="data">Data</param>
        /// <returns>Pakage</returns>
        public static List<byte> PackData(CMD cmd, byte data)
        {
            byte len = 1;
            List<byte> result = new List<byte>();
            result.Add(SSTX);
            result.Add((byte)cmd);
            result.Add(len);
            result.Add(data);
            result.Add(MakeCrc(result));
            return result;
        }
        /// <summary>
        /// Pack data
        /// </summary>
        /// <param name="cmd">CMD</param>
        /// <returns>Pakage</returns>
        public static List<byte> PackData(CMD cmd)
        {
            byte len = 0;
            List<byte> result = new List<byte>();
            result.Add(SSTX);
            result.Add((byte)cmd);
            result.Add(len);
            result.Add(MakeCrc(result));
            return result;
        }
        /// <summary>
        /// Parse data
        /// </summary>
        /// <param name="data">Raw data</param>
        /// <returns>Parsed data. Return null when no data is match with this format.</returns>
        public static List<List<byte>> ParseData(List<byte> data)
        {
            if (data == null) return null;
            List<List<byte>> output = new List<List<byte>>();

            int pStx = 0, len, pEnd, totalLen;
            while ((pStx = data.IndexOf(RSTX, pStx)) != -1)
            {
                if (data.Count <= pStx + 4) { return null; }
                len = data[pStx + 2];
                totalLen = len + 4;
                pEnd = pStx + totalLen - 1;
                if (data.Count <= pEnd) { return null; }
                if (data[pEnd] != MakeCrc(data, pStx, totalLen - 1)) { pStx++; continue; }
                output.Add(data.GetRange(pStx, totalLen));

                data.RemoveRange(pStx, totalLen);
            }
            return output;
        }

        /// <summary>
        /// 以指定範圍的List<byte>製作CRC驗證碼</byte>
        /// </summary>
        /// <param name="input">輸入List</param>
        /// <param name="pStart">起使位置</param>
        /// <param name="length">納入運算的長度</param>
        /// <returns>CRC驗證碼</returns>
        public static byte MakeCrc(List<byte> input, int pStart, int length)
        {
            try
            {
                return MakeCrc(input.GetRange(pStart, length));
            }
            catch { throw; }
        }
        /// <summary>
        /// 將輸入清單的每個元素作XOR運算
        /// </summary>
        /// <param name="input">輸入清單</param>
        /// <returns>CRC驗證碼</returns>
        public static byte MakeCrc(List<byte> input)
        {
            byte result = 0x00;
            try
            {
                result = input.Aggregate((x, y) => { return (byte)(x ^ y); });
            }
            catch { throw; }
            return result;
        }
        #endregion

        #region Dynamic Zone
        public CMD? Cmd { get; protected set; } = null;
        public ErrorStatue? Statue { get; protected set; } = null;
        public List<byte> Data { get; protected set; } = null;
        public int DataLen { get; protected set; } = 0;
        public bool Formated { get; protected set; } = false;

        public LPS50A(CMD? cmd = null, ErrorStatue? statue = null) { Cmd = cmd; Statue = statue; }
        public LPS50A(List<byte> source)
        {
            FormatData(source);
        }

        public void FormatData(List<byte> source)
        {
            if (source == null) { return; }
            int totalLen = source.Count;
            if (totalLen < 4) { return; }
            if (source[0] != RSTX) { return; }
            if (source[2] + 4 != totalLen) { return; }  //DataLen + [HEAD + END](4 bytes)
            Cmd = (CMD)source[1];
            DataLen = source[2];
            if (DataLen > 0)
            {
                Statue = (ErrorStatue)source[3];
                DataLen--;
                if (DataLen > 0)
                {
                    Data = source.GetRange(3, DataLen);
                }
            }
            Formated = true;
        }

        public override string ToString()
        {
            if (!Formated) return "Not Formated";
            return $"CMD: {(Cmd == null ? "N/A" : Cmd.ToString())}, DataLen: {DataLen}, Data: {NumConverter.ToHexString(Data)}";
        }
        #endregion
    }
}
