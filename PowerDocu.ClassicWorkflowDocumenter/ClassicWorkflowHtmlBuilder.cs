using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PowerDocu.Common;

namespace PowerDocu.ClassicWorkflowDocumenter
{
    class ClassicWorkflowHtmlBuilder : HtmlBuilder
    {
        private readonly ClassicWorkflowDocumentationContent content;
        private readonly string mainFileName;
        private readonly string triggerActionsFileName;
        private string _navigationHtmlTop;
        private string _navigationHtmlSub;
        private string _metadataTableHtml;

        public ClassicWorkflowHtmlBuilder(ClassicWorkflowDocumentationContent contentDocumentation)
        {
            content = contentDocumentation;
            Directory.CreateDirectory(content.folderPath);
            WriteDefaultStylesheet(content.folderPath);
            Directory.CreateDirectory(Path.Combine(content.folderPath, "actions"));
            WriteDefaultStylesheet(Path.Combine(content.folderPath, "actions"));

            mainFileName = CollapseDashes(("workflow-" + content.filename + ".html").Replace(" ", "-"));
            triggerActionsFileName = CollapseDashes(("triggersactions-" + content.filename + ".html").Replace(" ", "-"));

            addMainPage();
            addTriggersActionsPage();
            addTriggerPage();
            addActionPages();
            NotificationHelper.SendNotification("Created HTML documentation for Classic Workflow: " + content.workflow.GetDisplayName());
        }

        // ── Navigation ──

        private string getNavigationHtml(bool fromSubfolder = false)
            => fromSubfolder
                ? (_navigationHtmlSub ??= BuildNavigationHtmlCore(true))
                : (_navigationHtmlTop ??= BuildNavigationHtmlCore(false));

        private string BuildNavigationHtmlCore(bool fromSubfolder)
        {
            string prefix = fromSubfolder ? "../" : "";
            var navItems = new List<(string label, string href)>();
            if (content.context?.Solution != null)
            {
                string solutionPrefix = fromSubfolder ? "../../" : "../";
                if (content.context?.Config?.documentSolution == true)
                    navItems.Add(("Solution", solutionPrefix + CrossDocLinkHelper.GetSolutionDocHtmlPath(content.context.Solution.UniqueName)));
                else
                    navItems.Add((content.context.Solution.UniqueName, ""));
            }
            navItems.AddRange(new (string label, string href)[]
            {
                ("Overview", prefix + mainFileName),
                ("Triggers & Actions", prefix + triggerActionsFileName),
                ("Table Relationships", prefix + mainFileName + "#table-relationships")
            });
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"<div class=\"nav-title\">{Encode(content.workflow.GetDisplayName())}</div>");
            sb.Append(NavigationList(navItems));
            return sb.ToString();
        }

        private string buildMetadataTable()
        {
            if (_metadataTableHtml != null) return _metadataTableHtml;
            StringBuilder sb = new StringBuilder();
            sb.Append(TableStart("Property", "Value"));
            sb.Append(TableRow("Name", content.workflow.GetDisplayName()));
            sb.Append(TableRow("Primary Table", content.GetTableDisplayName(content.workflow.PrimaryEntity)));
            sb.Append(TableRow("Category", content.workflow.GetCategoryLabel()));
            sb.Append(TableRow("Mode", content.workflow.GetModeLabel()));
            sb.Append(TableRow("Scope", content.workflow.GetScopeLabel()));
            sb.Append(TableRow("Run As", content.workflow.GetRunAsLabel()));
            sb.Append(TableRow("State", content.workflow.GetStateLabel()));
            sb.Append(TableRow("Is Customizable", content.workflow.IsCustomizable ? "Yes" : "No"));
            if (!string.IsNullOrEmpty(content.workflow.ID))
                sb.Append(TableRow("ID", content.workflow.ID));
            if (!string.IsNullOrEmpty(content.workflow.OwnerId))
                sb.Append(TableRow("Owner", content.workflow.OwnerId));
            if (!string.IsNullOrEmpty(content.workflow.Description))
                sb.Append(TableRow("Description", content.workflow.Description));
            if (!string.IsNullOrEmpty(content.workflow.IntroducedVersion))
                sb.Append(TableRow("Version", content.workflow.IntroducedVersion));
            sb.Append(TableRow("Number of Actions", CountAllSteps(content.workflow.Steps).ToString()));
            sb.Append(TableRow("Number of Conditions", CountConditions(content.workflow.Steps).ToString()));
            sb.Append(TableRow(content.headerDocumentationGenerated, PowerDocuReleaseHelper.GetTimestampWithVersion()));
            sb.Append(TableEnd());
            _metadataTableHtml = sb.ToString();
            return _metadataTableHtml;
        }

