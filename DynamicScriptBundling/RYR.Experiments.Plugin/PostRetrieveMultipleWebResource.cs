using DouglasCrockford.JsMin;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;
using System.Text;

namespace RYR.Experiments.Plugins
{
    public class PostRetrieveMultipleWebResource : Plugin
    {
        public PostRetrieveMultipleWebResource()
            : base(typeof(PostRetrieveMultipleWebResource))
        {
            base.RegisteredEvents.Add(new Tuple<int, string, string, Action<LocalPluginContext>>(
                (int)LocalPluginContext.PipelinePhase.PostOperation,
                LocalPluginContext.Message.RetrieveMultiple.ToString(),
                "webresource", Execute));
        }

        protected void Execute(LocalPluginContext localContext)
        {
            if (localContext == null)
            {
                throw new ArgumentNullException("localContext");
            }
            if (localContext.PluginExecutionContext.Depth > 1 || !localContext.PluginExecutionContext.InputParameters.Contains("Query"))
            {
                return;
            }
            var webResourceQuery = localContext.PluginExecutionContext.InputParameters["Query"] as QueryExpression;
            var webResourceNameCondition =
                webResourceQuery.Criteria.Conditions.FirstOrDefault(x => x.AttributeName == "name");

            if (webResourceNameCondition == null) return;

            var webResourceName =
                webResourceNameCondition.Values.FirstOrDefault(x => x.ToString().Contains("crmform.min.js"));
            
            if (webResourceName == null) return;

            var entityName = webResourceName.ToString().Split('.')[0];
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
                localContext.OrganizationService.RetrieveMultiple(new FetchExpression(scriptLoadOrderFetchXml))
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
            var toBeMergedWebResources = localContext.OrganizationService.RetrieveMultiple(
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

            var dynamicWebResources = new EntityCollection();
            var dynamicWebResource = new Entity("webresource") { Id = new Guid() };
            dynamicWebResource["name"] = webResourceName;
            dynamicWebResource["content"] = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(new JsMinifier().Minify(concatenatedScripts.ToString())));
            dynamicWebResource["webresourcetype"] = new OptionSetValue(3);
            dynamicWebResources.Entities.Add(dynamicWebResource);
            localContext.PluginExecutionContext.OutputParameters["BusinessEntityCollection"] = dynamicWebResources;
        }
    }
}
