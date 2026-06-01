namespace MEMORIA_BE.Configurations;

public sealed class CloudinarySettings
{
    public string CloudName { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string ApiSecret { get; set; } = string.Empty;

    public string UploadPreset { get; set; } = string.Empty;

    public string Folder { get; set; } = "memoria/future-letters";

    public bool BypassUploadForLocalTesting { get; set; }
}
