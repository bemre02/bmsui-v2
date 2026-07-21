namespace BmsUi.Serial;

/// <summary>
/// Seri I/O soyutlamasi. SerialLink'in gercek COM portu olmadan test edilebilmesi icin var.
/// Read(): veri gelmezse TimeoutException firlatir (SerialPort davranisiyla ayni).
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
