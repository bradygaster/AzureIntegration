// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DiagnosticAdapter;

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
            services.AddSingleton<IStartupFilter, ApplicationInsightsVisualStudioStartupFilter>();
        }
    }
    internal class AIVSTelemetry
    {
        public DateTimeOffset Timestamp { get; set; }
    }

    internal class AIVSDiagnosticsAdapter
    {
        [DiagnosticName("Microsoft.AspNetCore.Hosting.HttpRequestIn")]
        public void OnRequest(HttpContext httpContext)
        {
            // No-op
        }

        [DiagnosticName("Microsoft.AspNetCore.Hosting.HttpRequestIn.Start")]
        public void OnRequestStart(HttpContext httpContext)
        {
            httpContext.Features.Set(new AIVSTelemetry { Timestamp = DateTimeOffset.UtcNow });
        }

        [DiagnosticName("Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop")]
        public void OnRequestStop(HttpContext httpContext)
        {
            var activity = Activity.Current;
            var telemetry = httpContext.Features.Get<AIVSTelemetry>();
            var duration = DateTimeOffset.UtcNow - telemetry.Timestamp;
            var message = $@"Application Insights Telemetry: {{""name"":""{activity.OperationName}"",""time"":""{telemetry.Timestamp.UtcDateTime.ToString("o", CultureInfo.InvariantCulture)}"",""iKey"":""b4525cec-6bb8-4f4b-8b69-01b57c284e29"",""tags"":{{""ai.operation.id"":""928bdfc0-4a07235921780c33"",""ai.internal.sdkVersion"":""aspnet5c:2.1.1"",""ai.operation.name"":""GET /Index"",""ai.location.ip"":""::1"",""ai.internal.nodeName"":""johluo-desktop"",""ai.cloud.roleInstance"":""johluo-desktop"",""ai.application.ver"":""1.0.0.0""}},""data"":{{""baseType"":""RequestData"",""baseData"":{{""ver"":2,""id"":""|928bdfc0-4a07235921780c33."",""name"":""GET /Index"",""duration"":""{duration.ToString()}"",""success"":true,""responseCode"":""200"",""url"":""https://localhost:44343/"",""properties"":{{""DeveloperMode"":""true"",""httpMethod"":""GET"",""AspNetCoreEnvironment"":""Telemetry""}}}}}}}}";
            Debugger.Log(0, "category", message + Environment.NewLine);
        }
    }

    internal class ApplicationInsightsVisualStudioStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return builder =>
            {
                var diagnosticListener = builder.ApplicationServices.GetRequiredService<DiagnosticListener>();
                diagnosticListener.SubscribeWithAdapter(new AIVSDiagnosticsAdapter());

                next(builder);
            };
        }
    }
}
