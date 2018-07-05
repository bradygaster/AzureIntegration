// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DiagnosticAdapter;
using Microsoft.Extensions.Logging;

[assembly: HostingStartup(typeof(Microsoft.AspNetCore.ApplicationInsights.VisualStudio.HostingStartup.ApplicationInsightsVisualStudioHostingStartup))]

// To be able to build as <OutputType>Exe</OutputType>
internal class Program { public static void Main() { } }

namespace Microsoft.AspNetCore.ApplicationInsights.VisualStudio.HostingStartup
{
    /// <summary>
    /// A dynamic Application Insights lightup experience
    /// </summary>
    public class ApplicationInsightsVisualStudioHostingStartup : IHostingStartup
    {
        /// <summary>
        /// Calls UseApplicationInsights
        /// </summary>
        /// <param name="builder"></param>
        public void Configure(IWebHostBuilder builder)
        {
            builder.ConfigureServices(InitializeServices);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> associated with the application.</param>
        private void InitializeServices(IServiceCollection services)
        {
            // We treat ApplicationInsightsDebugLogger as a marker that AI services were added to service collection
            if (!services.Any(service => service.ServiceType.Name == "ApplicationInsightsDebugLogger"))
            {
                services.AddSingleton<IStartupFilter, ApplicationInsightsVisualStudioStartupFilter>();
            }
        }
    }
    internal static class AspNetCoreAIConstants
    {
        public static readonly string DefaultTelemetryNamePrefix = "Microsoft.AspNetCore.";
        public static readonly string DefaultDeveloperTelemetryNamePrefix = "Microsoft.AspNetCore.Dev";
    }

    internal interface JValue
    {
        string ToJson();
    }

    internal class JObject : JValue
    {
        public IList<JProperty> Properties { get; } = new List<JProperty>();

        public string ToJson()
        {
            if (Properties.Count == 0)
            {
                return "{}";
            }

            var firstValue = true;
            var sb = new StringBuilder();
            sb.Append('{');
            foreach (var property in Properties)
            {
                if (!firstValue)
                {
                    sb.Append(',');
                }
                if (firstValue)
                {
                    firstValue = false;
                }
                sb.Append(property.ToJson());
            }
            sb.Append('}');

            return sb.ToString();
        }
    }

    internal class JArray : JValue
    {
        public IList<JValue> Values { get; } = new List<JValue>();

        public string ToJson()
        {
            if (Values.Count == 0)
            {
                return "[]";
            }

            var firstValue = true;
            var sb = new StringBuilder();
            sb.Append('[');
            foreach (var property in Values)
            {
                if (!firstValue)
                {
                    sb.Append(',');
                }
                if (firstValue)
                {
                    firstValue = false;
                }
                sb.Append(property.ToJson());
            }
            sb.Append(']');

            return sb.ToString();
        }
    }

    internal class JProperty : JValue
    {
        public JProperty(string name, JValue value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; }
        public JValue Value { get; }