        // ── Main Index Page ──

        private void addMainPage()
        {
            StringBuilder body = new StringBuilder();

            // Overview
            body.AppendLine(HeadingWithId(1, content.workflow.GetDisplayName(), "overview"));
            body.AppendLine(buildMetadataTable());

            // Workflow diagram
            addWorkflowDiagram(body);

            // Table relationships
            if (content.workflow.TableReferences.Count > 0)
            {
                body.AppendLine(HeadingWithId(2, content.headerTableRelationships, "table-relationships"));
                body.AppendLine($"<p>This workflow references {content.workflow.TableReferences.Count} table relationship(s).</p>");
                body.Append(TableStart("Table Display Name", "Table Logical Name", "Reference Type"));
                foreach (var tableRef in content.workflow.TableReferences)
                {
                    string displayName = content.GetTableDisplayName(tableRef.TableLogicalName);
                    string displayCell;
                    if (content.context?.Solution != null && content.context?.Config?.documentSolution == true)
                    {
                        string anchor = CrossDocLinkHelper.GetSolutionTableHtmlAnchor(tableRef.TableLogicalName);
                        string solutionHtmlPath = CrossDocLinkHelper.GetSolutionDocHtmlPath(content.context.Solution.UniqueName);
                        displayCell = $"<a href=\"../{solutionHtmlPath}{anchor}\">{Encode(displayName)}</a>";
                    }
                    else
                    {
                        displayCell = Encode(displayName);
                    }
                    body.Append(TableRowRaw(displayCell, Encode(tableRef.TableLogicalName), Encode(tableRef.ReferenceType.ToString())));
                }
                body.AppendLine(TableEnd());
            }

            SaveHtmlFile(Path.Combine(content.folderPath, mainFileName),
                WrapInHtmlPage($"Classic Workflow - {content.workflow.GetDisplayName()}", body.ToString(), getNavigationHtml(false)));
        }

        private void AddActionLinksRecursive(StringBuilder body, List<ClassicWorkflowStep> steps, int depth)
        {
            foreach (var step in steps)
            {
                // ConditionBranch steps: show branch label in the list with children nested
                if (step.StepType == ClassicWorkflowStepType.ConditionBranch)
                {
                    string branchLabel = step.Name ?? "Condition Branch";
                    body.AppendLine($"<li><strong>{Encode(branchLabel)}</strong></li>");
                    if (step.ChildSteps.Count > 0)
                    {
                        body.AppendLine("<ul>");
                        AddActionLinksRecursive(body, step.ChildSteps, depth + 1);
                        body.AppendLine("</ul>");
                    }
                    continue;
                }

                string safeName = CharsetHelper.GetSafeName(step.Name ?? step.GetStepTypeLabel());
                string stepFileName = "actions/" + safeName + ".html";
                body.AppendLine(BulletItemRaw(Link(step.Name ?? step.GetStepTypeLabel(), stepFileName)
                    + $" <em>({step.GetStepTypeLabel()})</em>"));

                if (step.ChildSteps.Count > 0)
                {
                    body.AppendLine("<ul>");
                    AddActionLinksRecursive(body, step.ChildSteps, depth + 1);
                    body.AppendLine("</ul>");
                }
            }
        }

        // ── Triggers & Actions Overview Page ──

        private void addTriggersActionsPage()
        {
            StringBuilder body = new StringBuilder();
            body.AppendLine(Heading(1, content.workflow.GetDisplayName()));
            body.AppendLine(buildMetadataTable());

            // Trigger section with link
            body.AppendLine(Heading(2, "Trigger"));
            body.AppendLine(BulletListStart());
            body.AppendLine(BulletItemRaw(Link("Trigger", "actions/Trigger.html")));
            body.AppendLine(BulletListEnd());

            // Actions section with links
            body.AppendLine(Heading(2, "Actions"));
            if (content.workflow.Steps.Count > 0)
            {
                int totalSteps = CountAllSteps(content.workflow.Steps);
                body.AppendLine($"<p>There are a total of {totalSteps} action(s) in this workflow:</p>");
                body.AppendLine(BulletListStart());
                AddActionLinksRecursive(body, content.workflow.Steps, 0);
                body.AppendLine(BulletListEnd());
            }

            SaveHtmlFile(Path.Combine(content.folderPath, triggerActionsFileName),
                WrapInHtmlPage("Triggers & Actions - " + content.workflow.GetDisplayName(), body.ToString(), getNavigationHtml(false)));
        }

