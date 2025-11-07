using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;

namespace ApprovalTemplate
{
    public class ApprovalTemp : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            if (context.MessageName != "Create" && context.MessageName != "Update") return;
            if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity target)) return;
            if (target.LogicalName != "rel_approval_request") return;

            // -----------------------------------------------------------------
            // 2. Get the selected template (lookup)
            // -----------------------------------------------------------------
            var templateRef = target.GetAttributeValue<EntityReference>("rel_approvaltemplate");
            if (templateRef == null) return;

            Guid requestId = context.MessageName == "Create"
                ? (Guid)context.OutputParameters["id"]
                : target.Id;

            try
            {
                tracing.Trace($"Copying stages from template {templateRef.Id} to request {requestId}");

                // -----------------------------------------------------------------
                // 3. STEP 1 – Pull stages from the TEMPLATE
                // -----------------------------------------------------------------
                var fetchTemplate = $@"
                    <fetch>
                      <entity name='rel_approvalstagetemplate'>
                        <attribute name='rel_name' />
                        <attribute name='rel_stageorder' />
                        <attribute name='rel_approvalstageapprover' />
                        <filter>
                          <condition attribute='rel_approvaltemplate' operator='eq' value='{templateRef.Id}' />
                        </filter>
                        <order attribute='rel_stageorder' ascending='true' />
                      </entity>
                    </fetch>";

                var templateStages = service.RetrieveMultiple(new FetchExpression(fetchTemplate)).Entities;
                if (templateStages.Count == 0)
                {
                    tracing.Trace("No template stages found.");
                    return;
                }

                // -----------------------------------------------------------------
                // 4. STEP 2 – Remove existing stages for this request (prevents duplicates)
                // -----------------------------------------------------------------
                var fetchDelete = $@"
                    <fetch>
                      <entity name='rel_approvalrequeststage'>
                        <filter>
                          <condition attribute='rel_approvalrequest' operator='eq' value='{requestId}' />
                        </filter>
                      </entity>
                    </fetch>";

                foreach (var old in service.RetrieveMultiple(new FetchExpression(fetchDelete)).Entities)
                {
                    service.Delete(old.LogicalName, old.Id);
                }

                // -----------------------------------------------------------------
                // 5. STEP 3 – Create new request-stage records
                // -----------------------------------------------------------------
                foreach (var ts in templateStages)
                {
                    var newStage = new Entity("rel_approvalrequeststage");

                    // REQUIRED parent link
                    newStage["rel_approvalrequest"] = new EntityReference("rel_approval_request", requestId);

                    // MAP: name → approval stage name
                    var name = ts.GetAttributeValue<string>("rel_name");
                    newStage["rel_approvalrequeststagename"] = string.IsNullOrWhiteSpace(name) ? "Stage" : name;

                    // MAP: stage order
                    var order = ts.GetAttributeValue<int?>("rel_stageorder");
                    newStage["rel_stageorder"] = order ?? 0;

                    // MAP: stage approver (from template → request)
                    var approver = ts.GetAttributeValue<EntityReference>("rel_approvalstageapprover");
                    if (approver != null)
                        newStage["rel_stageapprover"] = new EntityReference(approver.LogicalName, approver.Id);



                    service.Create(newStage);
                }

                tracing.Trace($"Successfully copied {templateStages.Count} stage(s).");
            }
            catch (Exception ex)
            {
                tracing.Trace($"ERROR: {ex.Message}\n{ex.StackTrace}");
                throw new InvalidPluginExecutionException($"Stage copy failed: {ex.Message}", ex);
            }
        }
    }
}
