using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerDocu.Common;
using Grynwald.MarkdownGenerator;

namespace PowerDocu.ClassicWorkflowDocumenter
{
    class ClassicWorkflowMarkdownBuilder : MarkdownBuilder
    {
        private readonly ClassicWorkflowDocumentationContent content;
        private readonly string mainDocumentFileName;
        private readonly string triggerActionsFileName;
        private readonly MdDocument mainDocument;
        private readonly MdDocument triggerActionsDocument;
        private readonly DocumentSet<MdDocument> set;

        public ClassicWorkflowMarkdownBuilder(ClassicWorkflowDocumentationContent contentDocumentation)
        {
            content = contentDocumentation;
            Directory.CreateDirectory(content.folderPath);
            mainDocumentFileName = ("index-" + content.filename + ".md").Replace(" ", "-");
            triggerActionsFileName = ("triggersactions-" + content.filename + ".md").Replace(" ", "-");
            set = new DocumentSet<MdDocument>();
            mainDocument = set.CreateMdDocument(mainDocumentFileName);
            triggerActionsDocument = set.CreateMdDocument(triggerActionsFileName);

            addOverview();
            addWorkflowDiagram();
            addTriggerAndActionsLinks();
            addTableRelationships();

            addTriggerPage();
            addActionPages();

            set.Save(content.folderPath);
            NotificationHelper.SendNotification("Created Markdown documentation for Classic Workflow: " + content.workflow.GetDisplayName());
        }

        // ── Main Document ──

        private void addOverview()
        {
            mainDocument.Root.Add(new MdHeading(content.workflow.GetDisplayName(), 1));

            if (content.context?.Solution != null)
            {
                if (content.context?.Config?.documentSolution == true)
                    mainDocument.Root.Add(new MdParagraph(new MdCompositeSpan(new MdTextSpan("Solution: "), new MdLinkSpan(content.context.Solution.UniqueName, "../" + CrossDocLinkHelper.GetSolutionDocMdPath(content.context.Solution.UniqueName)))));
                else
                    mainDocument.Root.Add(new MdParagraph(new MdTextSpan("Solution: " + content.context.Solution.UniqueName)));
            }

            List<MdTableRow> tableRows = new List<MdTableRow>
            {
                new MdTableRow("Name", content.workflow.GetDisplayName()),
                new MdTableRow("Primary Table", content.GetTableDisplayName(content.workflow.PrimaryEntity)),
                new MdTableRow("Category", content.workflow.GetCategoryLabel()),
                new MdTableRow("Mode", content.workflow.GetModeLabel()),
                new MdTableRow("Scope", content.workflow.GetScopeLabel()),
                new MdTableRow("Run As", content.workflow.GetRunAsLabel()),
                new MdTableRow("State", content.workflow.GetStateLabel()),
                new MdTableRow("Is Customizable", content.workflow.IsCustomizable ? "Yes" : "No"),
                new MdTableRow("Number of Actions", CountAllSteps(content.workflow.Steps).ToString()),
                new MdTableRow("Number of Conditions", CountConditions(content.workflow.Steps).ToString()),
                new MdTableRow(content.headerDocumentationGenerated, PowerDocuReleaseHelper.GetTimestampWithVersion())
            };
            if (!string.IsNullOrEmpty(content.workflow.ID))
                tableRows.Insert(tableRows.Count - 1, new MdTableRow("ID", content.workflow.ID));
            if (!string.IsNullOrEmpty(content.workflow.OwnerId))
                tableRows.Insert(tableRows.Count - 1, new MdTableRow("Owner", content.workflow.OwnerId));
            if (!string.IsNullOrEmpty(content.workflow.Description))
                tableRows.Insert(tableRows.Count - 1, new MdTableRow("Description", content.workflow.Description));
            if (!string.IsNullOrEmpty(content.workflow.IntroducedVersion))
                tableRows.Insert(tableRows.Count - 1, new MdTableRow("Version", content.workflow.IntroducedVersion));

            mainDocument.Root.Add(new MdTable(new MdTableRow("Property", "Value"), tableRows));
        }

        private void addWorkflowDiagram()
        {
            string pngFile = "workflow.png";
            if (!System.IO.File.Exists(content.folderPath + pngFile)) return;

            mainDocument.Root.Add(new MdHeading("Workflow Diagram", 2));
            mainDocument.Root.Add(new MdParagraph(new MdTextSpan("The following diagram shows the flow of the workflow including condition branches.")));
            mainDocument.Root.Add(new MdParagraph(new MdImageSpan(content.workflow.GetDisplayName(), pngFile)));
        }

