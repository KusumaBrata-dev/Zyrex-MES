namespace ZyrexMES.PrintAgent;

/// <summary>Prints one label for the given payload using the named template.</summary>
public interface ILabelPrinter
{
    /// <param name="payloadJson">Job payload (JSON) handed to the printer.</param>
    /// <param name="templateCode">Template code; implementations resolve it to a template file.</param>
    /// <exception cref="PrintException">Any printing failure must throw so the caller retries/acks false.</exception>
    void Print(string payloadJson, string templateCode);
}
