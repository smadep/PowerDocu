using System;
using System.Collections.Generic;
using System.Linq;
using PowerDocu.Common;

namespace PowerDocu.SolutionDocumenter
{
    public class SolutionDocumentationContent
    {
        public List<FlowEntity> flows = new List<FlowEntity>();
        public List<AppEntity> apps = new List<AppEntity>();
        public List<AppModuleEntity> appModules = new List<AppModuleEntity>();
        public List<AgentEntity> agents = new List<AgentEntity>();
        public List<BPFEntity> businessProcessFlows = new List<BPFEntity>();
        public List<DesktopFlowEntity> desktopFlows = new List<DesktopFlowEntity>();
        public List<DataflowEntity> dataflows = new List<DataflowEntity>();
        public SolutionEntity solution;
        public DocumentationContext context;
        public string folderPath,
            filename;

        public SolutionDocumentationContent(
            DocumentationContext context,
            string path
        )
        {
            this.context = context;
            this.solution = context.Solution;
            this.apps = context.Apps ?? new List<AppEntity>();
            this.flows = context.Flows ?? new List<FlowEntity>();
            this.appModules = context.AppModules ?? new List<AppModuleEntity>();
            this.agents = context.Agents ?? new List<AgentEntity>();
            this.businessProcessFlows = context.BusinessProcessFlows ?? new List<BPFEntity>();
            this.desktopFlows = context.DesktopFlows ?? new List<DesktopFlowEntity>();
            this.dataflows = context.Dataflows ?? new List<DataflowEntity>();
            filename = CharsetHelper.GetSafeName(solution.UniqueName);
            folderPath = path;
        }

        /// <summary>
        /// Legacy constructor for backward compatibility.
        /// </summary>
        public SolutionDocumentationContent(
            SolutionEntity solution,
            List<AppEntity> apps,
            List<FlowEntity> flows,
            List<AppModuleEntity> appModules,
            string path
        )
        {
            this.solution = solution;
            this.apps = apps ?? new List<AppEntity>();
            this.flows = flows ?? new List<FlowEntity>();
            this.appModules = appModules ?? new List<AppModuleEntity>();
            filename = CharsetHelper.GetSafeName(solution.UniqueName);
            folderPath = path;
        }

        public string GetDisplayNameForComponent(SolutionComponent component)
        {
            if (component.Type == "Workflow")
            {
                // Try to resolve flow by ID using the context first (most reliable)
                if (context != null)
                {
                    string flowName = context.GetFlowNameById(component.ID);
                    if (!string.IsNullOrEmpty(flowName))
                    {
                        FlowEntity flow = context.GetFlowById(component.ID);
                        string typeLabel = "[" + FlowEntity.GetModernFlowTypeLabel(
                            flow?.modernFlowType ?? FlowEntity.ModernFlowType.CloudFlow) + "]";
                        if (flow?.trigger != null)
                            return flowName + " (" + flow.trigger.Name + ": " + flow.trigger.Type + ") " + typeLabel;
                        return flowName + " " + typeLabel;
                    }
                }
                // Fallback: search parsed flows list by ID
                FlowEntity flowEntity = flows?.FirstOrDefault(f =>
                    f.ID != null && f.ID.Trim('{', '}').Equals(component.ID?.Trim('{', '}'), StringComparison.OrdinalIgnoreCase));
                if (flowEntity != null)
                {
                    string typeLabel = "[" + FlowEntity.GetModernFlowTypeLabel(flowEntity.modernFlowType) + "]";
                    return flowEntity.Name + " (" + flowEntity.trigger.Name + ": " + flowEntity.trigger.Type + ") " + typeLabel;
                }
            }
            if (component.Type == "Model-Driven App")
            {
                AppModuleEntity appModule = appModules?.FirstOrDefault(a => a.UniqueName != null && a.UniqueName.Equals(component.SchemaName, StringComparison.OrdinalIgnoreCase));
                if (appModule != null)
                {
                    return appModule.GetDisplayName();
                }
            }
            return solution.GetDisplayNameForComponent(component);
        }

        /// <summary>
        /// Returns structured display parts for a Workflow component:
        /// Name, Trigger Info (e.g. "manual: Request"), and Flow Type (e.g. "Cloud Flow").
        /// </summary>
        public (string Name, string TriggerInfo, string FlowType) GetWorkflowDisplayParts(SolutionComponent component)
        {
            if (context != null)
            {
                string flowName = context.GetFlowNameById(component.ID);
                if (!string.IsNullOrEmpty(flowName))
                {
                    FlowEntity flow = context.GetFlowById(component.ID);
                    string flowType = FlowEntity.GetModernFlowTypeLabel(
                        flow?.modernFlowType ?? FlowEntity.ModernFlowType.CloudFlow);
                    string triggerInfo = (flow?.trigger != null)
                        ? flow.trigger.Name + ": " + flow.trigger.Type
                        : "";
                    return (flowName, triggerInfo, flowType);
                }
            }
            FlowEntity flowEntity = flows?.FirstOrDefault(f =>
                f.ID != null && f.ID.Trim('{', '}').Equals(component.ID?.Trim('{', '}'), StringComparison.OrdinalIgnoreCase));
            if (flowEntity != null)
            {
                string flowType = FlowEntity.GetModernFlowTypeLabel(flowEntity.modernFlowType);
                string triggerInfo = (flowEntity.trigger != null)
                    ? flowEntity.trigger.Name + ": " + flowEntity.trigger.Type
                    : "";
                return (flowEntity.Name, triggerInfo, flowType);
            }
            return (solution.GetDisplayNameForComponent(component), "", "");
        }
    }
}
