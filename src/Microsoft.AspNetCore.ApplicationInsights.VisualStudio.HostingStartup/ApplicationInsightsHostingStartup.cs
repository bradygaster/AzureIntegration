// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DiagnosticAdapter;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

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
        public static readonly JsonSerializerSettings JsonSerializationSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };
    }

    internal class AspNetCoreAITelemetryPropertiesConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
            => objectType == typeof(AspNetCoreAITelemetryProperties);

        public override bool CanRead => false;

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) => throw new NotImplementedException();

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var properties = value as AspNetCoreAITelemetryProperties;
            writer.WriteStartObject();

            foreach (var property in value.GetType().GetProperties())
            {
                if (property.CanRead && property.GetType() == typeof(string))
                {
                    var propertyName = property.Name;
                    var propertyValue = property.GetValue(value) as string;

                    if (!string.IsNullOrEmpty(propertyValue))
                    {
                        writer.WritePropertyName(propertyName);
                        writer.WriteValue(propertyValue);
                    }
                }
            }

            if (properties.MessageMetadata != null)
            {
                foreach (var metadata in properties.MessageMetadata)
                {
                    writer.WritePropertyName(metadata.Key);
                    writer.WriteValue(Convert.ToString(metadata.Value, CultureInfo.InvariantCulture));
                }
            }
            writer.WriteEndObject();
        }
    }

    [JsonConverter(typeof(AspNetCoreAITelemetryPropertiesConverter))]
    internal class AspNetCoreAITelemetryProperties
    {
        public string handledAt { get; internal set; }
        public string DeveloperMode { get; internal set; }
        public string AspNetCoreEnvironment { get; internal set; }
        public string CategoryName { get; internal set; }

        public IEnumerable<KeyValuePair<string, object>> MessageMetadata { get; internal set; }
    }

    internal class AspNetCoreAITelemetryStackFrame
    {
        public int Level { get; internal set; }
        public string Method { get; internal set; }
        public string Assembly { get; internal set; }
        public string FileName { get; internal set; }
        public int? Line { get; internal set; }
    }

    internal class AspNetCoreAITelemetryException
    {
        public int Id { get; internal set; }
        public string TypeName { get; internal set; }
        public string Message { get; internal set; }
        public bool HasFullStack { get; internal set; }
        public AspNetCoreAITelemetryStackFrame[] ParsedStack { get; internal set; }
    }

    internal class AspNetCoreAITelemetryBaseData
    {
        public int Ver { get; internal set; }
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public string Duration { get; internal set; }
        public bool Success { get; internal set; }
        public string ResponseCode { get; internal set; }
        public string Url { get; internal set; }
        public string Message { get; internal set; }
        public string SeverityLevel { get; internal set; }
        public AspNetCoreAITelemetryProperties Properties { get; internal set; }
        public AspNetCoreAITelemetryException Exceptions { get; internal set; }
    }

    internal class AspNetCoreAITelemetryData
    {
        public string BaseType { get; internal set; }
        public AspNetCoreAITelemetryBaseData BaseData { get; internal set; }
    }

    internal abstract class AspNetCoreAITelemetry
    {
        public string Name { get; internal set; }
        public DateTime Time { get; internal set; }
        public string InstrumentationKey { get; internal set; }
        public AspNetCoreAITelemetryData Data { get; internal set; }

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
            Name = fullname;
            Time = Activity.Current?.StartTimeUtc ?? DateTime.UtcNow;
            InstrumentationKey = instrumentationKey;
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

        public override string ToString()
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

            Data = new AspNetCoreAITelemetryData
            {
                BaseType = "RequestData",
                BaseData = new AspNetCoreAITelemetryBaseData
                {
                    Ver = 2,
                    Id = operationId,
                    Name = $"{method} {path}",
                    Duration = duration.ToString(),
                    Success = success,
                    ResponseCode = statusCode.ToString(),
                    Url = url,
                    Properties = new AspNetCoreAITelemetryProperties
                    {
                        DeveloperMode = _developerMode ? "true" : "false",
                        AspNetCoreEnvironment = _environment
                    }
                }
            };

            return JsonConvert.SerializeObject(this, AspNetCoreAIConstants.JsonSerializationSettings);
        }
    }

    internal class AspNetCoreAIExceptionTelemetry : AspNetCoreAITelemetry
    {
        public AspNetCoreAIExceptionTelemetry(string instrumentationKey, bool developerMode, string environment, Exception exception)
            : base("Exception", instrumentationKey, developerMode)
        {
            var parsedStack = new List<AspNetCoreAITelemetryStackFrame>();
            var stackTrace = new StackTrace(exception, true);

            for (var i = 0; i < stackTrace.FrameCount; i++)
            {
                var stackFrame = stackTrace.GetFrame(i);
                var method = stackFrame.GetMethod();
                var fullMethodName = method.DeclaringType == null ? method.Name : $"{method.DeclaringType.FullName}.{method.Name}";
                var fileName = stackFrame.GetFileName();
                var lineNumber = stackFrame.GetFileLineNumber();

                var parsedStackFrame = new AspNetCoreAITelemetryStackFrame
                {
                    Level = i,
                    Method = fullMethodName,
                    Assembly = method.Module.Assembly.FullName,
                    FileName = fileName?.Replace("\\", "\\\\")
                };

                if (lineNumber != 0) // 0 is unavailable
                {
                    parsedStackFrame.Line = lineNumber;
                }

                parsedStack.Add(parsedStackFrame);
            }

            Data = new AspNetCoreAITelemetryData
            {
                BaseType = "ExceptionData",
                BaseData = new AspNetCoreAITelemetryBaseData
                {
                    Ver = 2,
                    Properties = new AspNetCoreAITelemetryProperties
                    {
                        handledAt = "Platform",
                        DeveloperMode = developerMode ? "true" : "false",
                        AspNetCoreEnvironment = environment
                    },
                    Exceptions = new AspNetCoreAITelemetryException
                    {
                        Id = exception.GetHashCode(),
                        TypeName = exception.GetType().FullName,
                        Message = exception.Message,
                        HasFullStack = true,
                        ParsedStack = parsedStack.ToArray()
                    }
                }
            };
        }

        public override string ToString() => JsonConvert.SerializeObject(this, AspNetCoreAIConstants.JsonSerializationSettings);
    }

    internal class AspNetCoreAITraceTelemetry : AspNetCoreAITelemetry
    {
        public AspNetCoreAITraceTelemetry(string instrumentationKey, bool developerMode, string environment, string categoryName, string message, LogLevel logLevel, object state)
            : base("Message", instrumentationKey, developerMode)
        {
            Data = new AspNetCoreAITelemetryData
            {
                BaseType = "MessageData",
                BaseData = new AspNetCoreAITelemetryBaseData
                {
                    Ver = 2,
                    Message = message,
                    SeverityLevel = GetSeverityLevel(logLevel),
                    Properties = new AspNetCoreAITelemetryProperties
                    {
                        DeveloperMode = developerMode ? "true" : "false",
                        AspNetCoreEnvironment = environment,
                        CategoryName = categoryName,
                        MessageMetadata = state as IReadOnlyList<KeyValuePair<string, object>>
                    }
                }
            };
        }

        public override string ToString() => JsonConvert.SerializeObject(this, AspNetCoreAIConstants.JsonSerializationSettings);

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
            httpContext.Features.Set(new AspNetCoreAIRequestTelemetry(_options.InstrumentationKey, _options.DeveloperMode, _options.Environment, httpContext));
        }

        [DiagnosticName("Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop")]
        public void OnRequestStop(HttpContext httpContext)
        {
            Debug.WriteLine($"{_options.TelemetryPrefix}{httpContext.Features.Get<AspNetCoreAIRequestTelemetry>()}");
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
            Debug.WriteLine($"{_options.TelemetryPrefix}{new AspNetCoreAIExceptionTelemetry(_options.InstrumentationKey, _options.DeveloperMode, _options.Environment, exception)}");
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
            Debug.WriteLine($"{_options.TelemetryPrefix}{new AspNetCoreAITraceTelemetry(_options.InstrumentationKey, _options.DeveloperMode, _options.Environment, _categoryName, formatter(state, exception), logLevel, state)}");
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
