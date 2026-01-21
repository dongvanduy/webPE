using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PESystem.Areas.NPI.Models;

namespace PESystem.Areas.NPI.Services
{
    public class NpiDocumentService
    {
        private const string BasePath = @"D:\NpiDocument";
        private const string MetadataFileName = "metadata.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private static readonly List<NpiFolderDefinition> FolderStructure = new()
        {
            new NpiFolderDefinition
            {
                Name = "BOM",
                DisplayName = "BOM",
                Children = new List<NpiFolderDefinition>
                {
                    new NpiFolderDefinition { Name = "NPI BOM", DisplayName = "NPI BOM" },
                    new NpiFolderDefinition { Name = "MP BOM", DisplayName = "MP BOM" }
                }
            },
            new NpiFolderDefinition
            {
                Name = "Document instruction",
                DisplayName = "Document instruction",
                Children = new List<NpiFolderDefinition>
                {
                    new NpiFolderDefinition { Name = "PCB layout", DisplayName = "PCB layout" },
                    new NpiFolderDefinition { Name = "schematic", DisplayName = "Schematic" },
                    new NpiFolderDefinition { Name = "ASSY + Packing instruction", DisplayName = "ASSY + Packing instruction" },
                    new NpiFolderDefinition { Name = "Label instruction", DisplayName = "Label instruction" }
                }
            },
            new NpiFolderDefinition
            {
                Name = "Manufacturing process",
                DisplayName = "Manufacturing process",
                Children = new List<NpiFolderDefinition>
                {
                    new NpiFolderDefinition { Name = "Route from NV", DisplayName = "Route from NV" },
                    new NpiFolderDefinition { Name = "Manufacturing in foxconn + SFC", DisplayName = "Manufacturing in foxconn + SFC" },
                    new NpiFolderDefinition { Name = "SOP full process from IE", DisplayName = "SOP full process from IE" }
                }
            },
            new NpiFolderDefinition
            {
                Name = "Key component",
                DisplayName = "Key component",
                Children = new List<NpiFolderDefinition>
                {
                    new NpiFolderDefinition { Name = "OPV data", DisplayName = "OPV data" }
                }
            },
            new NpiFolderDefinition
            {
                Name = "SOP NPI",
                DisplayName = "SOP NPI",
                Children = new List<NpiFolderDefinition>
                {
                    new NpiFolderDefinition { Name = "SOP polarity", DisplayName = "SOP polarity" },
                    new NpiFolderDefinition { Name = "EPAD location", DisplayName = "EPAD location" },
                    new NpiFolderDefinition { Name = "ESD location", DisplayName = "ESD location" }
                }
            },
            new NpiFolderDefinition
            {
                Name = "Config NPI",
                DisplayName = "Config NPI",
                Children = new List<NpiFolderDefinition>
                {
                    new NpiFolderDefinition { Name = "Config 27 for BI process", DisplayName = "Config 27 for BI process" },
                    new NpiFolderDefinition { Name = "LCR config", DisplayName = "LCR config" }
                }
            },
            new NpiFolderDefinition
            {
                Name = "DFX",
                DisplayName = "DFX",
                Children = new List<NpiFolderDefinition>
                {
                    new NpiFolderDefinition { Name = "PFMEA", DisplayName = "PFMEA" },
                    new NpiFolderDefinition { Name = "PMP", DisplayName = "PMP" }
                }
            },
            new NpiFolderDefinition
            {
                Name = "Production plan",
                DisplayName = "Production plan",
                Children = new List<NpiFolderDefinition>
                {
                    new NpiFolderDefinition { Name = "DEV + status", DisplayName = "DEV + status" }
                }
            },
            new NpiFolderDefinition
            {
                Name = "Yield Rate NPI for each build",
                DisplayName = "Yield Rate NPI for each build",
                Children = new List<NpiFolderDefinition>
                {
                    new NpiFolderDefinition { Name = "YR everyday (tracker)", DisplayName = "YR everyday (tracker)" },
                    new NpiFolderDefinition { Name = "Only test station (engineer report)", DisplayName = "Only test station (engineer report)" },
                    new NpiFolderDefinition { Name = "FA detail report", DisplayName = "FA detail report" }
                }
            },
            new NpiFolderDefinition
            {
                Name = "Cook book",
                DisplayName = "Cook book",
                Children = new List<NpiFolderDefinition>
                {
                    new NpiFolderDefinition { Name = "Cook book document", DisplayName = "Cook book document" },
                    new NpiFolderDefinition { Name = "Picture SFG + FG", DisplayName = "Picture SFG + FG" },
                    new NpiFolderDefinition { Name = "Picture AOI", DisplayName = "Picture AOI" },
                    new NpiFolderDefinition { Name = "Profile SMT", DisplayName = "Profile SMT" }
                }
            },
            new NpiFolderDefinition
            {
                Name = "Yield rate 1st MP",
                DisplayName = "Yield rate 1st MP",
                Children = new List<NpiFolderDefinition>
                {
                    new NpiFolderDefinition { Name = "Only test station (engineer report)", DisplayName = "Only test station (engineer report)" },
                    new NpiFolderDefinition { Name = "FA detail report", DisplayName = "FA detail report" }
                }
            },
            new NpiFolderDefinition
            {
                Name = "Bone pile report",
                DisplayName = "Bone pile report",
                Children = new List<NpiFolderDefinition>
                {
                    new NpiFolderDefinition { Name = "Bone pile report", DisplayName = "Bone pile report" }
                }
            }
        };

