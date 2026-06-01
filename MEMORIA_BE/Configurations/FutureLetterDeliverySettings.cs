namespace MEMORIA_BE.Configurations;

public sealed class FutureLetterDeliverySettings
{
    public int PollIntervalSeconds { get; set; } = 60;

    public int BatchSize { get; set; } = 25;
}
