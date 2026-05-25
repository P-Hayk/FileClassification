namespace FileClassification.Worker.Settings;

public class WorkerSettings
{
    public int ConcurrencyLimit { get; set; } = 3;
    public int PollIntervalSeconds { get; set; } = 10;
    public int ProgressUpdateIntervalSeconds { get; set; } = 10;
    public int HeartbeatTimeoutSeconds { get; set; } = 20;
}
