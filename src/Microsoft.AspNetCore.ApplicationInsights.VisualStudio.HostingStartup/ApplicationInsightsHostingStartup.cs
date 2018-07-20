// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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
        public static readonly string AspNetCoreAIOperationNameTagKey = "Microsoft.AspNetCore.ApplicationInsights.OperationName";
        public static readonly string AspNetCoreAILocationIpTagKey = "Microsoft.AspNetCore.ApplicationInsights.LocationIp";
        public static readonly string DefaultTelemetryNamePrefix = "Microsoft.AspNetCore.";
        public static readonly string DefaultDeveloperTelemetryNamePrefix = "Microsoft.AspNetCore.Dev";
        public static readonly JsonSerializerSettings JsonSerializationSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };
    }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    internal class SerializationNameAttribute : Attribute
    {
        public string Name { get; }

        public SerializationNameAttribute(string name)
        {
            Name = name;
        }
    }

    internal class AspNetCoreAITelemetryConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
            => objectType == typeof(AspNetCoreAITelemetryProperties);

        public override bool CanRead => false;

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) => throw new NotImplementedException();

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteStartObject();

            foreach (var property in value.GetType().GetProperties())
            {
                if (property.CanRead && property.PropertyType == typeof(string))
                {
                    var customName = property.GetCustomAttributes(typeof(SerializationNameAttribute), false).SingleOrDefault() as SerializationNameAttribute;
                    var propertyName = customName?.Name ?? property.Name;
                    var propertyValue = property.GetValue(value) as string;

                    if (!string.IsNullOrEmpty(propertyValue))
                    {
                        writer.WritePropertyName(propertyName);
                        writer.WriteValue(propertyValue);
                    }
                }
            }

            var properties = value as AspNetCoreAITelemetryProperties;
            if (properties?.MessageMetadata != null)
            {
                foreach (var metadata in properties.MessageMetadata)
                {
                    var metadataValue = Convert.ToString(metadata.Value, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(metadataValue))
                    {
                        writer.WritePropertyName(metadata.Key);
                        writer.WriteValue(metadataValue);
                    }
                }
            }
            writer.WriteEndObject();
        }
    }

    [JsonConverter(typeof(AspNetCoreAITelemetryConverter))]
    internal class AspNetCoreAITelemetryProperties
    {
        [SerializationName("handledAt")]
        public string HandledAt { get; internal set; }
        public string AspNetCoreEnvironment { get; internal set; }
        public string DeveloperMode { get; internal set; }
        public string CategoryName { get; internal set; }
        public string Exception { get; internal set; }

        public IEnumerable<KeyValuePair<string, object>> MessageMetadata { get; internal set; }
    }

    [JsonConverter(typeof(AspNetCoreAITelemetryConverter))]
    internal class AspNetCoreAITelemetryTags
    {
        [SerializationName("ai.application.ver")]
        public string ApplicationVersion { get; internal set; }
        [SerializationName("ai.cloud.roleInstance")]
        public string CloudRoleInstance { get; internal set; }
        [SerializationName("ai.operation.id")]
        public string OperationId { get; internal set; }
        [SerializationName("ai.operation.parentId")]
        public string OperationParentId { get; internal set; }
        [SerializationName("ai.operation.name")]
        public string OperationName { get; internal set; }
        [SerializationName("ai.location.ip")]
        public string LocationIp { get; internal set; }
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
        public bool? Success { get; internal set; }
        public string ResponseCode { get; internal set; }
        public string Url { get; internal set; }
        public string Message { get; internal set; }
        public string SeverityLevel { get; internal set; }
        public string Data { get; internal set; }
        public string ResultCode { get; internal set; }
        public string Type { get; internal set; }
        public string Target { get; internal set; }
        public AspNetCoreAITelemetryProperties Properties { get; internal set; }
        public AspNetCoreAITelemetryException[] Exceptions { get; internal set; }
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
        public AspNetCoreAITelemetryTags Tags { get; internal set; }
        public AspNetCoreAITelemetryData Data { get; internal set; }

        public AspNetCoreAITelemetry(string name, string instrumentationKey, bool developerMode)
        {
            var currentActivity = Activity.Current;
            var rootActivity = currentActivity;
            while (rootActivity?.Parent != null)
            {
                rootActivity = rootActivity.Parent;
            }
            Tags = new AspNetCoreAITelemetryTags
            {
                ApplicationVersion = Assembly.GetEntryAssembly()?.GetName().Version.ToString(),
                CloudRoleInstance = Dns.GetHostName(),
                OperationName = rootActivity?.Tags?.SingleOrDefault(t => t.Key == AspNetCoreAIConstants.AspNetCoreAIOperationNameTagKey).Value,
                LocationIp = rootActivity?.Tags?.SingleOrDefault(t => t.Key == AspNetCoreAIConstants.AspNetCoreAILocationIpTagKey).Value
            };
            var fullname = developerMode
                ? AspNetCoreAIConstants.DefaultDeveloperTelemetryNamePrefix
                : AspNetCoreAIConstants.DefaultTelemetryNamePrefix;
            if (!string.IsNullOrEmpty(instrumentationKey))
            {
                fullname += $".{instrumentationKey.Replace("-", string.Empty)}";
            }
            fullname += $".{name}";
            Name = fullname;
            Time = currentActivity?.StartTimeUtc ?? DateTime.UtcNow;
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
            var url = $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}";
            var path = $"{request.PathBase}{request.Path}";
            var statusCode = response.StatusCode;
            var success = (statusCode > 0) && (statusCode < 400);
            var currentActivity = Activity.Current;
            var duration = currentActivity.Duration;
            var operationId = currentActivity.Id;
            var remoteIp = _httpContext.Request.Headers["X-Forwarded-For"].ToString()
                ?? _httpContext.Features.Get<IHttpConnectionFeature>().RemoteIpAddress.ToString();

            Tags.OperationId = currentActivity.RootId;
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
        public AspNetCoreAIExceptionTelemetry(string instrumentationKey, bool developerMode, string environment, Exception exception, string message = null)
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
                        DeveloperMode = developerMode ? "true" : "false",
                        AspNetCoreEnvironment = environment
                    },
                    Exceptions = new[] {
                        new AspNetCoreAITelemetryException
                        {
                            Id = exception.GetHashCode(),
                            TypeName = exception.GetType().FullName,
                            Message = message ?? exception.Message,
                            HasFullStack = true,
                            ParsedStack = parsedStack.ToArray()
                        }
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
                    Properties = new AspNetCoreAITelemetryProperties
                    {
                        DeveloperMode = developerMode ? "true" : "false",
                        AspNetCoreEnvironment = environment,
                    }
                }
            };
        }

        public override string ToString() => JsonConvert.SerializeObject(this, AspNetCoreAIConstants.JsonSerializationSettings);

    }
    internal class AspNetCoreAIDependencyTelemetry : AspNetCoreAITelemetry
    {
        public AspNetCoreAIDependencyTelemetry(string instrumentationKey, bool developerMode, string environment, HttpRequestMessage request, TaskStatus status, HttpResponseMessage response)
            : base("RemoteDependency", instrumentationKey, developerMode)
        {
            var statusCode = (int)response.StatusCode;
            var currentActivity = Activity.Current;

            Data = new AspNetCoreAITelemetryData
            {
                BaseType = "RemoteDependencyData",
                BaseData = new AspNetCoreAITelemetryBaseData
                {
                    Ver = 2,
                    Name = $"{request.Method} {request.RequestUri.AbsolutePath}",
                    Id = currentActivity.Id,
                    Data = request.RequestUri.OriginalString,
                    Duration = currentActivity.Duration.ToString(),
                    ResultCode = statusCode.ToString(CultureInfo.InvariantCulture),
                    Success = (statusCode > 0) && (statusCode < 400),
                    Type = "Http",
                    Target = request.RequestUri.Host,
                    Properties = new AspNetCoreAITelemetryProperties
                    {
                        DeveloperMode = developerMode ? "true" : "false",
                        AspNetCoreEnvironment = environment,
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
            var currentActivity = Activity.Current;
            if (string.IsNullOrEmpty(currentActivity.Tags.SingleOrDefault(t => t.Key == AspNetCoreAIConstants.AspNetCoreAIOperationNameTagKey).Value))
            {
                currentActivity.AddTag(AspNetCoreAIConstants.AspNetCoreAIOperationNameTagKey, $"{httpContext.Request.Method} {httpContext.Request.PathBase}{httpContext.Request.Path}");
            }
            if (string.IsNullOrEmpty(currentActivity.Tags.SingleOrDefault(t => t.Key == AspNetCoreAIConstants.AspNetCoreAILocationIpTagKey).Value))
            {
                currentActivity.AddTag(AspNetCoreAIConstants.AspNetCoreAILocationIpTagKey, httpContext.Connection.RemoteIpAddress.ToString());
            }

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
            var telemetry = new AspNetCoreAIExceptionTelemetry(_options.InstrumentationKey, _options.DeveloperMode, _options.Environment, exception);

            var currentActivity = Activity.Current;
            if (currentActivity != null)
            {
                telemetry.Tags.OperationId = currentActivity.RootId;
                telemetry.Tags.OperationParentId = currentActivity.Id;
            }
            telemetry.Data.BaseData.Properties.HandledAt = "Platform";
            Debug.WriteLine($"{_options.TelemetryPrefix}{telemetry}");
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
            AspNetCoreAITelemetry telemetry = null;
            if (exception == null)
            {
                telemetry = new AspNetCoreAITraceTelemetry(_options.InstrumentationKey, _options.DeveloperMode, _options.Environment, _categoryName, formatter(state, exception), logLevel, state);
            }
            else
            {
                telemetry = new AspNetCoreAIExceptionTelemetry(_options.InstrumentationKey, _options.DeveloperMode, _options.Environment, exception, formatter(state, exception));
                telemetry.Data.BaseData.Properties.Exception = exception.ToString();
            }

            var currentActivity = Activity.Current;
            if (currentActivity != null)
            {
                telemetry.Tags.OperationId = currentActivity.RootId;
                telemetry.Tags.OperationParentId = currentActivity.Id;
            }
            telemetry.Data.BaseData.Properties.CategoryName = _categoryName;
            telemetry.Data.BaseData.SeverityLevel = logLevel.ToAISeverityLevel();
            telemetry.Data.BaseData.Properties.MessageMetadata = state as IReadOnlyList<KeyValuePair<string, object>>;
            Debug.WriteLine($"{_options.TelemetryPrefix}{telemetry}");
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

    internal static class PropertyExtensions
    {
        public static object GetProperty(this object obj, string propertyName)
        {
            return obj.GetType().GetTypeInfo().GetDeclaredProperty(propertyName)?.GetValue(obj);
        }
    }

    internal static class LogLeveExtensions
    {
        public static string ToAISeverityLevel(this LogLevel logLevel)
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

    internal class HttpDiagnosticSourceListener : IObserver<KeyValuePair<string, object>>
    {
        private AspNetCoreAIOptions _options;

        public HttpDiagnosticSourceListener(AspNetCoreAIOptions options)
        {
            _options = options;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(KeyValuePair<string, object> evnt)
        {
            switch (evnt.Key)
            {
                case "System.Net.Http.HttpRequestOut.Stop":
                    {
                        var request = evnt.Value.GetProperty("Request") as HttpRequestMessage;
                        var status = (TaskStatus)(evnt.Value.GetProperty("RequestTaskStatus"));
                        var response = evnt.Value.GetProperty("Response") as HttpResponseMessage;

                        var telemetry = new AspNetCoreAIDependencyTelemetry(_options.InstrumentationKey, _options.DeveloperMode, _options.Environment, request, status, response);
                        var currentActivity = Activity.Current;
                        if (currentActivity != null)
                        {
                            telemetry.Tags.OperationId = currentActivity.RootId;
                            telemetry.Tags.OperationParentId = currentActivity.Id;
                        }

                        Debug.WriteLine($"{_options.TelemetryPrefix}{telemetry}");
                        break;
                    }
                case "System.Net.Http.Exception":
                    {
                        var exception = evnt.Value.GetProperty("Exception") as Exception;
                        var telemetry = new AspNetCoreAIExceptionTelemetry(_options.InstrumentationKey, _options.DeveloperMode, _options.Environment, exception);
                        var currentActivity = Activity.Current;
                        if (currentActivity != null)
                        {
                            telemetry.Tags.OperationId = currentActivity.RootId;
                            telemetry.Tags.OperationParentId = currentActivity.Id;
                        }

                        Debug.WriteLine($"{_options.TelemetryPrefix}{telemetry}");
                        break;
                    }
                default:
                    break;
            }
        }
    }

    internal class HttpDiagnosticSourceSubscriber : IObserver<DiagnosticListener>
    {
        private AspNetCoreAIOptions _options;

        public HttpDiagnosticSourceSubscriber(AspNetCoreAIOptions options)
        {
            _options = options;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(DiagnosticListener value)
        {

            if (value.Name == "HttpHandlerDiagnosticListener")
            {
                value.Subscribe(new HttpDiagnosticSourceListener(_options));
            }
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
                DiagnosticListener.AllListeners.Subscribe(new HttpDiagnosticSourceSubscriber(options));

                if (string.IsNullOrEmpty(options.InstrumentationKey) && Debugger.IsAttached)
                {
                    loggerFactory.AddAspNetCoreAI(options);
                }

                next(builder);
            };
        }
    }
}
