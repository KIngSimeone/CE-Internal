using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;

namespace AssetTypePluginSync
{
    public class AssetYearlCycleSync : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);

            try
            {
                if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity target))
                    return;

                if (target.LogicalName != "rel_assetyearlycycle") return;
                if (context.MessageName != "Create" && context.MessageName != "Update") return;

                // Only run when rel_asset is selected
                if (!target.Contains("rel_asset") || !(target["rel_asset"] is EntityReference assetRef))
                    return;

                Guid assetId = assetRef.Id;
                Guid cycleId = target.Id;

                // Retrieve parent asset
                var asset = service.Retrieve("rel_asset", assetId, new ColumnSet("rel_assettype", "rel_servicetype"));

                var parentAssetType = asset.GetAttributeValue<OptionSetValue>("rel_assettype")?.Value;
                var parentServiceType = asset.GetAttributeValue<OptionSetValue>("rel_servicetype")?.Value;

                tracing.Trace($"Parent: AssetType={parentAssetType}, ServiceType={parentServiceType}");

                int? targetValue = null;

                // SERVICE TYPE WINS (Flowline, Bulkline, Manifold)
                if (parentServiceType == 0) targetValue = 4; // Bulkline
                else if (parentServiceType == 1) targetValue = 5; // Flowline
                else if (parentServiceType == 2) targetValue = 6; // Manifold
                // ASSET TYPE (if no service type)
                else if (parentAssetType == 1) targetValue = 0; // Well
                else if (parentAssetType == 2) targetValue = 1; // Pipeline
                else if (parentAssetType == 3) targetValue = 2; // Facility
                else if (parentAssetType == 4) targetValue = 3; // Burrow Pit

                if (targetValue.HasValue)
                {
                    var update = new Entity("rel_assetyearlycycle", cycleId);
                    update["rel_assettype"] = new OptionSetValue(targetValue.Value);
                    service.Update(update);
                    tracing.Trace($"Set rel_assettype = {targetValue}");
                }
            }
            catch (Exception ex)
            {
                tracing.Trace("ERROR: " + ex.Message);
                throw new InvalidPluginExecutionException("Asset type sync failed: " + ex.Message);
            }
        }
    }
}