        // ── Trigger Page ──

        private void addTriggerPage()
        {
            StringBuilder body = new StringBuilder();
            body.AppendLine(Heading(1, content.workflow.GetDisplayName()));
            body.AppendLine(buildMetadataTable());
            body.AppendLine(Heading(2, "Trigger"));

            // Trigger type as bulleted list
            body.AppendLine("<h3>Trigger Type</h3>");
            body.AppendLine("<ul>");
            if (content.workflow.OnDemand)
                body.AppendLine("<li>On-Demand</li>");
            if (content.workflow.TriggerOnCreate)
                body.AppendLine("<li>Record Created</li>");
            if (content.workflow.TriggerOnDelete)
                body.AppendLine("<li>Record Deleted</li>");
            if (!string.IsNullOrEmpty(content.workflow.TriggerOnUpdateAttributeList))
            {
                body.AppendLine("<li>Record Updated<ul>");
                foreach (string field in content.workflow.TriggerOnUpdateAttributeList.Split(','))
                {
                    string trimmed = field.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        body.AppendLine($"<li>{Encode(content.GetFieldDisplayName(trimmed))}</li>");
                }
                body.AppendLine("</ul></li>");
            }
            if (!content.workflow.OnDemand && !content.workflow.TriggerOnCreate && !content.workflow.TriggerOnDelete && string.IsNullOrEmpty(content.workflow.TriggerOnUpdateAttributeList))
                body.AppendLine("<li>None</li>");
            body.AppendLine("</ul>");

            SaveHtmlFile(Path.Combine(content.folderPath, "actions", "Trigger.html"),
                WrapInHtmlPage("Trigger - " + content.workflow.GetDisplayName(), body.ToString(), getNavigationHtml(true)));
        }

        // ── Per-Action Pages ──

        private void addActionPages()
        {
            if (content.workflow.Steps.Count == 0) return;
            AddActionPagesRecursive(content.workflow.Steps);
        }

