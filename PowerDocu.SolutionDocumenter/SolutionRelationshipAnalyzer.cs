using System;
using System.Collections.Generic;
using System.Linq;
using PowerDocu.Common;

namespace PowerDocu.SolutionDocumenter
{
    public static class SolutionRelationshipAnalyzer
    {
        private static readonly HashSet<string> DataverseConnectorNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "commondataserviceforapps",
            "commondataservice"
        };

        public static (List<ComponentRelationship> Relationships, HashSet<SolutionComponentNode> AllComponents) Analyze(SolutionDocumentationContent content)
        {
            var relationships = new List<ComponentRelationship>();
            var allComponents = new HashSet<SolutionComponentNode>();

            CollectAllComponents(content, allComponents);
            CollectAgentRelationships(content, relationships);
            CollectFlowRelationships(content, relationships);
            CollectAppRelationships(content, relationships);
            CollectTableRelationships(content, relationships);
            CollectModelDrivenAppRelationships(content, relationships);
            CollectBPFRelationships(content, relationships);
            CollectDesktopFlowRelationships(content, relationships);
            //CollectDataflowRelationships(content, relationships);

            NormalizeTableNames(content, relationships, allComponents);

            return (relationships, allComponents);
        }

        private static void CollectAllComponents(SolutionDocumentationContent content, HashSet<SolutionComponentNode> allComponents)
        {
            foreach (var agent in content.agents)
            {
                allComponents.Add(new SolutionComponentNode("Agent", agent.Name));
            }
            foreach (var flow in content.flows)
            {
                allComponents.Add(new SolutionComponentNode("Flow", flow.Name));
            }
            foreach (var app in content.apps)
            {
                allComponents.Add(new SolutionComponentNode("Canvas App", app.Name));
            }
            foreach (var appModule in content.appModules)
            {
                allComponents.Add(new SolutionComponentNode("Model-Driven App", appModule.GetDisplayName()));
            }
            foreach (var bpf in content.businessProcessFlows)
            {
                allComponents.Add(new SolutionComponentNode("Business Process Flow", bpf.GetDisplayName()));
            }
            foreach (var desktopFlow in content.desktopFlows)
            {
                allComponents.Add(new SolutionComponentNode("Desktop Flow", desktopFlow.GetDisplayName()));
            }
            /*foreach (var dataflow in content.dataflows)
            {
                allComponents.Add(new SolutionComponentNode("Dataflow", dataflow.GetDisplayName()));
            }*/
            if (content.solution?.Customizations != null)
            {
                foreach (var table in content.solution.Customizations.getEntities())
                {
                    string name = table.getLocalizedName();
                    if (string.IsNullOrEmpty(name)) name = table.getName();
                    allComponents.Add(new SolutionComponentNode("Table", name));
                }
                foreach (var aiModel in content.solution.Customizations.getAIModels())
                {
                    allComponents.Add(new SolutionComponentNode("AI Model", aiModel.getName()));
                }
            }
            if (content.solution != null)
            {
                foreach (var envVar in content.solution.EnvironmentVariables)
                {
                    allComponents.Add(new SolutionComponentNode("Environment Variable", envVar.DisplayName ?? envVar.Name));
                }
                // Add remaining component types that aren't explicitly parsed
                foreach (var comp in content.solution.Components)
                {
                    string displayName = content.GetDisplayNameForComponent(comp);
                    switch (comp.Type)
                    {
                        case "Workflow":
                        case "Entity":
                        case "Canvas App":
                        case "AI Project":
                            // Already added above via parsed entities
                            break;
                        case "Connector":
                        case "Connection Reference":
                            allComponents.Add(new SolutionComponentNode("Connector", ConnectorHelper.ResolveConnectorDisplayName(displayName)));
                            break;
                        case "Role":
                            allComponents.Add(new SolutionComponentNode("Security Role", displayName));
                            break;
                        case "Option Set":
                            allComponents.Add(new SolutionComponentNode("Option Set", displayName));
                            break;
                        default:
                            allComponents.Add(new SolutionComponentNode(comp.Type, displayName));
                            break;
                    }
                }
            }
        }

