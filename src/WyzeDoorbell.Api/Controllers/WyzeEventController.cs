using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace WyzeDoorbell.Api.Controllers
{
    // C# Model to match the JSON payload sent by the Python script
    public class DoorbellEventPayload
    {
        // Example: 2005 (Doorbell Button Press)
        [Required]
        public int EventType { get; set; }

        // MAC address of the Wyze device
        [Required]
        [RegularExpression(@"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$|^[0-9A-Fa-f]{12}$", ErrorMessage = "Invalid MAC Address format.")]
        public string DeviceMac { get; set; } = string.Empty;

        // The UTC timestamp from the event, sent as an ISO 8601 string
        [Required]
        public string EventTimeUtc { get; set; } = string.Empty;

        // A descriptive message, e.g., "Doorbell Button Pressed"
        public string Message { get; set; } = string.Empty;
    }

    [ApiController]
    [Route("api/[controller]")]
    public class WyzeController : ControllerBase
    {
        private readonly ILogger<WyzeController> _logger;

        public WyzeController(ILogger<WyzeController> logger)
        {
            _logger = logger;
        }

        [HttpPost("doorbell")]
        [Consumes("application/json")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult DoorbellPress([FromBody] DoorbellEventPayload payload)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogError("Received invalid payload from Python bridge.");
                return BadRequest(ModelState);
            }

            _logger.LogInformation(
                "[{Time}] Doorbell Event Received: MAC={Mac}, Type={Type}, Message='{Msg}'",
                DateTime.UtcNow.ToString("o"),
                payload.DeviceMac,
                payload.EventType,
                payload.Message
            );

            if (DateTimeOffset.TryParse(payload.EventTimeUtc, out var eventTime))
            {
                _logger.LogInformation($"Event Timestamp (Parsed): {eventTime.ToLocalTime()}");
                // --- YOUR APPLICATION LOGIC GOES HERE ---
            }
            else
            {
                _logger.LogWarning($"Could not parse EventTimeUtc: {payload.EventTimeUtc}");
            }

            return Ok(new { status = "Success", received = payload.DeviceMac });
        }
    }
}