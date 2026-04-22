using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PowerDocu.Common;

namespace PowerDocu.ClassicWorkflowDocumenter
{
    class ClassicWorkflowWordDocBuilder : WordDocBuilder
    {
        private readonly ClassicWorkflowDocumentationContent content;

        public ClassicWorkflowWordDocBuilder(ClassicWorkflowDocumentationContent contentDocumentation, string template)
        {
            content = contentDocumentation;
            Directory.CreateDirectory(content.folderPath);
            string filename = InitializeWordDocument(content.folderPath + content.filename, template);
            using (WordprocessingDocument wordDocument = WordprocessingDocument.Open(filename, true))
            {
                mainPart = wordDocument.MainDocumentPart;
                body = mainPart.Document.Body;
                PrepareDocument(!String.IsNullOrEmpty(template));

                addOverview();
                addTriggerInfo();
                addWorkflowDiagram(wordDocument);
                addStepsOverview();
                addActionDetails();
                addTableRelationships();
            }
            NotificationHelper.SendNotification("Created Word documentation for Classic Workflow: " + content.workflow.GetDisplayName());
        }

        private void addOverview()
        {
            AddHeading(content.workflow.GetDisplayName(), "Heading1");
            body.AppendChild(new Paragraph(new Run()));

            Table table = CreateTable();
            table.Append(CreateRow(new Text("Name"), new Text(content.workflow.GetDisplayName())));
            table.Append(CreateRow(new Text("Primary Table"), new Text(content.GetTableDisplayName(content.workflow.PrimaryEntity))));
            table.Append(CreateRow(new Text("Category"), new Text(content.workflow.GetCategoryLabel())));
            table.Append(CreateRow(new Text("Mode"), new Text(content.workflow.GetModeLabel())));
            table.Append(CreateRow(new Text("Scope"), new Text(content.workflow.GetScopeLabel())));
            table.Append(CreateRow(new Text("Run As"), new Text(content.workflow.GetRunAsLabel())));
            table.Append(CreateRow(new Text("Trigger"), new Text(content.workflow.GetTriggerDescription())));
            table.Append(CreateRow(new Text("On-Demand"), new Text(content.workflow.OnDemand ? "Yes" : "No")));
            table.Append(CreateRow(new Text("State"), new Text(content.workflow.GetStateLabel())));
            table.Append(CreateRow(new Text("Is Customizable"), new Text(content.workflow.IsCustomizable ? "Yes" : "No")));
            if (!string.IsNullOrEmpty(content.workflow.ID))
                table.Append(CreateRow(new Text("ID"), new Text(content.workflow.ID)));
            if (!string.IsNullOrEmpty(content.workflow.OwnerId))
                table.Append(CreateRow(new Text("Owner"), new Text(content.workflow.OwnerId)));
            if (!string.IsNullOrEmpty(content.workflow.Description))
                table.Append(CreateRow(new Text("Description"), new Text(content.workflow.Description)));
            if (!string.IsNullOrEmpty(content.workflow.IntroducedVersion))
                table.Append(CreateRow(new Text("Version"), new Text(content.workflow.IntroducedVersion)));
            table.Append(CreateRow(new Text("Number of Actions"), new Text(CountAllSteps(content.workflow.Steps).ToString())));
            table.Append(CreateRow(new Text("Number of Conditions"), new Text(CountConditions(content.workflow.Steps).ToString())));
            table.Append(CreateRow(new Text(content.headerDocumentationGenerated),
                new Text(PowerDocuReleaseHelper.GetTimestampWithVersion())));
            body.Append(table);
            body.AppendChild(new Paragraph(new Run(new Break())));
        }

