using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

//[20180221]Create by Simon
namespace MP_Module
{
    public static class NumConverter
    {
        /// <summary>
        /// 將List Byte轉為16進制字串
        /// </summary>
        /// <param name="data">List Byte</param>
        /// <returns>16進制字串</returns>
        public static string ToHexString(List<byte> data)
        {
            return ToHexString(data.ToArray());
        }
        /// <summary>
        /// 將Byte Array轉為16進制字串
        /// </summary>
        /// <param name="data">Byte Array</param>
        /// <returns>16進制字串</returns>
        public static string ToHexString(byte[] data)
        {
            if (data == null || data.Length == 0) { return "N/A"; }

            return BitConverter.ToString(data);
        }

        /// <summary>
        /// 將Byte Array轉為ASCII字串
        /// </summary>
        /// <param name="data">Byte Array</param>
        /// <returns>ASCII字串</returns>
        public static string ToAsciiString(byte[] data)
        {
            ASCIIEncoding encoder = new ASCIIEncoding();
            string result;
            try
            {
                result = encoder.GetString(data);
            }
            catch { throw; }
            return result;
        }

        /// <summary>
        /// 將Hex字串轉為Byte Array，輸入前請先移除不必要的符號
        /// </summary>
        /// <param name="data">Hex String</param>
        /// <returns>Byte Array</returns>
        public static byte[] HexStringToByteArray(string data)
        {
            byte[] result;
            try
            {
                result =
                    Enumerable.Range(0, data.Length)
                    .Where(x => x % 2 == 0)
                    .Select(x => Convert.ToByte(data.Substring(x, 2), 16))
                    .ToArray();
            }
            catch { throw; }
            return result;
        }

        /// <summary>
        /// 將Hex字串轉為Byte List，輸入前請先移除不必要的符號
        /// </summary>
        /// <param name="data">Hex String</param>
        /// <returns>Byte Array</returns>
        public static List<byte> HexStringToListByte(string data)
        {
            List<byte> result;
            try
            {
                result = Enumerable.Range(0, data.Length)
                    .Where(x => x % 2 == 0)
                    .Select(x => Convert.ToByte(data.Substring(x, 2), 16)).ToList();
            }
            catch { throw; }
            return result;
        }

        /// <summary>
        /// 將ASCII字串轉為Byte Array
        /// </summary>
        /// <param name="data">ASCII字串</param>
        /// <returns>Byte Array</returns>
        public static byte[] AsciiStringToByte(string data)
        {
            ASCIIEncoding result = new ASCIIEncoding();
            return result.GetBytes(data);
        }
    }
}
