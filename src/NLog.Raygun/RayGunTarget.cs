using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
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

    [RequiredParameter]
    public List<string> GlobalDiagnosticContextNames { get; set; }

    [RequiredParameter]
    public List<string> MappedDiagnosticsContextNames { get; set; }

    public RayGunTarget()
    {
        GlobalDiagnosticContextNames = new List<string>();
        MappedDiagnosticsContextNames = new List<string>();
    }

    [RequiredParameter]
    public bool UseIdentityNameAsUserId { get; set; }

    protected override void Write(LogEventInfo logEvent)
    {
      var properties = GetDiagnosticsContexts(logEvent);

      // If we have a real exception, we can log it as is, otherwise we can take the NLog message and use that.
      if (IsException(logEvent))
      {
        Exception exception = (Exception)logEvent.Parameters.First();

        List<string> tags = ExtractTagsFromException(exception);

        RaygunClient raygunClient = CreateRaygunClient();

        if (exception is AggregateException)
        {
            var aggregateException = exception as AggregateException;
            foreach (var innerException in aggregateException.InnerExceptions)
            {
                SendMessage(raygunClient, innerException, tags, properties);
            }
        }
        else
        {
            SendMessage(raygunClient, exception, tags, properties);
        }
      }
      else
      {
        string logMessage = Layout.Render(logEvent);

        RaygunException exception = new RaygunException(logMessage, logEvent.Exception);
        RaygunClient client = CreateRaygunClient();

        SendMessage(client, exception, new List<string>(), properties);
      }
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
      return logEvent.Parameters.Any() && logEvent.Parameters.FirstOrDefault() != null && logEvent.Parameters.First().GetType() == typeof(Exception);
    }

    private static List<string> ExtractTagsFromException(Exception exception)
    {
      // Try and get tags off the exception data, if they exist
      List<string> tags = new List<string>();
      if (exception.Data["Tags"] != null)
      {
        if (exception.Data["Tags"].GetType() == typeof(List<string>))
        {
          tags.AddRange((List<string>)exception.Data["Tags"]);
        }

        if (exception.Data["Tags"].GetType() == typeof(string[]))
        {
          tags.AddRange(((string[])exception.Data["Tags"]).ToList());
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

    private void SendMessage(RaygunClient client, Exception exception, IList<string> exceptionTags, IDictionary userCustomData)
    {
      if (!string.IsNullOrWhiteSpace(Tags))
      {
        var tags = Tags.Split(',');

        foreach (string tag in tags)
        {
          exceptionTags.Add(tag);
        }
      }

      client.SendInBackground(exception, exceptionTags, userCustomData);
    }

    private string[] SplitValues(string input)
    {
      if (!string.IsNullOrWhiteSpace(input))
      {
        return input.Split(',');
      }

      return new[] { string.Empty };
    }
  }
}