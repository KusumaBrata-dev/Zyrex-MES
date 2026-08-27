namespace ZyrexMES.Api.Modules.Insights;

public class InsightsOptions
{
    public double MinYieldPercent { get; set; } = 85;
    public double YieldDropPercent { get; set; } = 20;
    public int NgSpikePerHour { get; set; } = 10;
    public int EvaluationIntervalMinutes { get; set; } = 5;
}
