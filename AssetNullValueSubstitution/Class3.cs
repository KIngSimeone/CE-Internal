using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Text.RegularExpressions;

namespace AssetOne_WellCode_Validation
{
    public class WellCodeValidation : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            try
            {
                IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
                IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

                // Verify context: Create or Update on rel_asset with wellcode
                if (context.InputParameters.Contains("Target") &&
                    context.InputParameters["Target"] is Entity target &&
                    target.LogicalName == "rel_asset" &&
                    (context.MessageName == "Create" || context.MessageName == "Update") &&
                    target.Contains("rel_wellcode"))
                {
                    tracingService.Trace("ValidateWellcode: Starting processing.");

                    // Get AssetType (from target or preimage on Update)
                    int? assetTypeValue = target.Contains("rel_assettype")
                        ? target.GetAttributeValue<OptionSetValue>("rel_assettype")?.Value
                        : (context.PreEntityImages.Contains("preImage")
                            ? context.PreEntityImages["preImage"].GetAttributeValue<OptionSetValue>("rel_assettype")?.Value
                            : null);

                    // Skip if AssetType is not "well" (value 1)
                    if (assetTypeValue != 1)
                    {
                        tracingService.Trace("ValidateWellcode: AssetType is not 'well'. Skipping.");
                        return;
                    }

                    // Get wellcode
                    string wellcode = target.GetAttributeValue<string>("rel_wellcode");

                    if (string.IsNullOrEmpty(wellcode))
                    {
                        tracingService.Trace("ValidateWellcode: Wellcode is empty. Skipping.");
                        return;
                    }

                    // Clean the wellcode: Remove spaces and special characters, keep only letters and digits
                    string cleanedWellcode = Regex.Replace(wellcode, @"[^A-Za-z0-9]", "");

                    // Extract letters (first non-digit part) and digits (rest)
                    string letters = Regex.Match(cleanedWellcode, @"^[A-Za-z]+").Value;
                    string digits = Regex.Match(cleanedWellcode, @"\d+$").Value;

                    // If less than 4 letters, we can't form a valid prefix - skip or handle as per needs (here, we proceed with what we have)
                    if (letters.Length < 4)
                    {
                        tracingService.Trace("ValidateWellcode: Insufficient letters. Skipping normalization.");
                        return;
                    }

                    // Take first 4 letters, uppercase them
                    string prefix = letters.Substring(0, 4).ToUpper();

                    // Keep all digits (no fixed length, allow 3+)
                    if (digits.Length < 3)
                    {
                        tracingService.Trace("ValidateWellcode: Insufficient digits. Skipping normalization.");
                        return;
                    }

                    // Form normalized wellcode: uppercase prefix + all digits
                    string normalizedWellcode = prefix + digits;
                    tracingService.Trace($"ValidateWellcode: Normalized wellcode to {normalizedWellcode}");

                    // Update target with normalized wellcode if changed
                    if (wellcode != normalizedWellcode)
                    {
                        target["rel_wellcode"] = normalizedWellcode;
                    }

                    tracingService.Trace("ValidateWellcode: Processing complete.");
                }
                else
                {
                    tracingService.Trace("ValidateWellcode: Context does not meet requirements. Skipping.");
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace($"ValidateWellcode: Exception - {ex.Message}");
                throw;
            }
        }
    }
}
