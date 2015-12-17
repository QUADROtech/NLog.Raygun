using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mindscape.Raygun4Net;
using MoreLinq;
using NLog.Config;
using NLog.Targets;

namespace NLog.Raygun
{
    [Target("RayGun")]
    public class RayGunTarget : TargetWithLayout
    {
        [RequiredParameter]
        public string ApiKey { get; set; }

        [RequiredParameter]
        public string Tags { get; set; }

        [RequiredParameter]
        public string IgnoreFormFieldNames { get; set; }

        [RequiredParameter]
        public string IgnoreCookieNames { get; set; }

        [RequiredParameter]
        public string IgnoreServerVariableNames { get; set; }

        [RequiredParameter]
        public string IgnoreHeaderNames { get; set; }

        public List<string> IgnoreMessageStringStartsWith { get; set; }

        public List<string> IgnoreMessageStringContains { get; set; }

        public List<string> GlobalDiagnosticContextNames { get; set; }

        public List<string> GlobalDiagnosticContextNamesAsTags { get; set; }

        public List<string> MappedDiagnosticsContextNames { get; set; }

        public List<string> MappedDiagnosticsContextNamesAsTags { get; set; }

        public RayGunTarget()
        {
            GlobalDiagnosticContextNames = new List<string>();
            MappedDiagnosticsContextNames = new List<string>();
            MappedDiagnosticsContextNamesAsTags = new List<string>();
            MappedDiagnosticsContextNamesAsTags = new List<string>();
            IgnoreMessageStringStartsWith = new List<string>();
            IgnoreMessageStringContains = new List<string>();
        }

        [RequiredParameter]
        public bool UseIdentityNameAsUserId { get; set; }

        protected override void Write(LogEventInfo logEvent)
        {
            // If we have a real exception, we can log it as is, otherwise we can take the NLog message and use that.
            if (IsException(logEvent))
            {
                var exception = logEvent.Exception;
                ProcessException(logEvent, exception);
            }
            else if (logEvent.Properties.ContainsKey("Client.exception"))
            {
                var exception = logEvent.Properties["Client.exception"] as Exception;
                ProcessException(logEvent, exception);
            }
            else
            {
                string logMessage = Layout.Render(logEvent);
                // File.AppendAllLines(@"e:\temp\log.txt", logEvent.Properties.Keys.Select(k => k.ToString()));
                // logEvent.Dump();
                RaygunException exception = new RaygunException(logMessage, logEvent.Exception);
                ProcessException(logEvent, exception);
            }
        }

        private void ProcessException(LogEventInfo logEvent, Exception exception)
        {
            var properties = GetDiagnosticsContexts(logEvent);
            var tags = ExtractTagsFromException(exception);
            foreach (var tag in ExtractTagsFromContexts())
            {
                tags.Add(tag);
            }

            RaygunClient raygunClient = CreateRaygunClient();

            if (exception is AggregateException)
            {
                var aggregateException = exception as AggregateException;
                foreach (var innerException in aggregateException.InnerExceptions)
                {
                    if (IgnoreMessage(innerException))
                    {
                        continue;
                    }

                    SendMessage(raygunClient, innerException, tags.ToList(), properties);
                }
            }
            else
            {
                if (IgnoreMessage(exception))
                {
                    return;
                }

                SendMessage(raygunClient, exception, tags.ToList(), properties);
            }
        }

        private bool IgnoreMessage(Exception exception)
        {
            if (IgnoreMessageStringContains.Any(ignore => exception.Message.Contains(ignore)))
            {
                return true;
            }

            if (IgnoreMessageStringStartsWith.Any(ignore => exception.Message.StartsWith(ignore)))
            {
                return true;
            }

            return false;
        }

        private List<string> ExtractTagsFromContexts()
        {
            var tags =
                GlobalDiagnosticContextNamesAsTags.Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(GlobalDiagnosticsContext.Get)
                    .ToList();
            tags.AddRange(
                MappedDiagnosticsContextNamesAsTags.Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(MappedDiagnosticsContext.Get));
            return tags;
        }

        private Dictionary<string, string> GetDiagnosticsContexts(LogEventInfo logEvent)
        {
            string logMessage = Layout.Render(logEvent);
            var properties = new Dictionary<string, string> {{"logMessage", logMessage}};
            logEvent.Properties.ForEach(p => properties.Add(p.Key.ToString(), p.Value.ToString()));

            foreach (var gdc in GlobalDiagnosticContextNames)
            {
                if (GlobalDiagnosticsContext.Contains(gdc))
                {
                    properties.Add(gdc, GlobalDiagnosticsContext.Get(gdc));
                }
            }

            foreach (var mdg in MappedDiagnosticsContextNames)
            {
                if (MappedDiagnosticsContext.Contains(mdg))
                {
                    properties.Add(mdg, MappedDiagnosticsContext.Get(mdg));
                }
            }

            return properties;
        }

        private static bool IsException(LogEventInfo logEvent)
        {
            return logEvent.Exception != null;
            // return logEvent.Parameters.Any() && logEvent.Parameters.FirstOrDefault() != null && logEvent.Parameters.First().GetType() == typeof(Exception);
        }

        private static HashSet<string> ExtractTagsFromException(Exception exception)
        {
            // Try and get tags off the exception data, if they exist
            HashSet<string> tags = new HashSet<string>();
            if (exception.Data["Tags"] != null)
            {
                if (exception.Data["Tags"].GetType() == typeof (List<string>))
                {
                    foreach (var tag in (List<string>) exception.Data["Tags"])
                    {
                        tags.Add(tag);
                    }
                }

                if (exception.Data["Tags"].GetType() == typeof (string[]))
                {
                    foreach (var tag in (string[]) exception.Data["Tags"])
                    {
                        tags.Add(tag);
                    }
                }
            }

            return tags;
        }

        private RaygunClient CreateRaygunClient()
        {
            var client = new RaygunClient(ApiKey);

            client.IgnoreFormFieldNames(SplitValues(IgnoreFormFieldNames));
            client.IgnoreCookieNames(SplitValues(IgnoreCookieNames));
            client.IgnoreHeaderNames(SplitValues(IgnoreHeaderNames));
            client.IgnoreServerVariableNames(SplitValues(IgnoreServerVariableNames));

            return client;
        }

        private void SendMessage(RaygunClient client, Exception exception, IList<string> exceptionTags,
            IDictionary userCustomData)
        {
            if (!string.IsNullOrWhiteSpace(Tags))
            {
                var tags = Tags.Split(',');

                foreach (string tag in tags)
                {
                    exceptionTags.Add(tag);
                }
            }

            if (userCustomData.Contains("Version"))
            {
                client.ApplicationVersion = userCustomData["Version"].ToString();
            }
            else
            {
                client.ApplicationVersion = System.Reflection.Assembly.GetEntryAssembly().GetName().Version.ToString();
            }

            client.AddWrapperExceptions(typeof (AggregateException));
            client.SendInBackground(exception, exceptionTags.Distinct().ToList(), userCustomData);
        }

        private string[] SplitValues(string input)
        {
            if (!string.IsNullOrWhiteSpace(input))
            {
                return input.Split(',');
            }

            return new[] {string.Empty};
        }
    }
}