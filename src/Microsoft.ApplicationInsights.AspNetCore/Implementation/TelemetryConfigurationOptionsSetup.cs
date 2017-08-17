namespace Microsoft.Extensions.DependencyInjection
{
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using Microsoft.ApplicationInsights.AspNetCore;
    using Microsoft.ApplicationInsights.AspNetCore.Extensions;
    using Microsoft.ApplicationInsights.Channel;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.ApplicationInsights.Extensibility.PerfCounterCollector.QuickPulse;
#if NET451 || NET46
    using Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel;
#endif
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Initializes TelemetryConfiguration based on values in <see cref="ApplicationInsightsServiceOptions"/>
    /// and registered <see cref="ITelemetryInitializer"/>s and <see cref="ITelemetryModule"/>s.
    /// </summary>
    internal class TelemetryConfigurationOptionsSetup : IConfigureOptions<TelemetryConfiguration>
    {
        private readonly ApplicationInsightsServiceOptions applicationInsightsServiceOptions;
        private readonly IEnumerable<ITelemetryInitializer> initializers;
        private readonly IEnumerable<ITelemetryModule> modules;
        private readonly ITelemetryChannel telemetryChannel;
        private readonly IEnumerable<ITelemetryProcessorFactory> telemetryProcessorFactories;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:TelemetryConfigurationOptionsSetup"/> class.
        /// </summary>
        public TelemetryConfigurationOptionsSetup(
            IServiceProvider serviceProvider,
            IOptions<ApplicationInsightsServiceOptions> applicationInsightsServiceOptions,
            IEnumerable<ITelemetryInitializer> initializers,
            IEnumerable<ITelemetryModule> modules,
            IEnumerable<ITelemetryProcessorFactory> telemetryProcessorFactories)
        {
            this.applicationInsightsServiceOptions = applicationInsightsServiceOptions.Value;
            this.initializers = initializers;
            this.modules = modules;
            this.telemetryProcessorFactories = telemetryProcessorFactories;
            this.telemetryChannel = serviceProvider.GetService<ITelemetryChannel>();
        }

        /// <inheritdoc />
        public void Configure(TelemetryConfiguration configuration)
        {
            if (this.applicationInsightsServiceOptions.InstrumentationKey != null)
            {
                configuration.InstrumentationKey = this.applicationInsightsServiceOptions.InstrumentationKey;
            }

            if (this.telemetryProcessorFactories.Any())
            {
                foreach (ITelemetryProcessorFactory processorFactory in this.telemetryProcessorFactories)
                {
                    configuration.TelemetryProcessorChainBuilder.Use(processorFactory.Create);
                }
                configuration.TelemetryProcessorChainBuilder.Build();
            }

            this.AddTelemetryChannelAndProcessorsForFullFramework(configuration);

            configuration.TelemetryChannel = this.telemetryChannel ?? configuration.TelemetryChannel;

            if (this.applicationInsightsServiceOptions.DeveloperMode != null)
            {
                configuration.TelemetryChannel.DeveloperMode = this.applicationInsightsServiceOptions.DeveloperMode;
            }

            if (this.applicationInsightsServiceOptions.EndpointAddress != null)
            {
                configuration.TelemetryChannel.EndpointAddress = this.applicationInsightsServiceOptions.EndpointAddress;
            }

            foreach (ITelemetryInitializer initializer in this.initializers)
            {
                configuration.TelemetryInitializers.Add(initializer);
            }

            foreach (ITelemetryModule module in this.modules)
            {
                module.Initialize(configuration);
            }

            foreach (ITelemetryProcessor processor in configuration.TelemetryProcessors)
            {
                ITelemetryModule module = processor as ITelemetryModule;
                if (module != null)
                {
                    module.Initialize(configuration);
                }
            }
        }

        private void AddTelemetryChannelAndProcessorsForFullFramework(TelemetryConfiguration configuration)
        {
            // Enabling Quick Pulse Metric Stream
            if (this.applicationInsightsServiceOptions.EnableQuickPulseMetricStream)
            {
                var quickPulseModule = new QuickPulseTelemetryModule();
                quickPulseModule.Initialize(configuration);

                QuickPulseTelemetryProcessor processor = null;
                configuration.TelemetryProcessorChainBuilder.Use((next) => {
                    processor = new QuickPulseTelemetryProcessor(next);
                    quickPulseModule.RegisterTelemetryProcessor(processor);
                    return processor;
                });
            }

#if NET451 || NET46
            // Adding Server Telemetry Channel if services doesn't have an existing channel
            configuration.TelemetryChannel = this.telemetryChannel ?? new ServerTelemetryChannel();
            if (configuration.TelemetryChannel is ServerTelemetryChannel)
            {
                // Enabling Adaptive Sampling and initializing server telemetry channel with configuration
                if (configuration.TelemetryChannel.GetType() == typeof(ServerTelemetryChannel))
                {
                    if (this.applicationInsightsServiceOptions.EnableAdaptiveSampling)
                    {
                        configuration.TelemetryProcessorChainBuilder.UseAdaptiveSampling();
                    }
                    (configuration.TelemetryChannel as ServerTelemetryChannel).Initialize(configuration);
                }
            }
#endif
            configuration.TelemetryProcessorChainBuilder.Build();
        }
    }
}