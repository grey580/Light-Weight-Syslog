using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LightWeightSyslog
{
    internal static class SyslogParser
    {
        private static readonly Regex Rfc5424Regex = new Regex(
            @"^(?<version>\d+)\s+(?<timestamp>\S+)\s+(?<host>\S+)\s+(?<app>\S+)\s+(?<proc>\S+)\s+(?<msgid>\S+)\s+(?<structured>(?:-|\[[^\r\n]*\]))(?:\s(?<message>.*))?$",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex Rfc3164Regex = new Regex(
            @"^(?<timestamp>[A-Z][a-z]{2}\s+\d{1,2}\s+\d{2}:\d{2}:\d{2})\s+(?<host>\S+)\s*(?<message>.*)$",
            RegexOptions.Compiled);

        private static readonly Regex AppPrefixRegex = new Regex(
            @"^(?<app>[\w\.\-\/]+)(?:\[(?<proc>[^\]]+)\])?:\s*(?<message>.*)$",
            RegexOptions.Compiled);

        private static readonly string[] SeverityNames =
        {
            "Emergency",
            "Alert",
            "Critical",
            "Error",
            "Warning",
            "Notice",
            "Informational",
            "Debug"
        };

        private static readonly string[] FacilityNames =
        {
            "Kernel",
            "User",
            "Mail",
            "Daemon",
            "Auth",
            "Syslog",
            "Printer",
            "News",
            "UUCP",
            "Clock",
            "AuthPriv",
            "FTP",
            "NTP",
            "Audit",
            "Alert",
            "Clock2",
            "Local0",
            "Local1",
            "Local2",
            "Local3",
            "Local4",
            "Local5",
            "Local6",
            "Local7"
        };

        public static SyslogMessage Parse(string rawMessage, string sourceIp, DateTime receivedAt, int sequence)
        {
            var message = new SyslogMessage
            {
                Sequence = sequence,
                ReceivedAt = receivedAt,
                SourceIp = sourceIp ?? string.Empty,
                RawMessage = (rawMessage ?? string.Empty).TrimEnd('\0', '\r', '\n'),
                SenderTimestamp = string.Empty,
                HostName = string.Empty,
                AppName = string.Empty,
                ProcId = string.Empty,
                MsgId = string.Empty,
                MessageText = string.Empty,
                StructuredData = string.Empty,
                ParseFormat = "Raw",
                Priority = -1,
                Severity = -1,
                Facility = -1
            };

            var body = message.RawMessage;
            if (TryReadPriority(ref body, out var priority))
            {
                message.Priority = priority;
                message.Severity = priority % 8;
                message.Facility = priority / 8;
            }

            var rfc5424Match = Rfc5424Regex.Match(body);
            if (rfc5424Match.Success)
            {
                message.ParseFormat = "RFC5424";
                message.SenderTimestamp = GetValue(rfc5424Match, "timestamp");
                message.HostName = NormalizeNil(GetValue(rfc5424Match, "host"));
                message.AppName = NormalizeNil(GetValue(rfc5424Match, "app"));
                message.ProcId = NormalizeNil(GetValue(rfc5424Match, "proc"));
                message.MsgId = NormalizeNil(GetValue(rfc5424Match, "msgid"));
                message.StructuredData = NormalizeNil(GetValue(rfc5424Match, "structured"));
                message.MessageText = NormalizeNil(GetValue(rfc5424Match, "message"));
                return message;
            }

            var rfc3164Match = Rfc3164Regex.Match(body);
            if (rfc3164Match.Success)
            {
                message.ParseFormat = "RFC3164";
                message.SenderTimestamp = GetValue(rfc3164Match, "timestamp");
                message.HostName = NormalizeNil(GetValue(rfc3164Match, "host"));
                var text = NormalizeNil(GetValue(rfc3164Match, "message"));
                ExtractAppPrefix(text, message);
                return message;
            }

            message.MessageText = body;
            return message;
        }

        public static string GetSeverityName(int severity)
        {
            return severity >= 0 && severity < SeverityNames.Length
                ? SeverityNames[severity]
                : "Unknown";
        }

        public static string GetFacilityName(int facility)
        {
            return facility >= 0 && facility < FacilityNames.Length
                ? FacilityNames[facility]
                : "Unknown";
        }

        public static int GetMinimumSeverityForFilter(string filterName)
        {
            switch (filterName)
            {
                case "Warning and above":
                    return 4;
                case "Error and above":
                    return 3;
                case "Critical and above":
                    return 2;
                case "Informational and above":
                    return 6;
                default:
                    return int.MaxValue;
            }
        }

        private static bool TryReadPriority(ref string body, out int priority)
        {
            priority = -1;
            if (string.IsNullOrEmpty(body) || body[0] != '<')
            {
                return false;
            }

            var end = body.IndexOf('>');
            if (end < 0)
            {
                return false;
            }

            var priText = body.Substring(1, end - 1);
            if (!int.TryParse(priText, NumberStyles.Integer, CultureInfo.InvariantCulture, out priority))
            {
                return false;
            }

            body = body.Substring(end + 1).TrimStart();
            return true;
        }

        private static void ExtractAppPrefix(string messageText, SyslogMessage message)
        {
            var appMatch = AppPrefixRegex.Match(messageText ?? string.Empty);
            if (appMatch.Success)
            {
                message.AppName = NormalizeNil(GetValue(appMatch, "app"));
                message.ProcId = NormalizeNil(GetValue(appMatch, "proc"));
                message.MessageText = NormalizeNil(GetValue(appMatch, "message"));
                return;
            }

            message.MessageText = messageText ?? string.Empty;
        }

        private static string GetValue(Match match, string groupName)
        {
            return match.Groups[groupName].Success ? match.Groups[groupName].Value : string.Empty;
        }

        private static string NormalizeNil(string value)
        {
            return string.Equals(value, "-", StringComparison.Ordinal) ? string.Empty : (value ?? string.Empty);
        }
    }
}