        public string ToJson()
        {
            return $@"""{Name}"":{Value.ToJson()}";
        }
    }

    internal class JString : JValue
    {
        public JString(string value)
        {
            Value = value;
        }

        public string Value { get; set; }

        public string ToJson()
        {
            return $@"""{Value}""".Replace("\\", "\\\\");
        }
    }

    internal class JLiteral : JValue
    {
        public JLiteral(string value)
        {
            Value = value;
        }

        public string Value { get; set; }

        public string ToJson()
        {
            return Value;
        }
    }

    internal interface IAspNetCoreAITelemetry
    {
        JObject JsonData { get; }

        string ToJson();
    }

    internal abstract class AspNetCoreAITelemetry : IAspNetCoreAITelemetry
    {
        public AspNetCoreAITelemetry(string name, string instrumentationKey, bool developerMode)
        {
            var fullname = developerMode
                ? AspNetCoreAIConstants.DefaultDeveloperTelemetryNamePrefix
                : AspNetCoreAIConstants.DefaultTelemetryNamePrefix;
            if (!string.IsNullOrEmpty(instrumentationKey))
            {
                fullname += $".{instrumentationKey.Replace("-", string.Empty)}";
            }
            fullname += $".{name}";
            JsonData.Properties.Add(new JProperty("name", new JString(fullname)));

            var startTime = Activity.Current.StartTimeUtc;
            JsonData.Properties.Add(new JProperty("time", new JString(startTime.ToString("o", CultureInfo.InvariantCulture))));

            if (!string.IsNullOrEmpty(instrumentationKey))
            {
                JsonData.Properties.Add(new JProperty("iKey", new JString(instrumentationKey)));
            }
        }

        public JObject JsonData { get; } = new JObject();

        public virtual string ToJson()
        {
            return JsonData.ToJson();
        }
    }

    internal class AspNetCoreAIRequestTelemetry : AspNetCoreAITelemetry
    {
        private bool _developerMode;
        private string _environment;
        private HttpContext _httpContext;

        public AspNetCoreAIRequestTelemetry(string instrumentationKey, bool developerMode, string environment, HttpContext httpContext)
            : base("Request", instrumentationKey, developerMode)
        {
            _developerMode = developerMode;
            _environment = environment;
            _httpContext = httpContext;
        }

        public override string ToJson()
        {
            var request = _httpContext.Request;
            var response = _httpContext.Response;
            var method = request.Method;
            var url = $"{request.Scheme}://{request.Host}/";
            var path = $"{request.PathBase}{request.Path}";
            var statusCode = response.StatusCode;
            var success = statusCode >= 200 && statusCode < 300;
            var currentActivity = Activity.Current;
            var duration = currentActivity.Duration;
            var operationId = currentActivity.Id;

            var properties = new JObject();
            properties.Properties.Add(new JProperty("DeveloperMode", new JString((_developerMode ? "true" : "false"))));
            properties.Properties.Add(new JProperty("AspNetCoreEnvironment", new JString(_environment)));

            var baseData = new JObject();
            baseData.Properties.Add(new JProperty("ver", new JLiteral("2")));
            baseData.Properties.Add(new JProperty("id", new JString(operationId)));
            baseData.Properties.Add(new JProperty("name", new JString($"{method} {path}")));
            baseData.Properties.Add(new JProperty("duration", new JString(duration.ToString())));
            baseData.Properties.Add(new JProperty("success", new JLiteral((success ? "true" : "false"))));
            baseData.Properties.Add(new JProperty("responseCode", new JString(statusCode.ToString())));
            baseData.Properties.Add(new JProperty("url", new JString(url)));
            baseData.Properties.Add(new JProperty("properties", properties));

            var data = new JObject();
            data.Properties.Add(new JProperty("baseType", new JString("RequestData")));
            data.Properties.Add(new JProperty("baseData", baseData));

            JsonData.Properties.Add(new JProperty("data", data));

            return JsonData.ToJson();
        }
    }

    internal class AspNetCoreAIExceptionTelemetry : AspNetCoreAITelemetry
    {
        private bool _developerMode;
        private string _environment;
        private Exception _exception;

        public AspNetCoreAIExceptionTelemetry(string instrumentationKey, bool developerMode, string environment, Exception exception)
            : base("Exception", instrumentationKey, developerMode)
        {
            _developerMode = developerMode;
            _environment = environment;
            _exception = exception;
        }

        public override string ToJson()
        {
            var properties = new JObject();
            properties.Properties.Add(new JProperty("handledAt", new JString("Platform")));
            properties.Properties.Add(new JProperty("AspNetCoreEnvironment", new JString(_environment)));
            properties.Properties.Add(new JProperty("DeveloperMode", new JString((_developerMode ? "true" : "false"))));

            var parsedStack = new JArray();
            var stackTrace = new StackTrace(_exception, true);
            for (var i = 0; i < stackTrace.FrameCount; i++)
            {
                var stackFrame = stackTrace.GetFrame(i);
                var method = stackFrame.GetMethod();
                var fullMethodName = method.DeclaringType == null ? method.Name : $"{method.DeclaringType.FullName}.{method.Name}";
                var fileName = stackFrame.GetFileName();
                var lineNumber = stackFrame.GetFileLineNumber();

                var parsedStackObject = new JObject();
                parsedStackObject.Properties.Add(new JProperty("level", new JLiteral(i.ToString())));
                parsedStackObject.Properties.Add(new JProperty("method", new JString(fullMethodName)));
                parsedStackObject.Properties.Add(new JProperty("assembly", new JString(method.Module.Assembly.FullName)));
                if (!string.IsNullOrEmpty(fileName))
                {
                    parsedStackObject.Properties.Add(new JProperty("fileName", new JString(fileName)));
                }
                if (lineNumber != 0) // 0 is unavailable
                {
                    parsedStackObject.Properties.Add(new JProperty("line", new JLiteral(lineNumber.ToString())));
                }

                parsedStack.Values.Add(parsedStackObject);
            }

            var exceptions = new JObject();
            exceptions.Properties.Add(new JProperty("id", new JLiteral(_exception.GetHashCode().ToString())));
            exceptions.Properties.Add(new JProperty("typeName", new JString(_exception.GetType().FullName)));
            exceptions.Properties.Add(new JProperty("message", new JString(_exception.Message)));
            exceptions.Properties.Add(new JProperty("hasFullStack", new JLiteral("true")));
            exceptions.Properties.Add(new JProperty("parsedStack", parsedStack));

            var exceptionArray = new JArray();
            exceptionArray.Values.Add(exceptions);

            var baseData = new JObject();
            baseData.Properties.Add(new JProperty("ver", new JLiteral("2")));
            baseData.Properties.Add(new JProperty("properties", properties));
            baseData.Properties.Add(new JProperty("exceptions", exceptionArray));

            var data = new JObject();
            data.Properties.Add(new JProperty("baseType", new JString("ExceptionData")));
            data.Properties.Add(new JProperty("baseData", baseData));

            JsonData.Properties.Add(new JProperty("data", data));

            return JsonData.ToJson();
        }
    }

    internal class AspNetCoreAITraceTelemetry : AspNetCoreAITelemetry
    {
        private readonly bool _developerMode;
        private readonly string _environment;
        private readonly string _categoryName;
        private readonly string _message;
        private readonly LogLevel _logLevel;
        private readonly object _state;

        public AspNetCoreAITraceTelemetry(string instrumentationKey, bool developerMode, string environment, string categoryName, string message, LogLevel logLevel, object state)
            : base("Message", instrumentationKey, developerMode)
        {
            _developerMode = developerMode;
            _environment = environment;
            _categoryName = categoryName;
            _message = message;
            _logLevel = logLevel;
            _state = state;
        }

        public override string ToJson()
        {
            var properties = new JObject();
            properties.Properties.Add(new JProperty("DeveloperMode", new JString((_developerMode ? "true" : "false"))));
            properties.Properties.Add(new JProperty("AspNetCoreEnvironment", new JString(_environment)));
            properties.Properties.Add(new JProperty("CategoryName", new JString(_categoryName)));

            var stateDictionary = _state as IReadOnlyList<KeyValuePair<string, object>>;
            foreach (var item in stateDictionary)
            {
                properties.Properties.Add(new JProperty(item.Key, new JString(Convert.ToString(item.Value, CultureInfo.InvariantCulture))));
            }

            var baseData = new JObject();
            baseData.Properties.Add(new JProperty("ver", new JLiteral("2")));
            baseData.Properties.Add(new JProperty("message", new JString(_message)));
            baseData.Properties.Add(new JProperty("severityLevel", new JString(GetSeverityLevel(_logLevel))));
            baseData.Properties.Add(new JProperty("properties", properties));

            var data = new JObject();
            data.Properties.Add(new JProperty("baseType", new JString("MessageData")));
            data.Properties.Add(new JProperty("baseData", baseData));

            JsonData.Properties.Add(new JProperty("data", data));

            return JsonData.ToJson();
        }

        private string GetSeverityLevel(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Critical:
                    return "Critical";
                case LogLevel.Error:
                    return "Error";
                case LogLevel.Warning:
                    return "Warning";
                case LogLevel.Information:
                    return "Information";
                case LogLevel.Debug:
                case LogLevel.Trace:
                default:
                    return "Verbose";
            }
        }
    }

    internal class AspNetCoreAIOptions
    {
        public string TelemetryPrefix { get; } = "Application Insights Telemetry: ";
        public string InstrumentationKey { get; }
        public string Environment { get; }
        public bool DeveloperMode { get; }

        public AspNetCoreAIOptions(IConfiguration configuration, IHostingEnvironment env)
        {
            Environment = env.EnvironmentName;
            InstrumentationKey = configuration["ApplicationInsights:InstrumentationKey"];
            if (string.IsNullOrEmpty(InstrumentationKey))
            {
                TelemetryPrefix = "Application Insights Telemetry (unconfigured): ";
            }

            bool developerMode = false;
            Boolean.TryParse(configuration["ApplicationInsights:TelemetryChannel:DeveloperMode"], out developerMode);
            if (Debugger.IsAttached)
            {
                developerMode = true;
            }
            DeveloperMode = developerMode;
        }
    }

    internal class AIVSDiagnosticsAdapter
    {
        private readonly AspNetCoreAIOptions _options;

        public AIVSDiagnosticsAdapter(AspNetCoreAIOptions options)
        {
            _options = options;
        }

        [DiagnosticName("Microsoft.AspNetCore.Hosting.HttpRequestIn")]
        public void OnRequest(HttpContext httpContext)
        {
            // No-op
        }

        [DiagnosticName("Microsoft.AspNetCore.Hosting.HttpRequestIn.Start")]
        public void OnRequestStart(HttpContext httpContext)
        {
            httpContext.Features.Set<IAspNetCoreAITelemetry>(new AspNetCoreAIRequestTelemetry(_options.InstrumentationKey, _options.DeveloperMode, _options.Environment, httpContext));
        }

        [DiagnosticName("Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop")]
        public void OnRequestStop(HttpContext httpContext)
        {
            Debug.WriteLine($"{_options.TelemetryPrefix}{httpContext.Features.Get<IAspNetCoreAITelemetry>().ToJson()}");
        }

        [DiagnosticName("Microsoft.AspNetCore.Hosting.UnhandledException")]
        public void OnHostingUnhandledException(Exception exception)
            => OnException(exception);

        [DiagnosticName("Microsoft.AspNetCore.Diagnostics.UnhandledException")]
        public void OnDiagnosticsUnhandledException(Exception exception)
            => OnException(exception);

        [DiagnosticName("Microsoft.AspNetCore.Diagnostics.HandledException")]
        public void OnDiagnosticsHandledException(Exception exception)
            => OnException(exception);

        private void OnException(Exception exception)
        {
            Debug.WriteLine($"{_options.TelemetryPrefix}{new AspNetCoreAIExceptionTelemetry(_options.InstrumentationKey, _options.DeveloperMode, _options.Environment, exception).ToJson()}");
        }
    }

    internal class AspNetCoreAILogger : ILogger
    {
        private readonly AspNetCoreAIOptions _options;
        private readonly string _categoryName;

        internal AspNetCoreAILogger(string categoryname, AspNetCoreAIOptions options)
        {
            _categoryName = categoryname;
            _options = options;
        }

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            Debug.WriteLine($"{_options.TelemetryPrefix}{new AspNetCoreAITraceTelemetry(_options.InstrumentationKey, _options.DeveloperMode, _options.Environment, _categoryName, formatter(state, exception), logLevel, state).ToJson()}");
        }
    }

    internal class AspNetCoreAILoggerProvider : ILoggerProvider
    {
        private readonly AspNetCoreAIOptions _options;

        public AspNetCoreAILoggerProvider(AspNetCoreAIOptions options)
        {
            _options = options;
        }

        public ILogger CreateLogger(string categoryName)
            => new AspNetCoreAILogger(categoryName, _options);

        public void Dispose() { }
    }

    internal static class AspNetCoreAILoggerFactoryExtensions
    {
        public static ILoggerFactory AddAspNetCoreAI(this ILoggerFactory loggerFactory, AspNetCoreAIOptions options)
        {
            loggerFactory.AddProvider(new AspNetCoreAILoggerProvider(options));
            return loggerFactory;
        }
    }

    internal class ApplicationInsightsVisualStudioStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return builder =>
            {
                var configuration = builder.ApplicationServices.GetRequiredService<IConfiguration>();
                var env = builder.ApplicationServices.GetRequiredService<IHostingEnvironment>();
                var loggerFactory = builder.ApplicationServices.GetRequiredService<ILoggerFactory>();
                var diagnosticListener = builder.ApplicationServices.GetRequiredService<DiagnosticListener>();

                var options = new AspNetCoreAIOptions(configuration, env);
                diagnosticListener.SubscribeWithAdapter(new AIVSDiagnosticsAdapter(options));

                if (string.IsNullOrEmpty(options.InstrumentationKey) && Debugger.IsAttached)
                {
                    loggerFactory.AddAspNetCoreAI(options);
                }

                next(builder);
            };
        }
    }
}
