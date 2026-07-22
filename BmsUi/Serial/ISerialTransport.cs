namespace BmsUi.Serial;

/// <summary>
/// Serial I/O abstraction. Exists so SerialLink can be tested without a real COM port.
/// Read(): throws TimeoutException when no data arrives (same contract as SerialPort).
/// </summary>
public interface ISerialTransport : IDisposable
{
    bool IsOpen { get; }
    void Open();
    void Close();
    void DiscardInBuffer();
    void Write(byte[] buffer, int offset, int count);
    int Read(byte[] buffer, int offset, int count);
}
