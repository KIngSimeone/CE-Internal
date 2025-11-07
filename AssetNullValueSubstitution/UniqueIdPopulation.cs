using Microsoft.Xrm.Sdk;
using System;
using System.Text.RegularExpressions;

namespace AssetNullValueSubstitution
{
    public class UniqueIdPopulation : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            try
            {
                IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
                IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

                if (context.MessageName == "Create" &&
                    context.Stage == 40 &&
                    context.PrimaryEntityName == "rel_asset")
                {
                    tracingService.Trace("UniqueIdPopulation: Starting processing.");

                    Guid assetId = (Guid)context.OutputParameters["id"];

                    Entity asset = service.Retrieve("rel_asset", assetId, new Microsoft.Xrm.Sdk.Query.ColumnSet(
                        "rel_uniqueidentifier", "rel_assettype", "rel_servicetype",
                        "rel_wellcode", "rel_pipelineid", "rel_facilityid", "rel_burrowpitid",
                        "rel_flowlineid", "rel_bulklineid", "rel_manifoldid",
                        "rel_assetcode", "rel_name"));

                    string uniqueIdentifier = asset.GetAttributeValue<string>("rel_uniqueidentifier");
                    if (string.IsNullOrEmpty(uniqueIdentifier))
                    {
                        tracingService.Trace("rel_uniqueidentifier is empty. Skipping.");
                        return;
                    }

                    bool needsUpdate = false;
                    Entity updateEntity = new Entity("rel_asset") { Id = assetId };

                    int? assetTypeValue = asset.GetAttributeValue<OptionSetValue>("rel_assettype")?.Value;
                    int? serviceTypeValue = asset.GetAttributeValue<OptionSetValue>("rel_servicetype")?.Value;

                    string finalCode = null;
                    string finalName = null;

                    // Helper: Clean special characters (keep letters, numbers, and underscores only)
                    Func<string, string> CleanValue = (input) =>
                        string.IsNullOrEmpty(input) ? input : Regex.Replace(input, @"[^a-zA-Z0-9]", "");

                    // === PRIORITY: SERVICE TYPE (Flowline, Bulkline, Manifold) ===
                    if (serviceTypeValue == 1) // Flowline
                    {
                        finalCode = $"FLID{uniqueIdentifier}";
                        string flowlineId = asset.GetAttributeValue<string>("rel_flowlineid");

                        if (string.IsNullOrEmpty(flowlineId))
                        {
                            flowlineId = CleanValue(finalCode); // Bypass validation
                            updateEntity["rel_flowlineid"] = flowlineId;
                            tracingService.Trace($"FLOWLINE: Set rel_flowlineid = {flowlineId} (cleaned)");
                        }
                        else
                        {
                            tracingService.Trace($"FLOWLINE: rel_flowlineid already = {flowlineId}");
                        }

                        finalName = flowlineId;
                        needsUpdate = true;
                    }
                    else if (serviceTypeValue == 0) // Bulkline
                    {
                        finalCode = $"BULK{uniqueIdentifier}";
                        string bulklineId = asset.GetAttributeValue<string>("rel_bulklineid");

                        if (string.IsNullOrEmpty(bulklineId))
                        {
                            bulklineId = CleanValue(finalCode);
                            updateEntity["rel_bulklineid"] = bulklineId;
                            tracingService.Trace($"BULKLINE: Set rel_bulklineid = {bulklineId}");
                        }

                        finalName = bulklineId;
                        needsUpdate = true;
                    }
                    else if (serviceTypeValue == 2) // Manifold
                    {
                        finalCode = $"MFLD{uniqueIdentifier}";
                        string manifoldId = asset.GetAttributeValue<string>("rel_manifoldid");

                        if (string.IsNullOrEmpty(manifoldId))
                        {
                            manifoldId = CleanValue(finalCode);
                            updateEntity["rel_manifoldid"] = manifoldId;
                            tracingService.Trace($"MANIFOLD: Set rel_manifoldid = {manifoldId}");
                        }

                        finalName = manifoldId;
                        needsUpdate = true;
                    }

                    // === ASSET TYPE: Only if NO service type (0,1,2) is set ===
                    else if (serviceTypeValue == null || (serviceTypeValue != 0 && serviceTypeValue != 1 && serviceTypeValue != 2))
                    {
                        if (assetTypeValue == 1) // Well
                        {
                            finalCode = $"WELL{uniqueIdentifier}";
                            string wellCode = asset.GetAttributeValue<string>("rel_wellcode");

                            if (string.IsNullOrEmpty(wellCode))
                            {
                                wellCode = finalCode;
                                updateEntity["rel_wellcode"] = wellCode;
                                tracingService.Trace($"WELL: Set rel_wellcode = {wellCode}");
                            }

                            finalName = wellCode;
                            needsUpdate = true;
                        }
                        else if (assetTypeValue == 2) // Pipeline — ONLY if no service type
                        {
                            finalCode = $"PIPE{uniqueIdentifier}";
                            string pipelineId = asset.GetAttributeValue<string>("rel_pipelineid");

                            if (string.IsNullOrEmpty(pipelineId))
                            {
                                pipelineId = finalCode;
                                updateEntity["rel_pipelineid"] = pipelineId;
                                tracingService.Trace($"PIPELINE: Set rel_pipelineid = {pipelineId} (no service type)");
                            }

                            finalName = pipelineId;
                            needsUpdate = true;
                        }
                        else if (assetTypeValue == 3) // Facility
                        {
                            finalCode = $"FACN{uniqueIdentifier}";
                            string facilityId = asset.GetAttributeValue<string>("rel_facilityid");

                            if (string.IsNullOrEmpty(facilityId))
                            {
                                facilityId = finalCode;
                                updateEntity["rel_facilityid"] = facilityId;
                                tracingService.Trace($"FACILITY: Set rel_facilityid = {facilityId}");
                            }

                            finalName = facilityId;
                            needsUpdate = true;
                        }
                        else if (assetTypeValue == 4) // Burrowpit
                        {
                            finalCode = $"BPIT{uniqueIdentifier}";
                            string burrowpitId = asset.GetAttributeValue<string>("rel_burrowpitid");

                            if (string.IsNullOrEmpty(burrowpitId))
                            {
                                burrowpitId = finalCode;
                                updateEntity["rel_burrowpitid"] = burrowpitId;
                                tracingService.Trace($"BURROWPIT: Set rel_burrowpitid = {burrowpitId}");
                            }

                            finalName = burrowpitId;
                            needsUpdate = true;
                        }
                    }

                    // === ALWAYS SET ASSETCODE & NAME ===
                    if (finalCode != null && finalName != null)
                    {
                        updateEntity["rel_assetcode"] = finalCode;
                        updateEntity["rel_name"] = finalName;
                        tracingService.Trace($"FINAL: rel_assetcode = {finalCode}, rel_name = {finalName}");
                    }

                    // === UPDATE ===
                    if (needsUpdate)
                    {
                        service.Update(updateEntity);
                        tracingService.Trace("Update performed.");
                    }
                    else
                    {
                        tracingService.Trace("No updates needed.");
                    }
                }
                else
                {
                    tracingService.Trace("Context does not match. Skipping.");
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace($"ERROR: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }
    }
}