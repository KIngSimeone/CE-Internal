
    using Microsoft.Xrm.Sdk;
    using System;
    using System.Text.RegularExpressions;

    namespace AssetOne_FlowlineId_Validation
    {
        public class FlowlineIdValidation : IPlugin
        {
            public void Execute(IServiceProvider serviceProvider)
            {
                ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

                try
                {
                    IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
                    IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                    IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

                    // Verify context: Create or Update on rel_asset with rel_flowlineid
                    if (context.InputParameters.Contains("Target") &&
                        context.InputParameters["Target"] is Entity target &&
                        target.LogicalName == "rel_asset" &&
                        (context.MessageName == "Create" || context.MessageName == "Update") &&
                        target.Contains("rel_flowlineid"))
                    {
                        tracingService.Trace("FlowlineIdValidation: Starting processing.");

                        // Get rel_assettype and rel_servicetype
                        int? assetTypeValue = target.Contains("rel_assettype")
                            ? target.GetAttributeValue<OptionSetValue>("rel_assettype")?.Value
                            : (context.PreEntityImages.Contains("preImage")
                                ? context.PreEntityImages["preImage"].GetAttributeValue<OptionSetValue>("rel_assettype")?.Value
                                : null);

                        int? serviceTypeValue = target.Contains("rel_servicetype")
                            ? target.GetAttributeValue<OptionSetValue>("rel_servicetype")?.Value
                            : (context.PreEntityImages.Contains("preImage")
                                ? context.PreEntityImages["preImage"].GetAttributeValue<OptionSetValue>("rel_servicetype")?.Value
                                : null);

                        // Skip if not Pipeline + Flowline
                        if (assetTypeValue != 2 || serviceTypeValue != 1)
                        {
                            tracingService.Trace("FlowlineIdValidation: Conditions not met (assettype != 2 or servicetype != 1). Skipping.");
                            return;
                        }

                        // Get the flowlineid
                        string flowlineid = target.GetAttributeValue<string>("rel_flowlineid");

                        // 1. Skip if null or empty
                        if (string.IsNullOrEmpty(flowlineid))
                        {
                            tracingService.Trace("FlowlineIdValidation: Flowlineid is empty. Skipping.");
                            return;
                        }

                        // 2. SKIP IF SHORT CODE: FLID001, FLID0001, FLID1234
                        if (Regex.IsMatch(flowlineid, @"^FLID\d{3,4}$", RegexOptions.IgnoreCase))
                        {
                            tracingService.Trace($"FlowlineIdValidation: Short code '{flowlineid}' accepted. Skipping rectification.");
                            return;
                        }

                        // 3. Otherwise: Clean and rectify to 12-character format
                        string cleanedFlowlineid = Regex.Replace(flowlineid, @"[^A-Za-z0-9]", "");
                        string normalizedFlowlineid = Regex.Replace(cleanedFlowlineid, @"[a-z]", m => m.Value.ToUpper());
                        string rectifiedFlowlineid = RectifyFlowlineId(normalizedFlowlineid, tracingService);

                        tracingService.Trace($"FlowlineIdValidation: Rectified flowlineid to {rectifiedFlowlineid}");

                        // Update only if changed
                        if (flowlineid != rectifiedFlowlineid)
                        {
                            target["rel_flowlineid"] = rectifiedFlowlineid;
                        }

                        tracingService.Trace("FlowlineIdValidation: Processing complete.");
                    }
                    else
                    {
                        tracingService.Trace("FlowlineIdValidation: Context does not meet requirements. Skipping.");
                    }
                }
                catch (Exception ex)
                {
                    tracingService.Trace($"FlowlineIdValidation: Exception - {ex.Message}");
                    throw;
                }
            }

            private string RectifyFlowlineId(string input, ITracingService tracingService)
            {
                // Standard format: 5 letters + 3 digits + 4 letters (e.g., ADIBW002LFLN)
                string defaultLetters = "XXXXX";
                string defaultDigits = "000";

                // Extract letters and digits
                string letters = Regex.Replace(input, @"[^A-Z]", "");
                string digits = Regex.Replace(input, @"[^0-9]", "");

                // First 5 letters
                string firstPart = letters.Length >= 5 ? letters.Substring(0, 5) : (letters + defaultLetters).Substring(0, 5);

                // 3 digits
                string middlePart = digits.Length >= 3 ? digits.Substring(0, 3) : (digits + defaultDigits).Substring(0, 3);

                // Last 4 letters
                string lastPart = letters.Length >= 9
                    ? letters.Substring(letters.Length - 4, 4)
                    : (letters.Length >= 5
                        ? letters.Substring(letters.Length - Math.Min(4, letters.Length)) + "XXXX"
                        : "XXXX").Substring(0, 4);

                string rectified = firstPart + middlePart + lastPart;
                tracingService.Trace($"RectifyFlowlineId: Input={input}, First={firstPart}, Middle={middlePart}, Last={lastPart}, Result={rectified}");
                return rectified;
            }
        }
    }