        private void addTriggerAndActionsLinks()
        {
            // Single "Triggers & Actions" link on the main page (matching Flow documenter pattern)
            mainDocument.Root.Add(new MdHeading("Triggers & Actions", 2));
            mainDocument.Root.Add(new MdParagraph(new MdLinkSpan("View Triggers & Actions →", triggerActionsFileName)));
        }

        private void AddActionLinkItems(List<ClassicWorkflowStep> steps, List<MdListItem> items)
        {
            foreach (var step in steps)
            {
                string anchor = CharsetHelper.GetSafeName(step.Name ?? step.GetStepTypeLabel()).ToLowerInvariant().Replace(" ", "-");
                string label = (step.Name ?? step.GetStepTypeLabel()) + " (" + step.GetStepTypeLabel() + ")";
                items.Add(new MdListItem(new MdParagraph(new MdLinkSpan(label, triggerActionsFileName + "#" + anchor))));

                if (step.ChildSteps.Count > 0)
                    AddActionLinkItems(step.ChildSteps, items);
            }
        }

        private void addTableRelationships()
        {
            if (content.workflow.TableReferences.Count == 0) return;

            mainDocument.Root.Add(new MdHeading(content.headerTableRelationships, 2));
            mainDocument.Root.Add(new MdParagraph(new MdTextSpan($"This workflow references {content.workflow.TableReferences.Count} table relationship(s).")));

            List<MdTableRow> tableRows = new List<MdTableRow>();
            foreach (var tableRef in content.workflow.TableReferences)
            {
                string displayName = content.GetTableDisplayName(tableRef.TableLogicalName);
                MdSpan displaySpan;
                if (content.context?.Solution != null && content.context?.Config?.documentSolution == true)
                {
                    string anchor = CrossDocLinkHelper.GetSolutionTableMdAnchor(displayName, tableRef.TableLogicalName);
                    string solutionMdPath = CrossDocLinkHelper.GetSolutionDocMdPath(content.context.Solution.UniqueName);
                    displaySpan = new MdLinkSpan(displayName, "../" + solutionMdPath + anchor);
                }
                else
                {
                    displaySpan = new MdTextSpan(displayName);
                }
                tableRows.Add(new MdTableRow(
                    new MdCompositeSpan(displaySpan),
                    new MdTextSpan(tableRef.TableLogicalName),
                    new MdTextSpan(tableRef.ReferenceType.ToString())
                ));
            }
            mainDocument.Root.Add(new MdTable(
                new MdTableRow("Table Display Name", "Table Logical Name", "Reference Type"),
                tableRows));
        }

        // ── Triggers & Actions Document ──

        private void addTriggerPage()
        {
            triggerActionsDocument.Root.Add(new MdHeading(content.workflow.GetDisplayName(), 1));
            triggerActionsDocument.Root.Add(new MdParagraph(new MdLinkSpan("← Back to Overview", mainDocumentFileName)));
            triggerActionsDocument.Root.Add(new MdHeading("Trigger", 2));

            List<MdTableRow> triggerRows = new List<MdTableRow>
            {
                new MdTableRow("Primary Table", content.GetTableDisplayName(content.workflow.PrimaryEntity)),
                new MdTableRow("Mode", content.workflow.GetModeLabel()),
                new MdTableRow("Scope", content.workflow.GetScopeLabel())
            };
            triggerActionsDocument.Root.Add(new MdTable(new MdTableRow("Property", "Value"), triggerRows));

            // Trigger types as bulleted list
            triggerActionsDocument.Root.Add(new MdHeading("Trigger Type", 3));
            var triggerItems = new List<MdListItem>();
            if (content.workflow.OnDemand)
                triggerItems.Add(new MdListItem(new MdParagraph(new MdTextSpan("On-Demand"))));
            if (content.workflow.TriggerOnCreate)
                triggerItems.Add(new MdListItem(new MdParagraph(new MdTextSpan("Record Created"))));
            if (content.workflow.TriggerOnDelete)
                triggerItems.Add(new MdListItem(new MdParagraph(new MdTextSpan("Record Deleted"))));
            if (!string.IsNullOrEmpty(content.workflow.TriggerOnUpdateAttributeList))
            {
                triggerItems.Add(new MdListItem(new MdParagraph(new MdTextSpan("Record Updated"))));
                // Add indented field list
                foreach (string field in content.workflow.TriggerOnUpdateAttributeList.Split(','))
                {
                    string trimmed = field.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        triggerItems.Add(new MdListItem(new MdParagraph(new MdTextSpan("  - " + content.GetFieldDisplayName(trimmed)))));
                }
            }
            if (triggerItems.Count == 0)
                triggerItems.Add(new MdListItem(new MdParagraph(new MdTextSpan("None"))));
            triggerActionsDocument.Root.Add(new MdBulletList(triggerItems.ToArray()));
        }