        private void addTriggerInfo()
        {
            AddHeading("Trigger", "Heading2");
            body.AppendChild(new Paragraph(new Run()));

            Table table = CreateTable();
            table.Append(CreateRow(new Text("Primary Table"), new Text(content.GetTableDisplayName(content.workflow.PrimaryEntity))));
            table.Append(CreateRow(new Text("Mode"), new Text(content.workflow.GetModeLabel())));
            table.Append(CreateRow(new Text("Scope"), new Text(content.workflow.GetScopeLabel())));
            body.Append(table);
            body.AppendChild(new Paragraph(new Run(new Break())));

            // Trigger types as list
            AddHeading("Trigger Type", "Heading3");
            if (content.workflow.OnDemand)
                body.AppendChild(new Paragraph(new Run(new Text("• On-Demand"))));
            if (content.workflow.TriggerOnCreate)
                body.AppendChild(new Paragraph(new Run(new Text("• Record Created"))));
            if (content.workflow.TriggerOnDelete)
                body.AppendChild(new Paragraph(new Run(new Text("• Record Deleted"))));
            if (!string.IsNullOrEmpty(content.workflow.TriggerOnUpdateAttributeList))
            {
                body.AppendChild(new Paragraph(new Run(new Text("• Record Updated"))));
                foreach (string field in content.workflow.TriggerOnUpdateAttributeList.Split(','))
                {
                    string trimmed = field.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        body.AppendChild(new Paragraph(new Run(new Text("    ◦ " + content.GetFieldDisplayName(trimmed)))));
                }
            }
            if (!content.workflow.OnDemand && !content.workflow.TriggerOnCreate && !content.workflow.TriggerOnDelete && string.IsNullOrEmpty(content.workflow.TriggerOnUpdateAttributeList))
                body.AppendChild(new Paragraph(new Run(new Text("• None"))));
            body.AppendChild(new Paragraph(new Run(new Break())));
        }

