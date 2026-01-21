using System;
using System.Collections.Generic;

namespace PESystem.Areas.NPI.Models
{
    public class NpiProject
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<NpiDocument> Documents { get; set; } = new();
    }

    public class NpiDocument
    {
        public string CategoryPath { get; set; } = string.Empty;
        public string DocumentName { get; set; } = string.Empty;
        public List<NpiDocumentVersion> Versions { get; set; } = new();
    }

    public class NpiDocumentVersion
    {
        public int Version { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
        public string StoredFileName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; } = DateTime.Now;
        public string UploadedBy { get; set; } = string.Empty;
    }

    public class NpiFolderDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<NpiFolderDefinition> Children { get; set; } = new();
    }

    public class NpiProjectViewModel
    {
        public NpiProject Project { get; set; } = new();
        public List<NpiFolderDefinition> Structure { get; set; } = new();
        public Dictionary<string, List<NpiDocument>> DocumentsByCategory { get; set; } = new();
    }

    public class NpiCategoryCardViewModel
    {
        public string ProjectId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string CategoryPath { get; set; } = string.Empty;
        public List<NpiDocument> Documents { get; set; } = new();
        public bool IsChildCategory { get; set; }
    }

    public class NpiProjectSummaryViewModel
    {
        public NpiProject Project { get; set; } = new();
        public int UploadedCategoryCount { get; set; }
        public int TotalCategoryCount { get; set; }
    }
}
