namespace OrderProcessing.Shared.Exceptions;

public class DataFileCorruptedException : Exception
{
    public string FileName { get; }

    public DataFileCorruptedException(string fileName, string message)
        : base($"[File: {fileName}] {message}")
    {
        FileName = fileName;
    }

    public DataFileCorruptedException(string fileName, string message, Exception innerException)
        : base($"[File: {fileName}] {message}", innerException)
    {
        FileName = fileName;
    }
}

