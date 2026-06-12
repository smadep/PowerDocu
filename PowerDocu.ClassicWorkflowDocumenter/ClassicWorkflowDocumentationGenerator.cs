using System;
using System.IO;
using PowerDocu.Common;

namespace PowerDocu.ClassicWorkflowDocumenter
{
    public static class ClassicWorkflowDocumentationGenerator
    {
        public static void GenerateOutput(DocumentationContext context, string path)
        {
            if (context.ClassicWorkflows == null || context.ClassicWorkflows.Count == 0 || !context.Config.documentClassicWorkflows) return;

            DateTime startDocGeneration = DateTime.Now;
            NotificationHelper.SendNotification($"Found {context.ClassicWorkflows.Count} Classic Workflow(s) in the solution.");

            if (context.FullDocumentation)
            {
                foreach (ClassicWorkflowEntity workflow in context.ClassicWorkflows)
                {
                    ClassicWorkflowDocumentationContent content = new ClassicWorkflowDocumentationContent(workflow, path, context);

                    // Generate workflow flow diagram
                    if (workflow.Steps.Count > 0)
                    {
                        try
                        {
                            GraphBuilder graphBuilder = new GraphBuilder(workflow, content.folderPath);
                            graphBuilder.BuildGraph();
                        }
                        catch (Exception ex)
                        {
                            NotificationHelper.SendNotification("  - Warning: Could not generate workflow diagram: " + ex.Message);
                        }
                    }

                    string wordTemplate = (!String.IsNullOrEmpty(context.Config.wordTemplate) && File.Exists(context.Config.wordTemplate))
                        ? context.Config.wordTemplate : null;
                    if (context.Config.outputFormat.Equals(OutputFormatHelper.Word) || context.Config.outputFormat.Equals(OutputFormatHelper.All))
                    {
                        NotificationHelper.SendNotification("Creating Word documentation for Classic Workflow: " + workflow.GetDisplayName());
                        ClassicWorkflowWordDocBuilder wordDoc = new ClassicWorkflowWordDocBuilder(content, wordTemplate);
                    }
                    if (context.Config.outputFormat.Equals(OutputFormatHelper.Markdown) || context.Config.outputFormat.Equals(OutputFormatHelper.All))
                    {
                        NotificationHelper.SendNotification("Creating Markdown documentation for Classic Workflow: " + workflow.GetDisplayName());
                        ClassicWorkflowMarkdownBuilder markdownDoc = new ClassicWorkflowMarkdownBuilder(content);
                    }
                    if (context.Config.outputFormat.Equals(OutputFormatHelper.Html) || context.Config.outputFormat.Equals(OutputFormatHelper.All))
                    {
                        NotificationHelper.SendNotification("Creating HTML documentation for Classic Workflow: " + workflow.GetDisplayName());
                        ClassicWorkflowHtmlBuilder htmlDoc = new ClassicWorkflowHtmlBuilder(content);
                    }
                    context.Progress?.Increment("Classic Workflows");
                }
            }
            else
            {
                context.Progress?.Complete("ClassicWorkflows");
            }

            DateTime endDocGeneration = DateTime.Now;
            NotificationHelper.SendNotification(
                $"ClassicWorkflowDocumenter: Processed {context.ClassicWorkflows.Count} Classic Workflow(s) in {(endDocGeneration - startDocGeneration).TotalSeconds} seconds."
            );
        }
    }
}
