using System;

namespace VtNetCore.Avalonia
{
    public class DataReceivedEventArgs : EventArgs, IEquatable<DataReceivedEventArgs>
    {
        public byte[] Data { get; set; }

        public bool Equals(DataReceivedEventArgs other)
        {
            return ReferenceEquals(this, other);
        }
    }

    public interface IConnection
    {
        bool IsConnected { get; }

        event EventHandler<DataReceivedEventArgs> DataReceived;

        bool Connect();

        void Disconnect();

        void SendData(byte[] data);

        void SetTerminalWindowSize(int columns, int rows, int width, int height);
    }
}