        private void addActionPages()
        {
            if (content.workflow.Steps.Count == 0) return;

            triggerActionsDocument.Root.Add(new MdHeading("Actions", 2));
            int totalSteps = CountAllSteps(content.workflow.Steps);
            triggerActionsDocument.Root.Add(new MdParagraph(new MdTextSpan($"There are a total of {totalSteps} action(s) in this workflow:")));

            AddActionDetailsRecursive(content.workflow.Steps, 3);
        }

        private void AddActionDetailsRecursive(List<ClassicWorkflowStep> steps, int headingLevel)
        {
            int level = Math.Min(headingLevel, 6);

            // Build filtered list for prev/next navigation (skip branches)
            var navigableSteps = steps.Where(s => s.StepType != ClassicWorkflowStepType.ConditionBranch).ToList();

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];

                // ConditionBranch steps are rendered inline on the parent section
                if (step.StepType == ClassicWorkflowStepType.ConditionBranch)
                {
                    if (step.ChildSteps.Count > 0)
                        AddActionDetailsRecursive(step.ChildSteps, headingLevel);
                    continue;
                }

                int navIndex = navigableSteps.IndexOf(step);
                ClassicWorkflowStep prevStep = (navIndex > 0) ? navigableSteps[navIndex - 1] : null;
                ClassicWorkflowStep nextStep = (navIndex >= 0 && navIndex + 1 < navigableSteps.Count) ? navigableSteps[navIndex + 1] : null;

                triggerActionsDocument.Root.Add(new MdHeading(step.Name ?? step.GetStepTypeLabel(), level));

                // Action detail table
                List<MdTableRow> detailRows = new List<MdTableRow>
                {
                    new MdTableRow("Name", step.Name ?? ""),
                    new MdTableRow("Type", step.GetStepTypeLabel())
                };

                if (!string.IsNullOrEmpty(step.StepDescription))
                    detailRows.Add(new MdTableRow("Description", step.StepDescription));

                if (!string.IsNullOrEmpty(step.TargetEntity))
                    detailRows.Add(new MdTableRow("Target Table", content.GetTableDisplayName(step.TargetEntity)));
                if (!string.IsNullOrEmpty(step.CustomActivityName))
                    detailRows.Add(new MdTableRow("Custom Activity", step.CustomActivityName));
                if (!string.IsNullOrEmpty(step.CustomActivityClass))
                    detailRows.Add(new MdTableRow("Class", step.CustomActivityClass));
                if (!string.IsNullOrEmpty(step.CustomActivityAssembly))
                    detailRows.Add(new MdTableRow("Assembly", step.CustomActivityAssembly));
                if (!string.IsNullOrEmpty(step.CustomActivityFriendlyName))
                    detailRows.Add(new MdTableRow("Friendly Name", step.CustomActivityFriendlyName));
                if (!string.IsNullOrEmpty(step.CustomActivityDescription))
                    detailRows.Add(new MdTableRow("Description", step.CustomActivityDescription));
                if (!string.IsNullOrEmpty(step.CustomActivityGroupName))
                    detailRows.Add(new MdTableRow("Group", step.CustomActivityGroupName));

                triggerActionsDocument.Root.Add(new MdTable(new MdTableRow("Property", "Value"), detailRows));

                // Condition tree
                if (step.ConditionTree != null)
                {
                    triggerActionsDocument.Root.Add(new MdParagraph(new MdStrongEmphasisSpan("Condition:")));
                    RenderConditionTreeToMarkdown(step.ConditionTree, 0);
                }
                else if (!string.IsNullOrEmpty(step.ConditionDescription))
                {
                    triggerActionsDocument.Root.Add(new MdParagraph(
                        new MdStrongEmphasisSpan("Condition: "),
                        new MdTextSpan(step.ConditionDescription)));
                }

                // Field assignments / Inputs
                if (step.Fields.Count > 0)
                {
                    string inputsLabel = step.StepType switch
                    {
                        ClassicWorkflowStepType.Custom => "**Inputs:**",
                        ClassicWorkflowStepType.SendEmail => "**Email Properties:**",
                        _ => "**Field Assignments:**"
                    };
                    triggerActionsDocument.Root.Add(new MdParagraph(new MdRawMarkdownSpan(inputsLabel)));
                    List<MdTableRow> fieldRows = new List<MdTableRow>();
                    foreach (var field in step.Fields)
                    {
                        fieldRows.Add(new MdTableRow(field.FieldName ?? "", field.Value ?? ""));
                    }
                    triggerActionsDocument.Root.Add(new MdTable(new MdTableRow("Field", "Value"), fieldRows));
                }

