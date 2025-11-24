namespace SDHome.Lib.Services
{
    public interface ISignalsService
    {
        Task HandleMqttMessageAsync(string topic, string payload, CancellationToken cancellationToken = default);
    }
}
