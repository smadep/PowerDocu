using System.IO;
using System.Linq;
using PowerDocu.Common;

namespace PowerDocu.ClassicWorkflowDocumenter
{
    public class ClassicWorkflowDocumentationContent
    {
        public string folderPath, filename;
        public ClassicWorkflowEntity workflow;
        public DocumentationContext context;

        public string headerOverview = "Overview";
        public string headerSteps = "Steps";
        public string headerStepDetails = "Step Details";
        public string headerTableRelationships = "Table Relationships";
        public string headerProperties = "Properties";
        public string headerDocumentationGenerated = "Documentation generated at";

        public ClassicWorkflowDocumentationContent(ClassicWorkflowEntity workflow, string path, DocumentationContext context)
        {
            NotificationHelper.SendNotification("Preparing documentation content for Classic Workflow: " + workflow.GetDisplayName());
            this.workflow = workflow;
            this.context = context;
            folderPath = path + CharsetHelper.GetSafeName(@"\WorkflowDoc " + workflow.GetDisplayName() + @"\");
            Directory.CreateDirectory(folderPath);
            filename = CharsetHelper.GetSafeName(workflow.GetDisplayName());
        }

        public string GetTableDisplayName(string schemaName)
        {
            if (string.IsNullOrEmpty(schemaName)) return schemaName;
            string displayName = context?.GetTableDisplayName(schemaName) ?? schemaName;
            // If display name differs from logical name, show both: "Display Name (logical_name)"
            if (!string.IsNullOrEmpty(displayName) && !displayName.Equals(schemaName, System.StringComparison.OrdinalIgnoreCase))
                return displayName + " (" + schemaName + ")";
            return schemaName;
        }

        /// <summary>
        /// Resolves a field logical name to "Display Name (logical_name)" by looking up
        /// the column in the primary entity's table definition.
        /// </summary>
        public string GetFieldDisplayName(string fieldLogicalName, string entityLogicalName = null)
        {
            if (string.IsNullOrEmpty(fieldLogicalName)) return fieldLogicalName;

            string tableName = entityLogicalName ?? workflow.PrimaryEntity;
            if (string.IsNullOrEmpty(tableName) || context?.Tables == null) return fieldLogicalName;

            var table = context.Tables.FirstOrDefault(t =>
                t.getName().Equals(tableName, System.StringComparison.OrdinalIgnoreCase));
            if (table == null) return fieldLogicalName;

            var column = table.GetColumns().FirstOrDefault(c =>
                c.getLogicalName().Equals(fieldLogicalName, System.StringComparison.OrdinalIgnoreCase));
            if (column == null) return fieldLogicalName;

            string displayName = column.getDisplayName();
            if (!string.IsNullOrEmpty(displayName) && !displayName.Equals(fieldLogicalName, System.StringComparison.OrdinalIgnoreCase))
                return displayName + " (" + fieldLogicalName + ")";
            return fieldLogicalName;
        }
    }
}
