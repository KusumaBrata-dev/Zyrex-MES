namespace ZyrexMES.PrintAgent;

/// <summary>Prints one label for the given payload using the given template file.</summary>
public interface ILabelPrinter
{
    /// <exception cref="Exception">Any failure must throw so the caller acks the job as failed.</exception>
    void Print(string payloadJson, string templatePath);
}
