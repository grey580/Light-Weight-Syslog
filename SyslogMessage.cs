using System;

namespace LightWeightSyslog
{
    internal sealed class SyslogMessage
    {
        public int Sequence { get; set; }
        public DateTime ReceivedAt { get; set; }
        public string SourceIp { get; set; }
        public string SenderTimestamp { get; set; }
        public string HostName { get; set; }
        public string AppName { get; set; }
        public string ProcId { get; set; }
        public string MsgId { get; set; }
        public string MessageText { get; set; }
        public string RawMessage { get; set; }
        public string StructuredData { get; set; }
        public string ParseFormat { get; set; }
        public int Priority { get; set; }
        public int Severity { get; set; }
        public int Facility { get; set; }

        public string ReceivedAtDisplay => ReceivedAt.ToString("yyyy-MM-dd HH:mm:ss");

        public string SeverityName => SyslogParser.GetSeverityName(Severity);

        public string FacilityName => SyslogParser.GetFacilityName(Facility);

        public string Summary => string.IsNullOrWhiteSpace(MessageText) ? RawMessage : MessageText;

        public string DetailText =>
            "Received: " + ReceivedAtDisplay + Environment.NewLine +
            "Source IP: " + SourceIp + Environment.NewLine +
            "Format: " + ParseFormat + Environment.NewLine +
            "Sender timestamp: " + SenderTimestamp + Environment.NewLine +
            "Host: " + HostName + Environment.NewLine +
            "Facility: " + FacilityName + " (" + Facility + ")" + Environment.NewLine +
            "Severity: " + SeverityName + " (" + Severity + ")" + Environment.NewLine +
            "App: " + AppName + Environment.NewLine +
            "Process ID: " + ProcId + Environment.NewLine +
            "Message ID: " + MsgId + Environment.NewLine +
            "Structured data: " + StructuredData + Environment.NewLine +
            Environment.NewLine +
            "Message:" + Environment.NewLine +
            Summary + Environment.NewLine +
            Environment.NewLine +
            "Raw:" + Environment.NewLine +
            RawMessage;
    }
}