        public NpiDocumentService()
        {
            EnsureBasePath();
        }

        public IReadOnlyList<NpiFolderDefinition> GetFolderStructure()
        {
            return FolderStructure.Select(CloneDefinition).ToList();
        }

        public int GetTotalCategoryCount()
        {
            return CountLeafCategories(FolderStructure);
        }

        public IEnumerable<NpiProject> GetProjects()
        {
            EnsureBasePath();
            if (!Directory.Exists(BasePath))
            {
                yield break;
            }

            foreach (var directory in Directory.GetDirectories(BasePath))
            {
                var metadata = LoadMetadata(directory);
                if (metadata != null)
                {
                    yield return metadata;
                }
            }
        }

        public NpiProject? GetProject(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                return null;
            }

            var projectFolder = Path.Combine(BasePath, projectId);
            if (!Directory.Exists(projectFolder))
            {
                return null;
            }

            return LoadMetadata(projectFolder);
        }

        public (bool Success, string Message, NpiProject? Project) CreateProject(string projectName, string owner)
        {
            if (string.IsNullOrWhiteSpace(projectName))
            {
                return (false, "Tên project không được bỏ trống.", null);
            }

            var sanitizedName = SanitizeForPath(projectName);
            if (string.IsNullOrWhiteSpace(sanitizedName))
            {
                sanitizedName = "Project";
            }

            var projectId = EnsureUniqueProjectId(sanitizedName);
            var projectFolder = Path.Combine(BasePath, projectId);

            try
            {
                Directory.CreateDirectory(projectFolder);
                CreateFolderStructure(projectFolder);

                var project = new NpiProject
                {
                    Id = projectId,
                    Name = projectName.Trim(),
                    Owner = owner?.Trim() ?? string.Empty,
                    CreatedAt = DateTime.Now
                };

                SaveMetadata(projectFolder, project);
                return (true, "Project được tạo thành công.", project);
            }
            catch (Exception ex)
            {
                return (false, $"Không thể tạo project: {ex.Message}", null);
            }
        }

