using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace curse.NNSocket
{
    public static class Util
    {
        public static async Task ReadExactBytes(this NetworkStream _stream, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int readNow = await _stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read));
                read += readNow;
            }
        }
        public static async Task<InputFrameType> ReadMessageType(this NetworkStream _stream)
        {
            byte[] s =new byte[2];
            await _stream.ReadExactBytes(s);
            var type = (InputFrameType)BitConverter.ToInt16(s);
            return type;
        }
        public static async Task<int> ReadInt(this NetworkStream _stream)
        {
            byte[] s = new byte[4];
            await _stream.ReadExactBytes(s);
            var intMessage =BitConverter.ToInt32(s);
            return intMessage;
        }
        public static async Task WriteMessage(this NetworkStream _stream, OutputFrameType output, byte[] info)
        {
            await _stream.WriteAsync(BitConverter.GetBytes(info.Length));
            await _stream.WriteAsync(BitConverter.GetBytes((short)output));
            await _stream.WriteAsync(info);
         
        }

    }
}
