using System.IO.Ports;

namespace BmsUi.Serial;

/// <summary>Wraps System.IO.Ports.SerialPort. ReadLine() is never used.</summary>
public sealed class SerialPortTransport : ISerialTransport
{
    private readonly SerialPort _port;

    public SerialPortTransport(string portName, int baudRate = 115200,
                               int readTimeoutMs = 200, int writeTimeoutMs = 200)
    {
        _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = readTimeoutMs,
            WriteTimeout = writeTimeoutMs,
            DtrEnable = true,       // CDC devices usually expect DTR
            RtsEnable = true,
        };
    }

    public string PortName => _port.PortName;
    public bool IsOpen => _port.IsOpen;
    public void Open() => _port.Open();
    public void Close() { if (_port.IsOpen) _port.Close(); }
    public void DiscardInBuffer() { if (_port.IsOpen) _port.DiscardInBuffer(); }
    public void Write(byte[] buffer, int offset, int count) => _port.Write(buffer, offset, count);
    public int Read(byte[] buffer, int offset, int count) => _port.Read(buffer, offset, count);

    public void Dispose()
    {
        try { Close(); } catch { /* the port may already be gone */ }
        _port.Dispose();
    }
}