        public (bool Success, string Message) UpdateProject(string projectId, string name, string owner)
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                return (false, "Thiếu thông tin project.");
            }

            var projectFolder = Path.Combine(BasePath, projectId);
            var metadata = LoadMetadata(projectFolder);
            if (metadata == null)
            {
                return (false, "Không tìm thấy project.");
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                metadata.Name = name.Trim();
            }

            metadata.Owner = owner?.Trim() ?? string.Empty;
            SaveMetadata(projectFolder, metadata);

            return (true, "Đã cập nhật thông tin project.");
        }

        public (bool Success, string Message) DeleteProject(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                return (false, "Thiếu thông tin project.");
            }

            var projectFolder = Path.Combine(BasePath, projectId);
            if (!Directory.Exists(projectFolder))
            {
                return (false, "Không tìm thấy project.");
            }

            try
            {
                Directory.Delete(projectFolder, recursive: true);
                return (true, "Đã xoá project cùng toàn bộ tài liệu.");
            }
            catch (Exception ex)
            {
                return (false, $"Không thể xoá project: {ex.Message}");
            }
        }

        public (bool Success, string Message) AddDocumentVersion(
            string projectId,
            string categoryPath,
            string documentName,
            string uploadedBy,
            IFormFile file)
        {
            if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(categoryPath))
            {
                return (false, "Thiếu thông tin project hoặc thư mục.");
            }

            if (file == null || file.Length == 0)
            {
                return (false, "Tệp tải lên không hợp lệ.");
            }

            var projectFolder = Path.Combine(BasePath, projectId);
            var metadata = LoadMetadata(projectFolder);
            if (metadata == null)
            {
                return (false, "Không tìm thấy project.");
            }

            var categorySegments = SplitCategoryPath(categoryPath);
            var categoryFolder = Path.Combine(new[] { projectFolder }.Concat(categorySegments).ToArray());

            Directory.CreateDirectory(categoryFolder);

            var documentFolderName = SanitizeForPath(documentName);
            if (string.IsNullOrWhiteSpace(documentFolderName))
            {
                documentFolderName = "Document";
            }
            var documentFolder = Path.Combine(categoryFolder, documentFolderName);
            Directory.CreateDirectory(documentFolder);

            var document = metadata.Documents
                .FirstOrDefault(d => string.Equals(d.CategoryPath, categoryPath, StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(d.DocumentName, documentName.Trim(), StringComparison.OrdinalIgnoreCase));

            if (document == null)
            {
                document = new NpiDocument
                {
                    CategoryPath = categoryPath,
                    DocumentName = documentName.Trim()
                };
                metadata.Documents.Add(document);
            }

            var newVersion = document.Versions.Any() ? document.Versions.Max(v => v.Version) + 1 : 1;
            var sanitizedFileName = SanitizeForPath(Path.GetFileNameWithoutExtension(file.FileName));
            if (string.IsNullOrWhiteSpace(sanitizedFileName))
            {
                sanitizedFileName = "file";
            }
            var extension = Path.GetExtension(file.FileName);
            var storedFileName = $"{sanitizedFileName}_v{newVersion}_{DateTime.Now:yyyyMMddHHmmss}{extension}";

            var pathSegments = categorySegments.ToList();
            pathSegments.Add(documentFolderName);
            pathSegments.Add(storedFileName);

            var physicalPath = Path.Combine(new[] { projectFolder }.Concat(pathSegments).ToArray());
            using (var stream = new FileStream(physicalPath, FileMode.Create))
            {
                file.CopyTo(stream);
            }

            var relativePath = string.Join('/', pathSegments);
            document.Versions.Add(new NpiDocumentVersion
            {
                Version = newVersion,
                OriginalFileName = file.FileName,
                StoredFileName = storedFileName,
                RelativePath = relativePath,
                UploadedAt = DateTime.Now,
                UploadedBy = uploadedBy?.Trim() ?? string.Empty
            });

            SaveMetadata(projectFolder, metadata);
            return (true, "Tải tài liệu thành công.");
        }

        public (bool Success, string Message, string? AbsolutePath, string? DownloadFileName) GetDocumentForDownload(
            string projectId,
            string categoryPath,
            string documentName,
            int version)
        {
            var projectFolder = Path.Combine(BasePath, projectId);
            var metadata = LoadMetadata(projectFolder);
            if (metadata == null)
            {
                return (false, "Không tìm thấy project.", null, null);
            }

            var document = metadata.Documents
                .FirstOrDefault(d => string.Equals(d.CategoryPath, categoryPath, StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(d.DocumentName, documentName, StringComparison.OrdinalIgnoreCase));

            if (document == null)
            {
                return (false, "Không tìm thấy tài liệu.", null, null);
            }

            var versionInfo = document.Versions.FirstOrDefault(v => v.Version == version);
            if (versionInfo == null)
            {
                return (false, "Không tìm thấy phiên bản.", null, null);
            }

            var physicalPath = Path.Combine(projectFolder, versionInfo.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(physicalPath))
            {
                return (false, "File không tồn tại.", null, null);
            }

            return (true, string.Empty, physicalPath, versionInfo.OriginalFileName);
        }

        public (bool Success, string Message) DeleteVersion(string projectId, string categoryPath, string documentName, int version)
        {
            var projectFolder = Path.Combine(BasePath, projectId);
            var metadata = LoadMetadata(projectFolder);
            if (metadata == null)
            {
                return (false, "Không tìm thấy project.");
            }

            var document = metadata.Documents
                .FirstOrDefault(d => string.Equals(d.CategoryPath, categoryPath, StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(d.DocumentName, documentName, StringComparison.OrdinalIgnoreCase));

            if (document == null)
            {
                return (false, "Không tìm thấy tài liệu.");
            }

            var versionInfo = document.Versions.FirstOrDefault(v => v.Version == version);
            if (versionInfo == null)
            {
                return (false, "Không tìm thấy phiên bản.");
            }

            var physicalPath = Path.Combine(projectFolder, versionInfo.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(physicalPath))
            {
                File.Delete(physicalPath);
            }

            document.Versions.Remove(versionInfo);
            if (!document.Versions.Any())
            {
                metadata.Documents.Remove(document);
            }

            SaveMetadata(projectFolder, metadata);
            return (true, "Đã xoá phiên bản tài liệu.");
        }

        private void EnsureBasePath()
        {
            if (!Directory.Exists(BasePath))
            {
                Directory.CreateDirectory(BasePath);
            }
        }

        private static string SanitizeForPath(string value)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var cleaned = new string(value
                .Where(c => !invalidChars.Contains(c))
                .ToArray());

            cleaned = cleaned.Trim();
            cleaned = cleaned.Replace(' ', '_');
            return cleaned;
        }

        private string EnsureUniqueProjectId(string sanitizedName)
        {
            var projectId = sanitizedName;
            var counter = 1;
            while (Directory.Exists(Path.Combine(BasePath, projectId)))
            {
                counter++;
                projectId = $"{sanitizedName}_{counter}";
            }

            return projectId;
        }

        private void CreateFolderStructure(string projectFolder)
        {
            foreach (var definition in FolderStructure)
            {
                CreateFolderRecursive(projectFolder, definition, new List<string>());
            }
        }

        private void CreateFolderRecursive(string projectFolder, NpiFolderDefinition definition, List<string> parents)
        {
            var segments = new List<string>(parents) { definition.Name };
            var folderPath = Path.Combine(new[] { projectFolder }.Concat(segments).ToArray());
            Directory.CreateDirectory(folderPath);

            foreach (var child in definition.Children)
            {
                CreateFolderRecursive(projectFolder, child, segments);
            }
        }

        private static NpiFolderDefinition CloneDefinition(NpiFolderDefinition definition)
        {
            return new NpiFolderDefinition
            {
                Name = definition.Name,
                DisplayName = definition.DisplayName,
                Children = definition.Children.Select(CloneDefinition).ToList()
            };
        }

        private static int CountLeafCategories(IEnumerable<NpiFolderDefinition> definitions)
        {
            var total = 0;

            foreach (var definition in definitions)
            {
                if (definition.Children != null && definition.Children.Count > 0)
                {
                    total += CountLeafCategories(definition.Children);
                }
                else
                {
                    total += 1;
                }
            }

            return total;
        }

        private static NpiProject? LoadMetadata(string projectFolder)
        {
            var metadataPath = Path.Combine(projectFolder, MetadataFileName);
            if (!File.Exists(metadataPath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(metadataPath);
                var project = JsonSerializer.Deserialize<NpiProject>(json, JsonOptions);
                if (project != null)
                {
                    project.Documents ??= new List<NpiDocument>();
                    foreach (var doc in project.Documents)
                    {
                        doc.Versions ??= new List<NpiDocumentVersion>();
                    }

                    NormalizeDocumentVersions(project);
                }
                return project;
            }
            catch
            {
                return null;
            }
        }

        private static void SaveMetadata(string projectFolder, NpiProject project)
        {
            var metadataPath = Path.Combine(projectFolder, MetadataFileName);
            var json = JsonSerializer.Serialize(project, JsonOptions);
            File.WriteAllText(metadataPath, json);
        }

        private static string[] SplitCategoryPath(string categoryPath)
        {
            return categoryPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        }

        private static void NormalizeDocumentVersions(NpiProject project)
        {
            foreach (var document in project.Documents)
            {
                if (document.Versions == null || document.Versions.Count == 0)
                {
                    continue;
                }

                var orderedByTime = document.Versions
                    .OrderBy(v => v.UploadedAt)
                    .ThenBy(v => v.Version)
                    .ToList();

                var requiresRenumbering = false;
                for (int i = 0; i < orderedByTime.Count; i++)
                {
                    if (orderedByTime[i].Version != i + 1)
                    {
                        requiresRenumbering = true;
                        break;
                    }
                }

                if (requiresRenumbering)
                {
                    for (int i = 0; i < orderedByTime.Count; i++)
                    {
                        orderedByTime[i].Version = i + 1;
                    }
                }

                document.Versions = orderedByTime;
            }
        }
    }
}
