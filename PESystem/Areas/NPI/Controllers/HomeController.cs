using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PESystem.Areas.NPI.Models;
using PESystem.Areas.NPI.Services;

namespace PESystem.Areas.NPI.Controllers
{
    [Area("NPI")]
    [Authorize(Policy = "NPIAccess")]
    public class HomeController : Controller
    {
        private readonly NpiDocumentService _documentService;

        public HomeController(NpiDocumentService documentService)
        {
            _documentService = documentService;
        }

        public IActionResult Index()
        {
            var projects = _documentService
                .GetProjects()
                .OrderByDescending(p => p.CreatedAt)
                .ToList();

            var totalCategoryCount = _documentService.GetTotalCategoryCount();

            var projectSummaries = projects
                .Select(project =>
                {
                    var uploadedCategories = (project.Documents ?? Enumerable.Empty<NpiDocument>())
                        .Where(d => !string.IsNullOrWhiteSpace(d.CategoryPath))
                        .Select(d => d.CategoryPath)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();

                    return new NpiProjectSummaryViewModel
                    {
                        Project = project,
                        UploadedCategoryCount = uploadedCategories,
                        TotalCategoryCount = totalCategoryCount
                    };
                })
                .ToList();

            ViewBag.Success = TempData["Success"];
            ViewBag.Error = TempData["Error"];

            return View(projectSummaries);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateProject(string projectName, string owner)
        {
            var result = _documentService.CreateProject(projectName, owner);
            TempData[result.Success ? "Success" : "Error"] = result.Message;
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateProject(string projectId, string projectName, string owner)
        {
            var result = _documentService.UpdateProject(projectId, projectName, owner);
            TempData[result.Success ? "Success" : "Error"] = result.Message;
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteProject(string projectId)
        {
            var result = _documentService.DeleteProject(projectId);
            TempData[result.Success ? "Success" : "Error"] = result.Message;
            return RedirectToAction(nameof(Index));
        }

        public IActionResult Manage(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            var project = _documentService.GetProject(id);
            if (project == null)
            {
                return NotFound();
            }

            var structure = _documentService.GetFolderStructure().ToList();
            var documentsByCategory = project.Documents
                .GroupBy(d => d.CategoryPath)
                .ToDictionary(g => g.Key, g => g.OrderBy(d => d.DocumentName).ToList());

            var viewModel = new NpiProjectViewModel
            {
                Project = project,
                Structure = structure,
                DocumentsByCategory = documentsByCategory
            };

            ViewBag.Success = TempData["Success"];
            ViewBag.Error = TempData["Error"];

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UploadDocument(string projectId, string categoryPath, string documentName, string uploadedBy, IFormFile file)
        {
            var result = _documentService.AddDocumentVersion(projectId, categoryPath, documentName, uploadedBy, file);
            TempData[result.Success ? "Success" : "Error"] = result.Message;
            return RedirectToAction(nameof(Manage), new { id = projectId });
        }

        [HttpGet]
        public IActionResult Download(string projectId, string categoryPath, string documentName, int version)
        {
            var result = _documentService.GetDocumentForDownload(projectId, categoryPath, documentName, version);
            if (!result.Success || string.IsNullOrEmpty(result.AbsolutePath))
            {
                TempData["Error"] = string.IsNullOrEmpty(result.Message) ? "Không thể tải file." : result.Message;
                return RedirectToAction(nameof(Manage), new { id = projectId });
            }

            var fileBytes = System.IO.File.ReadAllBytes(result.AbsolutePath);
            var fileName = result.DownloadFileName ?? Path.GetFileName(result.AbsolutePath);
            return File(fileBytes, "application/octet-stream", fileName);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteVersion(string projectId, string categoryPath, string documentName, int version)
        {
            var result = _documentService.DeleteVersion(projectId, categoryPath, documentName, version);
            TempData[result.Success ? "Success" : "Error"] = result.Message;
            return RedirectToAction(nameof(Manage), new { id = projectId });
        }
    }
}
