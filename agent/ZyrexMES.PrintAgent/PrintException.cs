namespace ZyrexMES.PrintAgent;

/// <summary>Thrown when label printing fails; triggers the service-level retry/ack-false path.</summary>
public sealed class PrintException : Exception
{
    public PrintException(string message) : base(message) { }
    public PrintException(string message, Exception inner) : base(message, inner) { }
}