        private void addWorkflowDiagram(WordprocessingDocument wordDoc)
        {
            string pngPath = content.folderPath + "workflow.png";
            string svgPath = content.folderPath + "workflow.svg";

            if (!System.IO.File.Exists(pngPath)) return;

            AddHeading("Workflow Diagram", "Heading2");
            body.AppendChild(new Paragraph(new Run(new Text("The following diagram shows the flow of the workflow including condition branches."))));

            ImagePart imagePart = wordDoc.MainDocumentPart.AddImagePart(ImagePartType.Png);
            int imageWidth, imageHeight;
            using (FileStream stream = new FileStream(pngPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using (var image = Image.FromStream(stream, false, false))
                {
                    imageWidth = image.Width;
                    imageHeight = image.Height;
                }
                stream.Position = 0;
                imagePart.FeedData(stream);
            }

            if (System.IO.File.Exists(svgPath))
            {
                ImagePart svgPart = wordDoc.MainDocumentPart.AddNewPart<ImagePart>("image/svg+xml", "rId" + (new Random()).Next(100000, 999999));
                using (FileStream stream = new FileStream(svgPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    svgPart.FeedData(stream);
                }
                body.AppendChild(new Paragraph(new Run(
                    InsertSvgImage(wordDoc.MainDocumentPart.GetIdOfPart(svgPart), wordDoc.MainDocumentPart.GetIdOfPart(imagePart), imageWidth, imageHeight)
                )));
            }
            else
            {
                body.AppendChild(new Paragraph(new Run(
                    InsertImage(wordDoc.MainDocumentPart.GetIdOfPart(imagePart), imageWidth, imageHeight)
                )));
            }
            body.AppendChild(new Paragraph(new Run(new Break())));
        }

        private void addStepsOverview()
        {
            if (content.workflow.Steps.Count == 0) return;

            AddHeading(content.headerSteps, "Heading2");
            body.AppendChild(new Paragraph(new Run(
                new Text($"This workflow has {CountAllSteps(content.workflow.Steps)} step(s)."))));

            Table table = CreateTable();
            table.Append(CreateHeaderRow(new Text("#"), new Text("Step Name"), new Text("Step Type"), new Text("Target Table"), new Text("Condition")));

            int stepNum = 1;
            AddStepsToTable(table, content.workflow.Steps, ref stepNum, "");

            body.Append(table);
            body.AppendChild(new Paragraph(new Run(new Break())));
        }

        private void AddStepsToTable(Table table, List<ClassicWorkflowStep> steps, ref int stepNum, string prefix)
        {
            foreach (var step in steps)
            {
                string targetDisplay = !string.IsNullOrEmpty(step.TargetEntity)
                    ? content.GetTableDisplayName(step.TargetEntity)
                    : "";

                table.Append(CreateRow(
                    new Text(prefix + stepNum),
                    new Text(step.Name ?? ""),
                    new Text(step.GetStepTypeLabel()),
                    new Text(targetDisplay),
                    new Text(step.ConditionDescription ?? "")
                ));
                stepNum++;

                if (step.ChildSteps.Count > 0)
                {
                    int childNum = 1;
                    string childPrefix = prefix + (stepNum - 1) + ".";
                    AddStepsToTable(table, step.ChildSteps, ref childNum, childPrefix);
                }
            }
        }

        private void addTableRelationships()
        {
            if (content.workflow.TableReferences.Count == 0) return;

            AddHeading(content.headerTableRelationships, "Heading2");
            body.AppendChild(new Paragraph(new Run(
                new Text($"This workflow references {content.workflow.TableReferences.Count} table relationship(s)."))));

            Table table = CreateTable();
            table.Append(CreateHeaderRow(new Text("Table Display Name"), new Text("Table Logical Name"), new Text("Reference Type")));

            foreach (var tableRef in content.workflow.TableReferences)
            {
                table.Append(CreateRow(
                    new Text(content.GetTableDisplayName(tableRef.TableLogicalName)),
                    new Text(tableRef.TableLogicalName),
                    new Text(tableRef.ReferenceType.ToString())
                ));
            }

            body.Append(table);
            body.AppendChild(new Paragraph(new Run(new Break())));
        }

        private void addActionDetails()
        {
            if (content.workflow.Steps.Count == 0) return;

            AddHeading("Actions", "Heading2");
            int totalSteps = CountAllSteps(content.workflow.Steps);
            body.AppendChild(new Paragraph(new Run(new Text($"There are a total of {totalSteps} action(s) in this workflow:"))));
            AddActionDetailsRecursive(content.workflow.Steps, 3);
        }

        private void AddActionDetailsRecursive(List<ClassicWorkflowStep> steps, int headingLevel)
        {
            string headingStyle = headingLevel <= 3 ? "Heading" + headingLevel : "Heading3";

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

                AddHeading(step.Name ?? step.GetStepTypeLabel(), headingStyle);

                // Action detail table (matching Flow documenter pattern)
                Table actionTable = CreateTable();
                actionTable.Append(CreateRow(new Text("Name"), new Text(step.Name ?? "")));
                actionTable.Append(CreateRow(new Text("Type"), new Text(step.GetStepTypeLabel())));

                if (!string.IsNullOrEmpty(step.StepDescription))
                    actionTable.Append(CreateRow(new Text("Description"), new Text(step.StepDescription)));

                if (!string.IsNullOrEmpty(step.TargetEntity))
                    actionTable.Append(CreateRow(new Text("Target Table"), new Text(content.GetTableDisplayName(step.TargetEntity))));

                if (!string.IsNullOrEmpty(step.CustomActivityName))
                    actionTable.Append(CreateRow(new Text("Custom Activity"), new Text(step.CustomActivityName)));

                if (!string.IsNullOrEmpty(step.CustomActivityClass))
                    actionTable.Append(CreateRow(new Text("Class"), new Text(step.CustomActivityClass)));

                if (!string.IsNullOrEmpty(step.CustomActivityAssembly))
                    actionTable.Append(CreateRow(new Text("Assembly"), new Text(step.CustomActivityAssembly)));

                if (!string.IsNullOrEmpty(step.CustomActivityFriendlyName))
                    actionTable.Append(CreateRow(new Text("Friendly Name"), new Text(step.CustomActivityFriendlyName)));

                if (!string.IsNullOrEmpty(step.CustomActivityDescription))
                    actionTable.Append(CreateRow(new Text("Description"), new Text(step.CustomActivityDescription)));

                if (!string.IsNullOrEmpty(step.CustomActivityGroupName))
                    actionTable.Append(CreateRow(new Text("Group"), new Text(step.CustomActivityGroupName)));

                // Condition tree display
                if (step.ConditionTree != null)
                {
                    actionTable.Append(CreateMergedRow(new Text("Condition"), 2, cellHeaderBackground));
                    RenderConditionTreeToWordTable(actionTable, step.ConditionTree, 0);
                }
                else if (!string.IsNullOrEmpty(step.ConditionDescription))
                {
                    actionTable.Append(CreateMergedRow(new Text("Condition"), 2, cellHeaderBackground));
                    actionTable.Append(CreateRow(new Text("Expression"), new Text(step.ConditionDescription)));
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
                    actionTable.Append(CreateMergedRow(new Text(inputsLabel), 2, cellHeaderBackground));

                    foreach (var field in step.Fields)
                    {
                        actionTable.Append(CreateRow(
                            new Text(field.FieldName ?? ""),
                            new Text(field.Value ?? "")
                        ));
                    }
                }

                // Child steps / Subactions
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
                            actionTable.Append(CreateMergedRow(new Text(branchLabel), 2, cellHeaderBackground));

                            if (branch.ConditionTree != null)
                            {
                                RenderConditionTreeToWordTable(actionTable, branch.ConditionTree, 0);
                            }
                            else if (!string.IsNullOrEmpty(branch.ConditionDescription) && branchLabel != "Otherwise (Default)")
                            {
                                actionTable.Append(CreateRow(new Text("Expression"), new Text(branch.ConditionDescription)));
                            }

                            foreach (var child in branch.ChildSteps)
                            {
                                actionTable.Append(CreateRow(new Text(child.GetStepTypeLabel()), new Text(child.Name ?? "")));
                            }
                        }
                    }
                    else
                    {
                        actionTable.Append(CreateMergedRow(new Text("Subactions"), 2, cellHeaderBackground));

                        foreach (var child in step.ChildSteps)
                        {
                            actionTable.Append(CreateRow(new Text(child.GetStepTypeLabel()), new Text(child.Name ?? "")));
                        }
                    }
                }

