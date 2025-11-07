using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;

namespace AssetYearlyCycle
{
    public class ChangeTrackerPlugin : IPlugin
    {
        void IPlugin.Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = factory.CreateOrganizationService(context.UserId);

            try
            {
                if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity target))
                    return;

                if (target.LogicalName != "rel_assetyearlycycle") return;
                if (context.MessageName != "Create" && context.MessageName != "Update") return;

                // 1. GET ASSET LOOKUP
                if (!target.Contains("rel_asset") || !(target["rel_asset"] is EntityReference assetRef))
                {
                    tracing.Trace("No asset selected. Skipping.");
                    return;
                }

                Guid assetId = assetRef.Id;
                Guid currentId = target.Id;

                // 2. NEW: CHECK READINESS STATUS
                var asset = service.Retrieve("rel_asset", assetId, new ColumnSet("rel_readinessstatus"));
                var readiness = asset.GetAttributeValue<OptionSetValue>("rel_readinessstatus")?.Value;

                if (readiness == 1) // Ready For Abandonment
                {
                    UpdateTracker(service, currentId, "Asset is Ready For Abandonment – changes not tracked.");
                    tracing.Trace("BLOCKED: Ready For Abandonment");
                    return;
                }

                if (readiness == 2) // Abandoned
                {
                    UpdateTracker(service, currentId, "Asset is Abandoned – changes not tracked.");
                    tracing.Trace("BLOCKED: Abandoned");
                    return;
                }

                // 3. ONLY NOT ABANDONED (3) CONTINUES
                if (readiness != 3)
                {
                    UpdateTracker(service, currentId, "Readiness status unknown – changes not tracked.");
                    return;
                }

                // 4. NORMAL TRACKING (your original logic)
                decimal? currentP50Edm = target.GetAttributeValue<decimal?>("rel_p50edmcost");
                decimal? currentP50Mod = target.GetAttributeValue<decimal?>("rel_p50mod");
                int? currentDecommYear = target.GetAttributeValue<OptionSetValue>("rel_actualyearofdecommissioning")?.Value;

                var query = new QueryExpression("rel_assetyearlycycle")
                {
                    ColumnSet = new ColumnSet("rel_p50edmcost", "rel_p50mod", "rel_actualyearofdecommissioning", "createdon"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("rel_asset", ConditionOperator.Equal, assetId),
                            new ConditionExpression("rel_assetyearlycycleid", ConditionOperator.NotEqual, currentId)
                        }
                    },
                    Orders = { new OrderExpression("createdon", OrderType.Descending) },
                    TopCount = 1
                };

                var previous = service.RetrieveMultiple(query);
                string message;

                if (previous.Entities.Count == 0)
                    message = "First cycle for this asset.";
                else
                {
                    Entity prev = previous[0];
                    decimal? prevEdm = prev.GetAttributeValue<decimal?>("rel_p50edmcost");
                    decimal? prevMod = prev.GetAttributeValue<decimal?>("rel_p50mod");
                    int? prevYear = prev.GetAttributeValue<OptionSetValue>("rel_actualyearofdecommissioning")?.Value;

                    var changes = new List<string>();
                    if (prevEdm != currentP50Edm) changes.Add($"P50 EDM Cost ({F(prevEdm)} to {F(currentP50Edm)})");
                    if (prevMod != currentP50Mod) changes.Add($"P50 MOD ({F(prevMod)} to {F(currentP50Mod)})");
                    if (prevYear != currentDecommYear) changes.Add($"Decomm Year ({prevYear} to {currentDecommYear})");

                    message = changes.Count > 0
                        ? $"Change detected: {string.Join(", ", changes)}."
                        : "No change from previous cycle.";
                }

                UpdateTracker(service, currentId, message);
                tracing.Trace("TRACKED: " + message);
            }
            catch (Exception ex)
            {
                tracing.Trace("ERROR: " + ex.Message);
                throw new InvalidPluginExecutionException("Tracker failed: " + ex.Message);
            }
        }

        // RE-USABLE UPDATE (keeps code clean)
        private void UpdateTracker(IOrganizationService service, Guid recordId, string text)
        {
            var upd = new Entity("rel_assetyearlycycle", recordId);
            upd["rel_p50modtracker"] = text;
            service.Update(upd);
        }

        private string F(decimal? v) => v.HasValue ? v.Value.ToString("N0") : "—";
    }

}