        private void AddActionPagesRecursive(List<ClassicWorkflowStep> steps)
        {
            // Build a filtered list of non-branch steps for prev/next navigation
            var navigableSteps = new List<ClassicWorkflowStep>();
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].StepType != ClassicWorkflowStepType.ConditionBranch)
                    navigableSteps.Add(steps[i]);
            }

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];

                // ConditionBranch steps are rendered inline on the parent page;
                // skip creating separate pages but still recurse into their children.
                if (step.StepType == ClassicWorkflowStepType.ConditionBranch)
                {
                    if (step.ChildSteps.Count > 0)
                        AddActionPagesRecursive(step.ChildSteps);
                    continue;
                }

                int navIndex = navigableSteps.IndexOf(step);
                ClassicWorkflowStep prevStep = (navIndex > 0) ? navigableSteps[navIndex - 1] : null;
                ClassicWorkflowStep nextStep = (navIndex >= 0 && navIndex + 1 < navigableSteps.Count) ? navigableSteps[navIndex + 1] : null;

            {
                string safeName = CharsetHelper.GetSafeName(step.Name ?? step.GetStepTypeLabel());
                string fileName = safeName + ".html";

                StringBuilder body = new StringBuilder();
                body.AppendLine(Heading(1, content.workflow.GetDisplayName()));
                body.AppendLine(buildMetadataTable());
                body.AppendLine(Heading(2, step.Name ?? step.GetStepTypeLabel()));

                // Action detail table
                body.Append(TableStart("Property", "Value"));
                body.Append(TableRow("Name", step.Name ?? ""));
                body.Append(TableRow("Type", step.GetStepTypeLabel()));
                if (!string.IsNullOrEmpty(step.StepDescription))
                    body.Append(TableRow("Description", step.StepDescription));
                if (!string.IsNullOrEmpty(step.TargetEntity))
                    body.Append(TableRow("Target Table", content.GetTableDisplayName(step.TargetEntity)));
                if (!string.IsNullOrEmpty(step.CustomActivityName))
                    body.Append(TableRow("Custom Activity", step.CustomActivityName));
                if (!string.IsNullOrEmpty(step.CustomActivityClass))
                    body.Append(TableRow("Class", step.CustomActivityClass));
                if (!string.IsNullOrEmpty(step.CustomActivityAssembly))
                    body.Append(TableRow("Assembly", step.CustomActivityAssembly));
                if (!string.IsNullOrEmpty(step.CustomActivityFriendlyName))
                    body.Append(TableRow("Friendly Name", step.CustomActivityFriendlyName));
                if (!string.IsNullOrEmpty(step.CustomActivityDescription))
                    body.Append(TableRow("Description", step.CustomActivityDescription));
                if (!string.IsNullOrEmpty(step.CustomActivityGroupName))
                    body.Append(TableRow("Group", step.CustomActivityGroupName));
                body.AppendLine(TableEnd());

                // Condition tree
                if (step.ConditionTree != null)
                {
                    body.AppendLine(Heading(3, "Condition"));
                    RenderConditionSectionHtml(body, step.ConditionTree);
                }
                else if (!string.IsNullOrEmpty(step.ConditionDescription))
                {
                    body.AppendLine(Heading(3, "Condition"));
                    body.AppendLine($"<p>{Encode(step.ConditionDescription)}</p>");
                }

                // Field assignments / Inputs
                if (step.Fields.Count > 0)
                {
                    string inputsLabel = step.StepType switch
                    {
                        ClassicWorkflowStepType.Custom => "Inputs",
                        ClassicWorkflowStepType.SendEmail => "Email Properties",
                        _ => "Field Assignments"
                    };
                    body.AppendLine(Heading(3, inputsLabel));
                    body.Append(TableStart("Field", "Value"));
                    foreach (var field in step.Fields)
                    {
                        body.Append(TableRow(field.FieldName ?? "", field.Value ?? ""));
                    }
                    body.AppendLine(TableEnd());
                }

                // Child steps / Subactions
                if (step.ChildSteps.Count > 0)
                {
                    if (step.StepType == ClassicWorkflowStepType.CheckCondition ||
                        step.StepType == ClassicWorkflowStepType.Wait)
                    {
                        // Render condition branches inline on the CheckCondition/Wait page
                        RenderConditionBranchesInline(body, step.ChildSteps);
                    }
                    else
                    {
                        body.AppendLine(Heading(3, "Subactions"));
                        body.AppendLine(BulletListStart());
                        foreach (var child in step.ChildSteps)
                        {
                            string childSafeName = CharsetHelper.GetSafeName(child.Name ?? child.GetStepTypeLabel());
                            body.AppendLine(BulletItemRaw(
                                Link(child.Name ?? child.GetStepTypeLabel(), childSafeName + ".html")
                                + $" <em>({child.GetStepTypeLabel()})</em>"));
                        }
                        body.AppendLine(BulletListEnd());
                    }
                }

                // Previous Action(s)
                if (prevStep != null)
                {
                    body.AppendLine(Heading(3, "Previous Action(s)"));
                    string prevSafeName = CharsetHelper.GetSafeName(prevStep.Name ?? prevStep.GetStepTypeLabel());
                    body.Append(TableStart("Previous Action"));
                    body.Append(TableRowRaw(
                        Link(prevStep.Name ?? prevStep.GetStepTypeLabel(), prevSafeName + ".html")
                        + $" <em>({prevStep.GetStepTypeLabel()})</em>"));
                    body.AppendLine(TableEnd());
                }

                // Next Action(s)
                if (nextStep != null)
                {
                    body.AppendLine(Heading(3, "Next Action(s)"));
                    string nextSafeName = CharsetHelper.GetSafeName(nextStep.Name ?? nextStep.GetStepTypeLabel());
                    body.Append(TableStart("Next Action"));
                    body.Append(TableRowRaw(
                        Link(nextStep.Name ?? nextStep.GetStepTypeLabel(), nextSafeName + ".html")
                        + $" <em>({nextStep.GetStepTypeLabel()})</em>"));
                    body.AppendLine(TableEnd());
                }

                SaveHtmlFile(Path.Combine(content.folderPath, "actions", fileName),
                    WrapInHtmlPage(step.GetStepTypeLabel() + " - " + (step.Name ?? ""), body.ToString(), getNavigationHtml(true)));

                // Recurse into child steps
                if (step.ChildSteps.Count > 0)
                    AddActionPagesRecursive(step.ChildSteps);
            }
            }
        }

        // ── Inline Condition Branch Rendering ──

        private void RenderConditionBranchesInline(StringBuilder body, List<ClassicWorkflowStep> branches)
        {
            foreach (var branch in branches)
            {
                if (branch.StepType != ClassicWorkflowStepType.ConditionBranch) continue;

                string branchLabel = branch.Name ?? "Condition Branch";
                string borderColor = branchLabel == "Otherwise (Default)" ? "#888" : "#0078d4";

                body.AppendLine($"<div style=\"border-left:4px solid {borderColor}; margin:12px 0; padding:8px 16px; background:#fafafa; border-radius:3px;\">");
                body.AppendLine($"<h3 style=\"margin-top:0; color:{borderColor};\">{Encode(branchLabel)}</h3>");

                // Show condition tree if present
                if (branch.ConditionTree != null)
                {
                    RenderConditionSectionHtml(body, branch.ConditionTree);
                }
                else if (!string.IsNullOrEmpty(branch.ConditionDescription) && branchLabel != "Otherwise (Default)")
                {
                    body.AppendLine($"<p>{Encode(branch.ConditionDescription)}</p>");
                }

                // Show the branch's child actions as links
                if (branch.ChildSteps.Count > 0)
                {
                    body.AppendLine("<p style=\"margin-top:8px; font-weight:bold; font-size:13px;\">Actions:</p>");
                    body.AppendLine(BulletListStart());
                    foreach (var child in branch.ChildSteps)
                    {
                        string childSafeName = CharsetHelper.GetSafeName(child.Name ?? child.GetStepTypeLabel());
                        body.AppendLine(BulletItemRaw(
                            Link(child.Name ?? child.GetStepTypeLabel(), childSafeName + ".html")
                            + $" <em>({child.GetStepTypeLabel()})</em>"));
                    }
                    body.AppendLine(BulletListEnd());
                }

                body.AppendLine("</div>");
            }
        }

        // ── Helpers ──

        private void addWorkflowDiagram(StringBuilder body)
        {
            string svgFile = "workflow.svg";
            string pngFile = "workflow.png";

            if (!File.Exists(Path.Combine(content.folderPath, pngFile))) return;

            body.AppendLine(HeadingWithId(2, "Workflow Diagram", "workflow-diagram"));
            body.AppendLine("<p>The following diagram shows the flow of the workflow including condition branches.</p>");

            if (File.Exists(Path.Combine(content.folderPath, svgFile)))
            {
                try
                {
                    string svgContent = File.ReadAllText(Path.Combine(content.folderPath, svgFile));
                    int svgStart = svgContent.IndexOf("<svg");
                    if (svgStart >= 0)
                        svgContent = svgContent.Substring(svgStart);
                    body.AppendLine("<div class=\"workflow-diagram\" style=\"overflow-x:auto;\">");
                    body.AppendLine(svgContent);
                    body.AppendLine("</div>");
                }
                catch
                {
                    body.AppendLine($"<p><img src=\"{pngFile}\" alt=\"{Encode(content.workflow.GetDisplayName())}\" style=\"max-width:100%;\" /></p>");
                }
            }
            else
            {
                body.AppendLine($"<p><img src=\"{pngFile}\" alt=\"{Encode(content.workflow.GetDisplayName())}\" style=\"max-width:100%;\" /></p>");
            }
        }

        private void RenderConditionTreeToHtml(StringBuilder body, ConditionExpression expr, int depth)
        {
            if (expr.IsLeaf)
            {
                // Parse field into entity and field parts: "Field Display on Entity Display"
                string entityPart = "";
                string fieldPart = expr.Field ?? "";
                if (fieldPart.Contains(" on "))
                {
                    int onIdx = fieldPart.IndexOf(" on ");
                    entityPart = fieldPart.Substring(onIdx + 4);
                    fieldPart = fieldPart.Substring(0, onIdx);
                }

                body.AppendLine("<tr>");
                body.AppendLine($"<td style=\"padding:4px 8px; color:#0078d4;\">{Encode(entityPart)}</td>");
                body.AppendLine($"<td style=\"padding:4px 8px; font-weight:500;\">{Encode(fieldPart)}</td>");
                body.AppendLine($"<td style=\"padding:4px 8px; color:#0078d4;\">{Encode(expr.Operator ?? "")}</td>");
                body.AppendLine($"<td style=\"padding:4px 8px;\">{Encode(expr.Value ?? "")}</td>");
                body.AppendLine("</tr>");
            }
            else
            {
                // Group: render with the operator label on top, children below
                // The parent operator is shown as a header, children are rows/sub-groups under it
                string opColor = expr.LogicalOperator == "OR" ? "#d83b01" : "#0078d4";

                foreach (var child in expr.Children)
                {
                    if (child.IsGroup)
                    {
                        // Nested group: render as bordered sub-section with its operator label
                        string childColor = child.LogicalOperator == "OR" ? "#d83b01" : "#0078d4";
                        body.AppendLine("<tr>");
                        body.AppendLine($"<td colspan=\"4\" style=\"padding:4px 0;\">");
                        body.AppendLine($"<div style=\"border:1px solid {childColor}; border-left:4px solid {childColor}; margin:4px 0 4px 16px; border-radius:3px;\">");
                        body.AppendLine($"<div style=\"font-weight:bold; color:white; background:{childColor}; font-size:11px; padding:3px 8px;\">▼ {Encode(child.LogicalOperator)}</div>");
                        body.AppendLine("<table style=\"border-collapse:collapse; width:100%;\">");
                        RenderConditionTreeToHtml(body, child, depth + 1);
                        body.AppendLine("</table>");
                        body.AppendLine("</div></td>");
                        body.AppendLine("</tr>");
                    }
                    else
                    {
                        // Leaf child: just render as a row
                        RenderConditionTreeToHtml(body, child, depth);
                    }
                }
            }
        }

        private void RenderConditionSectionHtml(StringBuilder body, ConditionExpression expr)
        {
            string rootOp = expr.IsGroup ? expr.LogicalOperator : "";
            string rootColor = rootOp == "OR" ? "#d83b01" : "#0078d4";

            body.AppendLine("<div style=\"border:1px solid #e0e0e0; border-radius:4px; overflow:hidden; background:#fafafa;\">");
            if (expr.IsGroup)
            {
                body.AppendLine($"<div style=\"font-weight:bold; color:white; background:{rootColor}; padding:6px 12px; font-size:13px;\">▼ {Encode(rootOp)}</div>");
            }
            body.AppendLine("<table style=\"border-collapse:collapse; width:100%;\">");
            body.AppendLine("<tr style=\"background:#f0f0f0; font-weight:bold; font-size:12px;\">");
            body.AppendLine("<td style=\"padding:4px 8px;\">Entity</td>");
            body.AppendLine("<td style=\"padding:4px 8px;\">Field</td>");
            body.AppendLine("<td style=\"padding:4px 8px;\">Operator</td>");
            body.AppendLine("<td style=\"padding:4px 8px;\">Value</td>");
            body.AppendLine("</tr>");
            RenderConditionTreeToHtml(body, expr, 0);
            body.AppendLine("</table>");
            body.AppendLine("</div>");
        }

        private string ResolveFieldList(string commaDelimitedFields)
        {
            var parts = commaDelimitedFields.Split(',');
            var resolved = new List<string>();
            foreach (string field in parts)
            {
                string trimmed = field.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    resolved.Add(content.GetFieldDisplayName(trimmed));
            }
            return string.Join(", ", resolved);
        }

        private static string CollapseDashes(string s)
        {
            while (s.Contains("--"))
                s = s.Replace("--", "-");
            return s;
        }

        private static int CountAllSteps(List<ClassicWorkflowStep> steps)
        {
            int count = 0;
            foreach (var step in steps)
            {
                count++;
                count += CountAllSteps(step.ChildSteps);
            }
            return count;
        }

        private static int CountConditions(List<ClassicWorkflowStep> steps)
        {
            int count = 0;
            foreach (var step in steps)
            {
                if (step.StepType == ClassicWorkflowStepType.CheckCondition ||
                    step.StepType == ClassicWorkflowStepType.Wait)
                    count++;
                count += CountConditions(step.ChildSteps);
            }
            return count;
        }
    }
}