                // Previous Action(s)
                if (prevStep != null)
                {
                    actionTable.Append(CreateMergedRow(new Text("Previous Action(s)"), 2, cellHeaderBackground));
                    actionTable.Append(CreateRow(
                        new Text(prevStep.GetStepTypeLabel()),
                        new Text(prevStep.Name ?? "")));
                }

                // Next Action(s)
                if (nextStep != null)
                {
                    actionTable.Append(CreateMergedRow(new Text("Next Action(s)"), 2, cellHeaderBackground));
                    actionTable.Append(CreateRow(
                        new Text(nextStep.GetStepTypeLabel()),
                        new Text(nextStep.Name ?? "")));
                }

                body.Append(actionTable);
                body.AppendChild(new Paragraph(new Run(new Break())));

                // Recurse into child steps for their own detail sections
                if (step.ChildSteps.Count > 0)
                {
                    AddActionDetailsRecursive(step.ChildSteps, headingLevel + 1);
                }
            }
        }

        private void RenderConditionTreeToWordTable(Table table, ConditionExpression expr, int depth)
        {
            string indent = new string(' ', depth * 2);

            if (expr.IsLeaf)
            {
                string entityPart = "";
                string fieldPart = expr.Field ?? "";
                if (fieldPart.Contains(" on "))
                {
                    int onIdx = fieldPart.IndexOf(" on ");
                    entityPart = fieldPart.Substring(onIdx + 4);
                    fieldPart = fieldPart.Substring(0, onIdx);
                }

                string condLine = entityPart + " → " + fieldPart + " " + (expr.Operator ?? "") +
                    (string.IsNullOrEmpty(expr.Value) ? "" : " " + expr.Value);
                table.Append(CreateRow(new Text(indent + "  "), new Text(condLine)));
            }
            else
            {
                // Group: show operator label on top, then render children
                foreach (var child in expr.Children)
                {
                    if (child.IsGroup)
                    {
                        // Nested group: show its operator as a label, then its children indented
                        table.Append(CreateRow(new Text(indent + "▼ " + child.LogicalOperator), new Text("")));
                        RenderConditionTreeToWordTable(table, child, depth + 1);
                    }
                    else
                    {
                        RenderConditionTreeToWordTable(table, child, depth);
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
