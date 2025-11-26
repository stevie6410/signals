namespace SDHome.Lib.Models
{
    public class MqttOptions
    {
        public bool Enabled { get; set; } = true;
        public string Host { get; set; } = "";
        public int Port { get; set; }
        public string TopicFilter { get; set; } = "sdhome/#";
    }

    public class PostgresOptions
    {
        public string ConnectionString { get; set; } = "";
    }

    public class MsSQLOptions
    {
        public string ConnectionString { get; set; } = "";
    }


    public class WebhookOptions
    {
        public string? Main { get; set; }
        public string? Test { get; set; }
    }

    public class LoggingOptions
    {
        public string SeqUrl { get; set; } = "";
        public string MinimumLevel { get; set; } = "Information";
    }

    public class MetricsOptions
    {
        public int Port { get; set; } = 5050;
    }

}
