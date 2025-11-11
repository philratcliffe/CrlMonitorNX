namespace CrlMonitor.Tests.Licensing;

internal sealed class ConsoleCapture : IDisposable
{
    private readonly TextWriter _originalOut;
    private readonly TextWriter _originalError;
    private readonly StringWriter _writer = new();

    private ConsoleCapture()
    {
        this._originalOut = Console.Out;
        this._originalError = Console.Error;
        Console.SetOut(this._writer);
        Console.SetError(TextWriter.Null);
    }

    public static ConsoleCapture Start()
    {
        return new ConsoleCapture();
    }

    public string GetOutput()
    {
        return this._writer.ToString();
    }

    public void Dispose()
    {
        Console.SetOut(this._originalOut);
        Console.SetError(this._originalError);
        this._writer.Dispose();
    }
}
