using System.Text;
using DouglasCrockford.JsMin;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;
using System.Xml.Linq;
using Contract = System.Diagnostics.Contracts.Contract;

namespace RYR.Experiments.Plugins
{
    public class PostGetScriptsForEntity : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            Contract.Assert(serviceProvider != null, "serviceProvider is null");
            var pluginExecutionContext =
                (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            Contract.Assert(tracingService != null, "TracingService is null");

            try
            {
                var factory =
                    (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

                var organizationService = factory.CreateOrganizationService(pluginExecutionContext.UserId);
                var entityName = pluginExecutionContext.InputParameters["entityname"];

                var scriptLoadOrderFetchXml = string.Format(@"
                    <fetch count='1' >
                      <entity name='ryr_formloadsequence' >
                        <attribute name='ryr_name' />
                        <attribute name='ryr_scripts' />
                        <filter>
                          <condition attribute='ryr_name' operator='eq' value='{0}' />
                        </filter>
                      </entity>
                    </fetch>", entityName);
                var concatenatedScripts = new StringBuilder();
                var loadSequenceResults =
                    organizationService.RetrieveMultiple(new FetchExpression(scriptLoadOrderFetchXml))
                        .Entities;

                if (!loadSequenceResults.Any()) return;

                var scriptsToMerge = loadSequenceResults[0].GetAttributeValue<string>("ryr_scripts").Split(',');
                var webresourceFetchXml = string.Format(@"
                        <fetch>
                          <entity name='webresource' >
                            <attribute name='content' />
                            <attribute name='name' />
                            <filter>
                              <condition attribute='webresourcetype' operator='eq' value='3' />
                              <condition attribute='name' operator='in' >
                                {0}
                              </condition>
                            </filter>
                          </entity>
                        </fetch>", string.Join(string.Empty,
                         scriptsToMerge.Select(x => string.Format("<value>{0}</value>", x))));
                var toBeMergedWebResources = organizationService.RetrieveMultiple(
                    new FetchExpression(webresourceFetchXml)).Entities;

                if (!toBeMergedWebResources.Any()) return;

                foreach (var s in scriptsToMerge)
                {
                    var matchedWebresource = toBeMergedWebResources
                        .FirstOrDefault(x => x.GetAttributeValue<string>("name") == s);
                    if (matchedWebresource != null)
                    {
                        concatenatedScripts.AppendLine(Encoding.UTF8.GetString(Convert.FromBase64String(
                            matchedWebresource.GetAttributeValue<string>("content"))));
                    }
                    else
                    {
                        concatenatedScripts.AppendLine(
                            string.Format(
                                "Xrm.Page.ui.setFormNotification('Unable to load {0}', 'ERROR', '{1}');", s,
                                Guid.NewGuid()));
                    }
                }
                pluginExecutionContext.OutputParameters["minifiedscripts"] = new JsMinifier().Minify(concatenatedScripts.ToString());
            }
            catch (Exception e)
            {
                tracingService.Trace(e.StackTrace);
                throw;
            }
        }
    }
}