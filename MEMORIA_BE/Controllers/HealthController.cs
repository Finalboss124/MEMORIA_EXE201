using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MEMORIA_BE.Configurations;

namespace MEMORIA_BE.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly CloudinarySettings _cloudinarySettings;

    public HealthController(IOptions<CloudinarySettings> cloudinarySettings)
    {
        _cloudinarySettings = cloudinarySettings.Value;
    }

    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok" });

    [HttpGet("cloudinary")]
    public IActionResult Cloudinary() => Ok(new
    {
        cloudNameSet = !string.IsNullOrWhiteSpace(_cloudinarySettings.CloudName),
        apiKeySet = !string.IsNullOrWhiteSpace(_cloudinarySettings.ApiKey),
        apiSecretSet = !string.IsNullOrWhiteSpace(_cloudinarySettings.ApiSecret),
        uploadPresetSet = !string.IsNullOrWhiteSpace(_cloudinarySettings.UploadPreset),
        uploadPresetLength = string.IsNullOrWhiteSpace(_cloudinarySettings.UploadPreset) ? 0 : _cloudinarySettings.UploadPreset.Length,
        folder = _cloudinarySettings.Folder,
        bypassUploadForLocalTesting = _cloudinarySettings.BypassUploadForLocalTesting
    });
}
