using Jellyfin.Data;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Model.Serialization;
using n0tFlix.Channel.Redtube.Configuration;
using System;

namespace n0tFlix.Channel.Redtube
{
    public class Plugin : BasePlugin<PluginConfiguration>
    {
        /// <summary>
        /// The name of our plugin
        /// </summary>
        public override string Name => "Redtube";

        /// <summary>
        /// Description of the plugin
        /// </summary>
        public override string Description => "Watch redtube all day long";

        public override Guid Id => Guid.Parse("43fb2dfd-caa6-4455-854e-e9641a929cc0");

        /// <summary>
        /// Gets the plugin instance.
        /// </summary>
        public static Plugin Instance { get; private set; }

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
    : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }
    }
}