                // Child steps
                if (step.ChildSteps.Count > 0)
                {
                    if (step.StepType == ClassicWorkflowStepType.CheckCondition ||
                        step.StepType == ClassicWorkflowStepType.Wait)
                    {
                        // Render branches inline
                        foreach (var branch in step.ChildSteps)
                        {
                            if (branch.StepType != ClassicWorkflowStepType.ConditionBranch) continue;
                            string branchLabel = branch.Name ?? "Condition Branch";
                            triggerActionsDocument.Root.Add(new MdParagraph(new MdStrongEmphasisSpan(branchLabel + ":")));

                            if (branch.ConditionTree != null)
                            {
                                RenderConditionTreeToMarkdown(branch.ConditionTree, 0);
                            }
                            else if (!string.IsNullOrEmpty(branch.ConditionDescription) && branchLabel != "Otherwise (Default)")
                            {
                                triggerActionsDocument.Root.Add(new MdParagraph(new MdTextSpan(branch.ConditionDescription)));
                            }

                            if (branch.ChildSteps.Count > 0)
                            {
                                List<MdTableRow> branchRows = new List<MdTableRow>();
                                foreach (var child in branch.ChildSteps)
                                {
                                    branchRows.Add(new MdTableRow(child.GetStepTypeLabel(), child.Name ?? ""));
                                }
                                triggerActionsDocument.Root.Add(new MdTable(new MdTableRow("Type", "Name"), branchRows));
                            }
                        }
                    }
                    else
                    {
                        triggerActionsDocument.Root.Add(new MdParagraph(new MdStrongEmphasisSpan("Subactions:")));
                        List<MdTableRow> childRows = new List<MdTableRow>();
                        foreach (var child in step.ChildSteps)
                        {
                            childRows.Add(new MdTableRow(child.GetStepTypeLabel(), child.Name ?? ""));
                        }
                        triggerActionsDocument.Root.Add(new MdTable(new MdTableRow("Type", "Name"), childRows));
                    }

                    AddActionDetailsRecursive(step.ChildSteps, headingLevel + 1);
                }

                // Previous Action(s)
                if (prevStep != null)
                {
                    triggerActionsDocument.Root.Add(new MdParagraph(new MdStrongEmphasisSpan("Previous Action(s):")));
                    string prevLabel = (prevStep.Name ?? prevStep.GetStepTypeLabel()) + " (" + prevStep.GetStepTypeLabel() + ")";
                    string prevAnchor = CharsetHelper.GetSafeName(prevStep.Name ?? prevStep.GetStepTypeLabel()).ToLowerInvariant().Replace(" ", "-");
                    triggerActionsDocument.Root.Add(new MdBulletList(
                        new MdListItem(new MdParagraph(new MdLinkSpan(prevLabel, "#" + prevAnchor)))));
                }

                // Next Action(s)
                if (nextStep != null)
                {
                    triggerActionsDocument.Root.Add(new MdParagraph(new MdStrongEmphasisSpan("Next Action(s):")));
                    string nextLabel = (nextStep.Name ?? nextStep.GetStepTypeLabel()) + " (" + nextStep.GetStepTypeLabel() + ")";
                    string anchor = CharsetHelper.GetSafeName(nextStep.Name ?? nextStep.GetStepTypeLabel()).ToLowerInvariant().Replace(" ", "-");
                    triggerActionsDocument.Root.Add(new MdBulletList(
                        new MdListItem(new MdParagraph(new MdLinkSpan(nextLabel, "#" + anchor)))));
                }
            }
        }

        // ── Helpers ──

        private void RenderConditionTreeToMarkdown(ConditionExpression expr, int depth)
        {
            if (expr.IsLeaf)
            {
                string text = string.IsNullOrEmpty(expr.Value)
                    ? $"{expr.Field} {expr.Operator}"
                    : $"{expr.Field} {expr.Operator} {expr.Value}";
                triggerActionsDocument.Root.Add(new MdParagraph(new MdTextSpan(new string(' ', depth * 2) + "- " + text)));
            }
            else
            {
                if (depth > 0)
                    triggerActionsDocument.Root.Add(new MdParagraph(new MdStrongEmphasisSpan(new string(' ', depth * 2) + "Group (" + expr.LogicalOperator + "):")));

                for (int i = 0; i < expr.Children.Count; i++)
                {
                    RenderConditionTreeToMarkdown(expr.Children[i], depth + 1);
                    if (i < expr.Children.Count - 1)
                    {
                        triggerActionsDocument.Root.Add(new MdParagraph(new MdEmphasisSpan(new string(' ', (depth + 1) * 2) + expr.LogicalOperator)));
                    }
                }
            }
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
