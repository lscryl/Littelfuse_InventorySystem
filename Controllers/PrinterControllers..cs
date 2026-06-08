// ============================================================
//  Controllers/PrintersController.cs
//
//  PLACE THIS FILE AT:
//    ITInventorySystem/Controllers/PrintersController.cs
//
//  ROUTING (auto via convention):
//    GET  /Printers              → Index
//    POST /Printers/Add          → Add
//    POST /Printers/Edit         → Edit
//    POST /Printers/Replace      → Replace
//    POST /Printers/Delete       → Delete
//    POST /Printers/Import       → Import
//    GET  /Printers/History/{id} → History  (returns JSON for AJAX)
//    GET  /Printers/ExportCsv    → ExportCsv (downloads CSV file)
// ============================================================

using ITInventorySystem.Data;
using ITInventorySystem.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace ITInventorySystem.Controllers
{
    public class PrintersController : Controller
    {
        private readonly PrinterRepository _repo;

        public PrintersController(PrinterRepository repo)
        {
            _repo = repo;
        }

        // ════════════════════════════════════════════════════
        //  GET /Printers
        // ════════════════════════════════════════════════════
        public IActionResult Index(string search = "", string status = "")
        {
            var vm = new PrintersIndexViewModel
            {
                Printers     = _repo.GetAll(search, status),
                Counts       = _repo.GetCounts(),
                FilterStatus = status,
                Search       = search,
                ToastMessage = TempData["PrinterMsg"]?.ToString() ?? "",
                ToastType    = TempData["PrinterMsgType"]?.ToString() ?? "success",
                SaveError    = TempData["SaveError"]?.ToString() ?? "",
            };
            return View(vm);
        }

        // ════════════════════════════════════════════════════
        //  POST /Printers/Add
        // ════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Add(AddPrinterForm form)
        {
            var (ok, newId, error) = _repo.Add(form);
            if (ok)
            {
                TempData["PrinterMsg"]     = $"Printer \"{form.Name}\" added successfully.";
                TempData["PrinterMsgType"] = "success";
            }
            else
            {
                TempData["SaveError"] = error;
            }
            return RedirectToAction(nameof(Index));
        }

        // ════════════════════════════════════════════════════
        //  POST /Printers/Edit
        // ════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(EditPrinterForm form)
        {
            var (ok, error) = _repo.Edit(form);
            if (ok)
            {
                TempData["PrinterMsg"]     = $"Printer \"{form.Name}\" updated successfully.";
                TempData["PrinterMsgType"] = "success";
            }
            else
            {
                TempData["SaveError"] = error;
            }
            return RedirectToAction(nameof(Index));
        }

        // ════════════════════════════════════════════════════
        //  POST /Printers/Replace
        // ════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Replace(ReplacePrinterForm form)
        {
            var old = _repo.GetById(form.Id);
            var (ok, error) = _repo.Replace(form);
            if (ok)
            {
                TempData["PrinterMsg"]     = $"Printer \"{old?.Name}\" replaced by \"{form.NewName}\" successfully.";
                TempData["PrinterMsgType"] = "success";
            }
            else
            {
                TempData["SaveError"] = error;
            }
            return RedirectToAction(nameof(Index));
        }

        // ════════════════════════════════════════════════════
        //  POST /Printers/Delete
        // ════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            _repo.Delete(id);
            TempData["PrinterMsg"]     = "Printer deleted.";
            TempData["PrinterMsgType"] = "success";
            return RedirectToAction(nameof(Index));
        }

        // ════════════════════════════════════════════════════
        //  POST /Printers/Import
        // ════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Import(string rowsJson = "[]")
        {
            try
            {
                var rows = JsonSerializer.Deserialize<List<ImportPrinterRow>>(rowsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new List<ImportPrinterRow>();

                int count = _repo.Import(rows);
                TempData["PrinterMsg"]     = $"{count} printer(s) imported successfully.";
                TempData["PrinterMsgType"] = "success";
            }
            catch (Exception ex)
            {
                TempData["SaveError"] = "Import failed: " + ex.Message;
            }
            return RedirectToAction(nameof(Index));
        }

        // ════════════════════════════════════════════════════
        //  GET /Printers/History/{id}   → returns JSON (AJAX)
        // ════════════════════════════════════════════════════
        [HttpGet]
        public IActionResult History(int id)
        {
            var hist = _repo.GetHistory(id);
            return Json(hist);
        }

        // ════════════════════════════════════════════════════
        //  GET /Printers/ExportCsv   → downloads CSV file
        //  Replaces the client-side exportCSV() JS function
        //  with a server-side download for reliability.
        // ════════════════════════════════════════════════════
        [HttpGet]
        public IActionResult ExportCsv(string search = "", string status = "")
        {
            var printers = _repo.GetAll(search, status);
            var sb = new StringBuilder();

            // Header row
            sb.AppendLine("id,name,vendor,model,serial,building,department,ip_address,mac_address,status,pullout_date,replaced_date,replaced_location,remarks,created_at");

            foreach (var p in printers)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    CsvCell(p.Id.ToString()),
                    CsvCell(p.Name),
                    CsvCell(p.Vendor),
                    CsvCell(p.Model),
                    CsvCell(p.Serial),
                    CsvCell(p.Building),
                    CsvCell(p.Department),
                    CsvCell(p.IpAddress),
                    CsvCell(p.MacAddress),
                    CsvCell(p.Status),
                    CsvCell(p.PulloutDate  ?? ""),
                    CsvCell(p.ReplacedDate ?? ""),
                    CsvCell(p.ReplacedLocation),
                    CsvCell(p.Remarks),
                    CsvCell(p.CreatedAt),
                }));
            }

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            return File(bytes, "text/csv", "printers_export.csv");
        }

        private static string CsvCell(string v)
        {
            if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }
    }
}