        private static void CollectAgentRelationships(SolutionDocumentationContent content, List<ComponentRelationship> relationships)
        {
            foreach (var agent in content.agents)
            {
                // Agent → Flow (tools)
                foreach (var toolInfo in agent.GetAllToolInfos())
                {
                    if (!string.IsNullOrEmpty(toolInfo.FlowId))
                    {
                        string flowName = content.context?.GetFlowNameById(toolInfo.FlowId);
                        if (!string.IsNullOrEmpty(flowName))
                        {
                            relationships.Add(new ComponentRelationship("Agent", agent.Name, "Flow", flowName, "uses as tool"));
                        }
                    }
                    if (!string.IsNullOrEmpty(toolInfo.ConnectionReference))
                    {
                        string connectorName = ConnectorHelper.ResolveConnectorDisplayName(toolInfo.ConnectionReference);
                        relationships.Add(new ComponentRelationship("Agent", agent.Name, "Connector", connectorName, "uses connector"));
                    }
                }

                // Agent → Agent (connected agents)
                foreach (var connectedAgentInfo in agent.GetAllConnectedAgentInfos())
                {
                    string targetName = connectedAgentInfo.Name;
                    if (!string.IsNullOrEmpty(connectedAgentInfo.BotSchemaName) && content.context != null)
                    {
                        string resolved = content.context.GetAgentNameBySchemaName(connectedAgentInfo.BotSchemaName);
                        if (!string.IsNullOrEmpty(resolved)) targetName = resolved;
                    }
                    relationships.Add(new ComponentRelationship("Agent", agent.Name, "Agent", targetName, "invokes agent"));
                }

                // Agent → Table (knowledge sources)
                foreach (var dvTable in agent.DvTableSearchEntities)
                {
                    if (!string.IsNullOrEmpty(dvTable.EntityLogicalName))
                    {
                        string tableName = content.context?.GetTableDisplayName(dvTable.EntityLogicalName);
                        if (string.IsNullOrEmpty(tableName)) tableName = dvTable.EntityLogicalName;
                        relationships.Add(new ComponentRelationship("Agent", agent.Name, "Table", tableName, "knowledge source"));
                    }
                }

                // Agent → AI Model (via AI Plugin Operations)
                var addedAIModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var operation in agent.AIPluginOperations)
                {
                    if (string.IsNullOrEmpty(operation.AIModelId))
                        continue;

                    string trimmedId = operation.AIModelId.Trim('{', '}');
                    var model = agent.AIModels.FirstOrDefault(m =>
                        m.getID().Trim('{', '}').Equals(trimmedId, StringComparison.OrdinalIgnoreCase));
                    if (model == null)
                        continue;

                    string aiModelName = model.getName();
                    if (!string.IsNullOrEmpty(aiModelName) && addedAIModels.Add(aiModelName))
                    {
                        relationships.Add(new ComponentRelationship("Agent", agent.Name, "AI Model", aiModelName, "uses AI model"));
                    }
                }
            }
        }

        private static void CollectFlowRelationships(SolutionDocumentationContent content, List<ComponentRelationship> relationships)
        {
            foreach (var flow in content.flows)
            {
                foreach (var connRef in flow.connectionReferences)
                {
                    if (!string.IsNullOrEmpty(connRef.Name))
                    {
                        string connectorName = ConnectorHelper.ResolveConnectorDisplayName(connRef.Name);
                        relationships.Add(new ComponentRelationship("Flow", flow.Name, "Connector", connectorName, "uses connector"));
                    }
                }

                // Flow → Table (from Dataverse actions and trigger)
                var addedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var actionNode in flow.actions.ActionNodes)
                {
                    if (string.IsNullOrEmpty(actionNode.Connection)) continue;
                    if (!DataverseConnectorNames.Contains(actionNode.Connection)) continue;

                    string entityName = ExtractEntityNameFromInputs(actionNode.actionInputs);
                    if (!string.IsNullOrEmpty(entityName) && addedTables.Add(entityName))
                    {
                        string tableName = content.context?.GetTableDisplayName(entityName);
                        if (string.IsNullOrEmpty(tableName)) tableName = entityName;
                        relationships.Add(new ComponentRelationship("Flow", flow.Name, "Table", tableName, "uses table"));
                    }
                }

                if (flow.trigger != null && !string.IsNullOrEmpty(flow.trigger.Connector)
                    && DataverseConnectorNames.Contains(flow.trigger.Connector))
                {
                    string entityName = ExtractEntityNameFromInputs(flow.trigger.Inputs);
                    if (!string.IsNullOrEmpty(entityName) && addedTables.Add(entityName))
                    {
                        string tableName = content.context?.GetTableDisplayName(entityName);
                        if (string.IsNullOrEmpty(tableName)) tableName = entityName;
                        relationships.Add(new ComponentRelationship("Flow", flow.Name, "Table", tableName, "uses table"));
                    }
                }

                // Flow → Flow (child flow calls via Workflow-type actions)
                var addedChildFlows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var actionNode in flow.actions.ActionNodes)
                {
                    if (!"Workflow".Equals(actionNode.Type, StringComparison.OrdinalIgnoreCase)) continue;

                    foreach (var inputExpr in actionNode.actionInputs)
                    {
                        if (!inputExpr.expressionOperator.Equals("host", StringComparison.OrdinalIgnoreCase)) continue;

                        string workflowRef = FindExpressionStringValue(inputExpr, "workflowReferenceName");
                        if (string.IsNullOrEmpty(workflowRef)) continue;

                        string childFlowName = content.context?.GetFlowNameById(workflowRef);
                        if (!string.IsNullOrEmpty(childFlowName) && addedChildFlows.Add(childFlowName))
                        {
                            relationships.Add(new ComponentRelationship("Flow", flow.Name, "Flow", childFlowName, "calls flow"));
                        }
                    }
                }
            }
        }

        private static void CollectAppRelationships(SolutionDocumentationContent content, List<ComponentRelationship> relationships)
        {
            // Build lookup to resolve canvas app flow references to parsed flow names
            // (canvas apps may store flow names with special chars like '>' stripped)
            var flowsBySafeName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var flow in content.flows)
            {
                string safeName = CharsetHelper.GetSafeName(flow.Name);
                if (!flowsBySafeName.ContainsKey(safeName))
                    flowsBySafeName[safeName] = flow.Name;
            }

            // Build lookup of known table names for matching data sources to tables
            var tablesBySchemaName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var tablesByDisplayName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (content.solution?.Customizations != null)
            {
                foreach (var table in content.solution.Customizations.getEntities())
                {
                    string displayName = table.getLocalizedName();
                    if (string.IsNullOrEmpty(displayName)) displayName = table.getName();
                    tablesBySchemaName[table.getName()] = displayName;
                    if (!string.IsNullOrEmpty(table.getLocalizedName()))
                        tablesByDisplayName[table.getLocalizedName()] = displayName;
                }
            }

            foreach (var app in content.apps)
            {
                foreach (var ds in app.DataSources)
                {
                    //we skip sample data sources, auxiliary data sources (such as views), and collections
                    if (ds.isSampleDataSource()) continue;
                    if (ds.isAuxiliaryDataSource()) continue;
                    if (string.IsNullOrEmpty(ds.Name)) continue;
                    if (ds.Type == "CollectionDataSourceInfo") continue;
                    // Check if this data source is a Power Automate flow
                    if (IsFlowDataSource(ds))
                    {
                        string flowName = ds.Name;
                        string safeDsName = CharsetHelper.GetSafeName(ds.Name);
                        if (flowsBySafeName.TryGetValue(safeDsName, out string resolvedName))
                            flowName = resolvedName;
                        relationships.Add(new ComponentRelationship("Canvas App", app.Name, "Flow", flowName, "calls flow"));
                        continue;
                    }
                    // Try to match this data source to a known Dataverse table
                    string matchedTableName = TryMatchDataSourceToTable(ds, tablesBySchemaName, tablesByDisplayName);
                    if (matchedTableName != null)
                    {
                        relationships.Add(new ComponentRelationship("Canvas App", app.Name, "Table", matchedTableName, "data source"));
                    }
                    else
                    {
                        relationships.Add(new ComponentRelationship("Canvas App", app.Name, "Data Source", ds.Name, "data source"));
                    }
                }
            }
        }

        private static string TryMatchDataSourceToTable(DataSource ds, Dictionary<string, string> tablesBySchemaName, Dictionary<string, string> tablesByDisplayName)
        {
            // 1) Check TableDefinition property for a Dataverse logical name (most reliable)
            var tableDefProp = ds.Properties.FirstOrDefault(p => p.expressionOperator == "TableDefinition");
            if (tableDefProp != null)
            {
                var info = TableDefinitionHelper.Parse(tableDefProp);
                if (info != null && !string.IsNullOrEmpty(info.LogicalName))
                {
                    if (tablesBySchemaName.TryGetValue(info.LogicalName, out string displayName))
                        return displayName;
                    // Table exists in the app but not in this solution — still a known Dataverse table
                    return info.DisplayName ?? info.LogicalName;
                }
            }

            // 2) Match data source name directly against table schema or display names
            if (tablesBySchemaName.TryGetValue(ds.Name, out string matched))
                return matched;
            if (tablesByDisplayName.TryGetValue(ds.Name, out matched))
                return matched;

            return null;
        }

        private static void CollectTableRelationships(SolutionDocumentationContent content, List<ComponentRelationship> relationships)
        {
            if (content.solution?.Customizations == null) return;

            var tables = content.solution.Customizations.getEntities();
            var entityRelationships = content.solution.Customizations.getEntityRelationships();

            foreach (var er in entityRelationships.Where(o => o.getRelationshipType().Equals("ManyToMany")))
            {
                string firstName = ResolveTableDisplayName(tables, er.getFirstEntityName());
                string secondName = ResolveTableDisplayName(tables, er.getSecondEntityName());
                relationships.Add(new ComponentRelationship("Table", firstName, "Table", secondName, "many-to-many"));
            }

            // One-to-many via lookup columns
            foreach (var table in tables)
            {
                string tableName = table.getLocalizedName();
                if (string.IsNullOrEmpty(tableName)) tableName = table.getName();

                foreach (var lookupCol in table.GetColumns().Where(c => c.isNonDefaultLookUpColumn()))
                {
                    var er = entityRelationships
                        .Where(o => o.getReferencingAttributeName().Equals(lookupCol.getLogicalName(), StringComparison.OrdinalIgnoreCase))
                        .FirstOrDefault();
                    if (er != null)
                    {
                        string referencedName = ResolveTableDisplayName(tables, er.getReferencedEntityName());
                        relationships.Add(new ComponentRelationship("Table", tableName, "Table", referencedName, "lookup"));
                    }
                }
            }
        }

        private static void CollectModelDrivenAppRelationships(SolutionDocumentationContent content, List<ComponentRelationship> relationships)
        {
            foreach (var appModule in content.appModules)
            {
                string appName = appModule.GetDisplayName();

                // Tables referenced via components
                foreach (var tableComp in appModule.GetTables())
                {
                    string tableName = content.context?.GetTableDisplayName(tableComp.SchemaName);
                    if (string.IsNullOrEmpty(tableName)) tableName = tableComp.SchemaName;
                    relationships.Add(new ComponentRelationship("Model-Driven App", appName, "Table", tableName, "uses table"));
                }

                // Tables referenced via sitemap sub-areas
                if (appModule.SiteMap != null)
                {
                    foreach (var area in appModule.SiteMap.Areas)
                    {
                        foreach (var group in area.Groups)
                        {
                            foreach (var subArea in group.SubAreas)
                            {
                                if (!string.IsNullOrEmpty(subArea.Entity))
                                {
                                    string tableName = content.context?.GetTableDisplayName(subArea.Entity);
                                    if (string.IsNullOrEmpty(tableName)) tableName = subArea.Entity;
                                    relationships.Add(new ComponentRelationship("Model-Driven App", appName, "Table", tableName, "navigates to"));
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void CollectBPFRelationships(SolutionDocumentationContent content, List<ComponentRelationship> relationships)
        {
            // Build lookup of known table names for resolving entity logical names
            var tablesBySchemaName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (content.solution?.Customizations != null)
            {
                foreach (var table in content.solution.Customizations.getEntities())
                {
                    string displayName = table.getLocalizedName();
                    if (string.IsNullOrEmpty(displayName)) displayName = table.getName();
                    tablesBySchemaName[table.getName()] = displayName;
                }
            }

            foreach (var bpf in content.businessProcessFlows)
            {
                string bpfName = bpf.GetDisplayName();

                // BPF → Table (primary entity)
                if (!string.IsNullOrEmpty(bpf.PrimaryEntity))
                {
                    string tableName = tablesBySchemaName.TryGetValue(bpf.PrimaryEntity, out string resolved)
                        ? resolved : bpf.PrimaryEntity;
                    relationships.Add(new ComponentRelationship("Business Process Flow", bpfName, "Table", tableName, "primary entity"));
                }

                // BPF → Table (cross-entity stages that reference different entities)
                var addedEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(bpf.PrimaryEntity))
                    addedEntities.Add(bpf.PrimaryEntity);

                foreach (var stage in bpf.Stages)
                {
                    if (!string.IsNullOrEmpty(stage.EntityName) && addedEntities.Add(stage.EntityName))
                    {
                        string tableName = tablesBySchemaName.TryGetValue(stage.EntityName, out string resolved)
                            ? resolved : stage.EntityName;
                        relationships.Add(new ComponentRelationship("Business Process Flow", bpfName, "Table", tableName, "stage entity"));
                    }
                }
            }
        }

        private static void CollectDesktopFlowRelationships(SolutionDocumentationContent content, List<ComponentRelationship> relationships)
        {
            foreach (var desktopFlow in content.desktopFlows)
            {
                string flowName = desktopFlow.GetDisplayName();

                // Desktop Flow → Connector
                foreach (var connector in desktopFlow.Connectors)
                {
                    string connectorName = !string.IsNullOrEmpty(connector.Title) ? connector.Title
                        : !string.IsNullOrEmpty(connector.Name) ? connector.Name
                        : connector.ConnectorId;
                    if (!string.IsNullOrEmpty(connectorName))
                    {
                        relationships.Add(new ComponentRelationship("Desktop Flow", flowName, "Connector", connectorName, "uses connector"));
                    }
                }

                // Desktop Flow → Environment Variable
                foreach (var envVar in desktopFlow.EnvironmentVariables)
                {
                    if (!string.IsNullOrEmpty(envVar.Name))
                    {
                        relationships.Add(new ComponentRelationship("Desktop Flow", flowName, "Environment Variable", envVar.Name, "uses env variable"));
                    }
                }
            }
        }

        private static void CollectDataflowRelationships(SolutionDocumentationContent content, List<ComponentRelationship> relationships)
        {
            //TODO: this is broken in main branch as content doesn't have dataFlows

            //foreach (var dataflow in content.dataFlows)
            //{
            //    foreach (var query in dataflow.Queries)
            //    {
            //        if (!string.IsNullOrEmpty(query.EntityName))
            //        {
            //            string tableName = content.context?.GetTableDisplayName(query.EntityName);
            //            if (string.IsNullOrEmpty(tableName)) tableName = query.EntityName;
            //            relationships.Add(new ComponentRelationship("Dataflow", dataflow.GetDisplayName(), "Table", tableName, "loads into"));
            //        }
            //    }
            //}
        }

        private static bool IsFlowDataSource(DataSource ds)
        {
            var apiIdExpr = ds.Properties.FirstOrDefault(p =>
                p.expressionOperator.Equals("ApiId", StringComparison.OrdinalIgnoreCase));
            if (apiIdExpr != null && apiIdExpr.expressionOperands.Count > 0
                && apiIdExpr.expressionOperands[0] is string apiIdValue)
            {
                return apiIdValue.IndexOf("logicflows", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return false;
        }

        private static Expression FindChildExpression(Expression parent, string operatorName)
        {
            foreach (object operand in parent.expressionOperands)
            {
                if (operand is Expression expr && expr.expressionOperator.Equals(operatorName, StringComparison.OrdinalIgnoreCase))
                    return expr;
            }
            return null;
        }

        private static string FindExpressionStringValue(Expression parent, string operatorName)
        {
            Expression child = FindChildExpression(parent, operatorName);
            if (child != null && child.expressionOperands.Count > 0 && child.expressionOperands[0] is string val)
                return val;
            return null;
        }

        private static string ExtractEntityNameFromInputs(List<Expression> inputs)
        {
            foreach (var expr in inputs)
            {
                if (expr.expressionOperator.Equals("parameters", StringComparison.OrdinalIgnoreCase))
                {
                    string entityName = FindExpressionStringValue(expr, "entityName");
                    if (!string.IsNullOrEmpty(entityName))
                        return entityName;
                    // Some actions use "tableName" instead of "entityName"
                    string tableName = FindExpressionStringValue(expr, "tableName");
                    if (!string.IsNullOrEmpty(tableName))
                        return tableName;
                    // Dataverse webhook triggers use "subscriptionRequest/entityname"
                    string subscriptionEntity = FindExpressionStringValue(expr, "subscriptionRequest/entityname");
                    if (!string.IsNullOrEmpty(subscriptionEntity))
                        return subscriptionEntity;
                    // Dataverse polling triggers use "table"
                    string pollingTable = FindExpressionStringValue(expr, "table");
                    if (!string.IsNullOrEmpty(pollingTable))
                        return pollingTable;
                }
            }
            return null;
        }

        private static string ResolveTableDisplayName(List<TableEntity> tables, string schemaName)
        {
            var table = tables.Find(t => t.getName().Equals(schemaName, StringComparison.OrdinalIgnoreCase));
            if (table != null)
            {
                string localized = table.getLocalizedName();
                if (!string.IsNullOrEmpty(localized)) return localized;
            }
            return schemaName;
        }

        /// <summary>
        /// Post-processes relationships and allComponents to ensure each Dataverse table
        /// is represented by a single canonical name (display name preferred over schema name).
        /// This prevents duplicate graph nodes when different collectors resolve the same table
        /// to different names (e.g., display name from a canvas app vs. schema name from a flow).
        /// </summary>
        private static void NormalizeTableNames(
            SolutionDocumentationContent content,
            List<ComponentRelationship> relationships,
            HashSet<SolutionComponentNode> allComponents)
        {
            var schemaToDisplay = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Source A (priority): customizations.xml entities — matches CollectAllComponents logic
            if (content.solution?.Customizations != null)
            {
                foreach (var table in content.solution.Customizations.getEntities())
                {
                    string schemaName = table.getName();
                    string displayName = table.getLocalizedName();
                    if (string.IsNullOrEmpty(displayName)) displayName = schemaName;
                    if (!string.IsNullOrEmpty(schemaName))
                        schemaToDisplay[schemaName] = displayName;

                    // Also map EntitySetName (pluralized name used in flow Dataverse actions)
                    string entitySetName = table.GetEntitySetName();
                    if (!string.IsNullOrEmpty(entitySetName) && !schemaToDisplay.ContainsKey(entitySetName))
                        schemaToDisplay[entitySetName] = displayName;
                }
            }

            // Source B (fallback): Canvas app TableDefinition metadata — for tables not in the solution
            foreach (var app in content.apps)
            {
                foreach (var ds in app.DataSources)
                {
                    var tableDefProp = ds.Properties.FirstOrDefault(
                        p => p.expressionOperator == "TableDefinition");
                    if (tableDefProp != null)
                    {
                        var info = TableDefinitionHelper.Parse(tableDefProp);
                        if (info != null
                            && !string.IsNullOrEmpty(info.LogicalName)
                            && !string.IsNullOrEmpty(info.DisplayName)
                            && !schemaToDisplay.ContainsKey(info.LogicalName))
                        {
                            schemaToDisplay[info.LogicalName] = info.DisplayName;
                        }
                        // Also map EntitySetName from canvas app metadata
                        if (info != null
                            && !string.IsNullOrEmpty(info.EntitySetName)
                            && !string.IsNullOrEmpty(info.DisplayName)
                            && !schemaToDisplay.ContainsKey(info.EntitySetName))
                        {
                            schemaToDisplay[info.EntitySetName] = info.DisplayName;
                        }
                    }
                }
            }

            if (schemaToDisplay.Count == 0) return;

            // Normalize relationship endpoints
            foreach (var rel in relationships)
            {
                if (rel.SourceType == "Table"
                    && schemaToDisplay.TryGetValue(rel.SourceName, out string resolvedSource))
                    rel.SourceName = resolvedSource;
                if (rel.TargetType == "Table"
                    && schemaToDisplay.TryGetValue(rel.TargetName, out string resolvedTarget))
                    rel.TargetName = resolvedTarget;
            }

            // Normalize allComponents table entries
            var tablesToRemove = new List<SolutionComponentNode>();
            var tablesToAdd = new List<SolutionComponentNode>();
            foreach (var comp in allComponents)
            {
                if (comp.Type == "Table"
                    && schemaToDisplay.TryGetValue(comp.Name, out string resolved)
                    && !resolved.Equals(comp.Name, StringComparison.Ordinal))
                {
                    tablesToRemove.Add(comp);
                    tablesToAdd.Add(new SolutionComponentNode("Table", resolved));
                }
            }
            foreach (var comp in tablesToRemove) allComponents.Remove(comp);
            foreach (var comp in tablesToAdd) allComponents.Add(comp);
        }
    }
}
